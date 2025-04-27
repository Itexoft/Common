// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.IO.VFS;
using Itexoft.IO.VFS.Storage;

namespace Itexoft.Tests.IO.VFS;

[TestFixture]
internal sealed class DebugReproTests
{
    [Test]
    public void DirectStorageWrite_ShouldPersistData()
    {
        using var root = new TestMemoryStream();
        using var outer = VirtualFileSystem.Mount(root, new() { EnableCompaction = false });
        outer.CreateDirectory("layers");
        using var container = outer.OpenFile("layers/container.vfs", FileMode.OpenOrCreate, FileAccess.ReadWrite);
        container.SetLength(0);

        var engine = StorageEngine.Open(container, outer.PageSize);
        var pageSize = outer.PageSize;
        var buffer = new byte[pageSize];
        buffer[0] = 0x02;
        engine.WritePage(new(6), buffer);

        var verify = new byte[pageSize];
        engine.ReadPhysicalPageUnsafe(6, verify);
        Assert.That(verify[0], Is.EqualTo(0x02));
    }
}
