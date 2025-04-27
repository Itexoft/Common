// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Buffers;
using Itexoft.Extensions;
using Itexoft.Threading;

namespace Itexoft.IO;

public sealed class RewindStream(IStreamRa inner, int initialCapacity = 256, bool leaveOpen = false) : StreamBase, IRewindStreamRa
{
    private readonly int initialCapacity = initialCapacity.RequiredPositiveOrZero();
    private readonly IStreamRa inner = inner.Required();
    private byte[] buffer = [];

    public long Position { get; private set; }

    public int BufferedLength { get; private set; }

    public bool IsBuffering { get; private set; } = true;

    public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancelToken cancelToken = default)
    {
        var position = this.Position;

        try
        {
            return await this.ReadAsyncCore(buffer, cancelToken).ConfigureAwait(false);
        }
        finally
        {
            this.Position = position;
        }
    }

    private async ValueTask<int> ReadAsyncCore(Memory<byte> buffer, CancelToken cancelToken = default)
    {
        if (buffer.Length == 0)
            return 0;

        var readFromBuffer = this.TryReadBuffered(buffer);

        if (readFromBuffer == buffer.Length)
            return readFromBuffer;

        var read = await this.inner.ReadAsync(buffer[readFromBuffer..], cancelToken).ConfigureAwait(false);

        if (read <= 0)
            return readFromBuffer == 0 ? read : readFromBuffer;

        if (this.IsBuffering)
            this.Append(buffer.Span.Slice(readFromBuffer, read));

        this.Position += read;

        return readFromBuffer + read;
    }

    public void StopBuffering()
    {
        this.IsBuffering = false;
        this.Position = 0;
    }

    protected async override ValueTask DisposeAny()
    {
        if (this.buffer.Length != 0)
        {
            ArrayPool<byte>.Shared.Return(this.buffer);
            this.buffer = [];
            this.BufferedLength = 0;
        }

        if (!leaveOpen)
            await this.inner.DisposeAsync().ConfigureAwait(false);
    }

    private int TryReadBuffered(Memory<byte> destination)
    {
        if (this.Position >= this.BufferedLength)
            return 0;

        var offset = (int)this.Position;
        var available = this.BufferedLength - offset;
        var toCopy = Math.Min(available, destination.Length);

        this.buffer.AsMemory(offset, toCopy).CopyTo(destination);
        this.Position += toCopy;

        return toCopy;
    }

    private void Append(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0)
            return;

        this.EnsureCapacity(data.Length);
        data.CopyTo(this.buffer.AsSpan(this.BufferedLength));
        this.BufferedLength += data.Length;
    }

    private void EnsureCapacity(int additional)
    {
        if (this.buffer.Length == 0)
        {
            var size = Math.Max(Math.Max(this.initialCapacity, additional), 8);
            this.buffer = ArrayPool<byte>.Shared.Rent(size);

            return;
        }

        if (this.BufferedLength + additional <= this.buffer.Length)
            return;

        var required = this.BufferedLength + additional;
        var newSize = this.buffer.Length * 2;

        if (newSize < required)
            newSize = required;

        var newBuffer = ArrayPool<byte>.Shared.Rent(newSize);
        this.buffer.AsSpan(0, this.BufferedLength).CopyTo(newBuffer);
        ArrayPool<byte>.Shared.Return(this.buffer);
        this.buffer = newBuffer;
    }
}
