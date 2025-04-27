// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.IO.VFS.Diagnostics;

namespace Itexoft.IO.VFS.Tests;

[TestFixture]
internal sealed class VirtualFileSystemNestedSingleFileTests
{
    [Test]
    public void NestedSingleFile_SingleThread_ShouldRoundtrip()
    {
        const int layerCount = 4;
        var root = new TestMemoryStream();

        var fileSystems = new VirtualFileSystem[layerCount];
        var hostStreams = new Stream[layerCount - 1];
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
                hostStreams[layer] = container;
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
            if (DebugUtility.Enabled && !buffer.AsSpan().SequenceEqual(payload))
            {
                DebugUtility.Log($"Mismatch in single threaded nested test: expected 0x{payload[0]:X2}, actual 0x{buffer[0]:X2}");
                var fileId = innermost.ResolveDebugFileId(path);
                if (fileId.IsValid)
                {
                    var meta = innermost.GetFileMetadata(fileId);
                    foreach (var extent in meta.Extents)
                    {
                        DebugUtility.Log($"extent [{extent.Start.Value}..{extent.EndExclusive})");
                        for (var page = extent.Start.Value; page < extent.EndExclusive; page++)
                            DebugUtility.Log($"    tracker[{page}] = {innermost.DescribeDebugPage(page)}");
                    }
                }

                Assert.That(buffer, Is.EqualTo(payload));
            }

            Assert.That(buffer, Is.EqualTo(payload));
        }

        // Осознанно не диспоузим вложенные слои для упрощения отладки:
        // порядок освобождения сложный и не влияет на суть теста.
    }
}