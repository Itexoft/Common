// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Buffers;
using Itexoft.IO;
using Itexoft.Threading;

namespace Itexoft.Net.Http.Internal;

internal sealed class NetHttpBufferedReader(IStreamRa inner, int bufferSize) : IAsyncDisposable
{
    private readonly IStreamRa inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private byte[] buffer = ArrayPool<byte>.Shared.Rent(Math.Max(bufferSize, 256));
    private int count;
    private int offset;

    public async ValueTask DisposeAsync()
    {
        if (this.buffer.Length != 0)
        {
            ArrayPool<byte>.Shared.Return(this.buffer);
            this.buffer = Array.Empty<byte>();
        }
    }

    public async ValueTask<int> ReadAsync(Memory<byte> destination, CancelToken cancelToken = default)
    {
        if (destination.Length == 0)
            return 0;

        var available = this.count - this.offset;

        if (available > 0)
        {
            var toCopy = Math.Min(available, destination.Length);
            this.buffer.AsMemory(this.offset, toCopy).CopyTo(destination);
            this.offset += toCopy;

            if (this.offset == this.count)
                this.resetBuffer();

            if (toCopy == destination.Length)
                return toCopy;

            var read = await this.inner.ReadAsync(destination[toCopy..], cancelToken).ConfigureAwait(false);

            return toCopy + read;
        }

        return await this.inner.ReadAsync(destination, cancelToken).ConfigureAwait(false);
    }

    public async ValueTask ReadExactAsync(Memory<byte> destination, CancelToken cancelToken = default)
    {
        var total = 0;

        while (total < destination.Length)
        {
            var read = await this.ReadAsync(destination[total..], cancelToken).ConfigureAwait(false);

            if (read == 0)
                throw new EndOfStreamException();

            total += read;
        }
    }

    public async ValueTask<ReadOnlyMemory<byte>> ReadHeadersAsync(int maxHeaderSize, CancelToken cancelToken = default)
    {
        while (true)
        {
            var span = this.buffer.AsSpan(this.offset, this.count - this.offset);
            var idx = NetHttpParsing.IndexOfHeaderTerminator(span);

            if (idx >= 0)
            {
                var headerLength = idx + 4;
                var result = this.buffer.AsMemory(this.offset, headerLength);
                this.offset += headerLength;

                if (this.offset == this.count)
                    this.resetBuffer();

                return result;
            }

            if (this.count - this.offset >= maxHeaderSize)
                throw new IOException("HTTP headers are too large.");

            await this.FillAsync(cancelToken).ConfigureAwait(false);
        }
    }

    public async ValueTask<ReadOnlyMemory<byte>> ReadLineAsync(int maxLineSize, CancelToken cancelToken = default)
    {
        while (true)
        {
            var span = this.buffer.AsSpan(this.offset, this.count - this.offset);
            var idx = IndexOfCrlf(span);

            if (idx >= 0)
            {
                var result = this.buffer.AsMemory(this.offset, idx);
                this.offset += idx + 2;

                if (this.offset == this.count)
                    this.resetBuffer();

                return result;
            }

            if (this.count - this.offset >= maxLineSize)
                throw new IOException("HTTP line is too large.");

            await this.FillAsync(cancelToken).ConfigureAwait(false);
        }
    }

    private async ValueTask FillAsync(CancelToken cancelToken)
    {
        if (this.count == this.buffer.Length)
        {
            if (this.offset > 0)
            {
                Buffer.BlockCopy(this.buffer, this.offset, this.buffer, 0, this.count - this.offset);
                this.count -= this.offset;
                this.offset = 0;
            }
            else
            {
                var newBuffer = ArrayPool<byte>.Shared.Rent(this.buffer.Length * 2);
                Buffer.BlockCopy(this.buffer, 0, newBuffer, 0, this.count);
                ArrayPool<byte>.Shared.Return(this.buffer);
                this.buffer = newBuffer;
            }
        }

        var read = await this.inner.ReadAsync(this.buffer.AsMemory(this.count), cancelToken).ConfigureAwait(false);

        if (read == 0)
            throw new EndOfStreamException();

        this.count += read;
    }

    private static int IndexOfCrlf(ReadOnlySpan<byte> span)
    {
        for (var i = 0; i + 1 < span.Length; i++)
        {
            if (span[i] == (byte)'\r' && span[i + 1] == (byte)'\n')
                return i;
        }

        return -1;
    }

    private void resetBuffer()
    {
        this.offset = 0;
        this.count = 0;
    }
}
