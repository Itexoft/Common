// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Buffers;

namespace Itexoft.IO.VFS.Cache;

internal sealed class PageCache
{
    private readonly int pageSize;
    private readonly ArrayPool<byte> pool = ArrayPool<byte>.Shared;
    private int leased;

    public PageCache(int pageSize) => this.pageSize = pageSize;

    public PageLease Lease()
    {
        var buffer = this.pool.Rent(this.pageSize);
        Interlocked.Increment(ref this.leased);

        return new(buffer, this.pageSize, this);
    }

    private void Return(byte[] buffer)
    {
        this.pool.Return(buffer, false);
        Interlocked.Decrement(ref this.leased);
    }

    internal readonly struct PageLease : IDisposable
    {
        private readonly byte[] buffer;
        private readonly int length;
        private readonly PageCache owner;

        public PageLease(byte[] buffer, int length, PageCache owner)
        {
            this.buffer = buffer;
            this.length = length;
            this.owner = owner;
        }

        public Span<byte> Span => this.buffer.AsSpan(0, this.length);
        public Memory<byte> Memory => this.buffer.AsMemory(0, this.length);

        public void Dispose() => this.owner.Return(this.buffer);
    }
}