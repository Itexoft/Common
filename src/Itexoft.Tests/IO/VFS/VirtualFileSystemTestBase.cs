// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.IO.VFS;

namespace Itexoft.Tests.IO.VFS;

internal abstract class VirtualFileSystemTestBase(TestMode mode)
{
    protected TestMode Mode { get; } = mode;

    protected VirtualFileSystemScope MountFileSystem(Func<VirtualFileSystemOptions>? configure = null, ReadOnlySpan<byte> initialPrimary = default) =>
        TestContainerFactory.Mount(this.Mode, configure ?? (() => new() { EnableMirroring = this.Mode.EnableMirroring }), initialPrimary);

    protected void WriteAll(Stream stream, byte[] payload) => this.Mode.WriteAll(stream, payload);

    protected void WriteAll(Stream stream, ReadOnlySpan<byte> payload) => this.Mode.WriteAll(stream, payload);

    protected int ReadExact(Stream stream, Span<byte> destination) => this.Mode.ReadExact(stream, destination);
}
