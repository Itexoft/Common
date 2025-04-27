// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.IO.VFS;

namespace Itexoft.Tests.IO.VFS;

[TestFixture]
internal sealed class VirtualFileSystemNestedSingleFileTests
{
    [Test]
    public void NestedSingleFile_SingleThread_ShouldRoundtrip()
    {
        const int layerCount = 4;
        var root = new TestMemoryStream();

        var fileSystems = new VirtualFileSystem[layerCount];
        Stream current = root;

        for (var layer = 0; layer < layerCount; layer++)
        {
            var vfs = VirtualFileSystem.Mount(current, new() { EnableCompaction = false });
            fileSystems[layer] = vfs;

            if (layer < layerCount - 1)
            {
                vfs.CreateDirectory("layers");
                var containerPath = $"layers/layer_{layer}.vfs";
                var container = vfs.OpenFile(containerPath, FileMode.OpenOrCreate, FileAccess.ReadWrite);
                container.SetLength(0);
                current = container;
            }
        }

        var innermost = fileSystems[^1];
        innermost.CreateDirectory("data");
        const string path = "data/single.bin";
        var payload = Enumerable.Repeat((byte)0x2A, 2048).ToArray();

        using (var stream = innermost.OpenFile(path, FileMode.OpenOrCreate, FileAccess.ReadWrite))
        {
            stream.SetLength(0);
            stream.Write(payload, 0, payload.Length);
            stream.Flush();
        }

        using (var stream = innermost.OpenFile(path, FileMode.Open, FileAccess.Read))
        {
            var buffer = new byte[payload.Length];
            var read = stream.Read(buffer, 0, buffer.Length);
            Assert.That(read, Is.EqualTo(buffer.Length));
            Assert.That(buffer, Is.EqualTo(payload));
        }
    }
}
