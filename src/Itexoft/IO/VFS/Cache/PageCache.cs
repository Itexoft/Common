// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Buffers;

namespace Itexoft.IO.VFS.Cache;

internal sealed class PageCache(int pageSize)
{
    private readonly ArrayPool<byte> pool = ArrayPool<byte>.Shared;
    private int leased;

    public PageLease Lease()
    {
        var buffer = this.pool.Rent(pageSize);
        Interlocked.Increment(ref this.leased);

        return new(buffer, pageSize, this);
    }

    private void Return(byte[] buffer)
    {
        this.pool.Return(buffer, false);
        Interlocked.Decrement(ref this.leased);
    }

    internal readonly struct PageLease(byte[] buffer, int length, PageCache owner) : IDisposable
    {
        public Span<byte> Span => buffer.AsSpan(0, length);
        public Memory<byte> Memory => buffer.AsMemory(0, length);

        public void Dispose() => owner.Return(buffer);
    }
}
