// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Buffers;
using System.Runtime.CompilerServices;

namespace Itexoft.Networking.P2P;

/// <summary>
/// Stream-based control channel that serialises frames using a compact header + varint payload length.
/// </summary>
internal sealed class SimpleStreamControlChannel : IP2PControlChannel
{
    private const int HeaderSize = sizeof(byte) + sizeof(long) + sizeof(long);

    private readonly Stream _stream;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public SimpleStreamControlChannel(Stream stream) => this._stream = stream ?? throw new ArgumentNullException(nameof(stream));

    public async ValueTask SendAsync(P2PControlFrame frame, CancellationToken cancellationToken)
    {
        await this._writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var payloadLength = frame.Payload.Length;
            var headerBuffer = ArrayPool<byte>.Shared.Rent(HeaderSize + sizeof(int));
            try
            {
                var span = headerBuffer.AsSpan();
                span[0] = (byte)frame.Type;
                BitConverter.TryWriteBytes(span.Slice(1), frame.TimestampUtcTicks);
                BitConverter.TryWriteBytes(span.Slice(1 + sizeof(long)), frame.Sequence);
                var lengthSpan = span.Slice(HeaderSize);
                var written = WriteVarInt(lengthSpan, payloadLength);

                await this._stream.WriteAsync(headerBuffer.AsMemory(0, HeaderSize + written), cancellationToken).ConfigureAwait(false);
                if (payloadLength > 0)
                    await this._stream.WriteAsync(frame.Payload, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(headerBuffer);
            }
        }
        finally
        {
            this._writeLock.Release();
        }
    }

    public async IAsyncEnumerable<P2PControlFrame> ReadFramesAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var header = new byte[HeaderSize];
        while (true)
        {
            await this._stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
            var type = (P2PControlFrameType)header[0];
            var timestamp = BitConverter.ToInt64(header, 1);
            var sequence = BitConverter.ToInt64(header, 1 + sizeof(long));

            var length = await ReadVarIntAsync(this._stream, cancellationToken).ConfigureAwait(false);
            var payload = ReadOnlyMemory<byte>.Empty;
            if (length > 0)
            {
                var buffer = new byte[length];
                await this._stream.ReadExactlyAsync(buffer, cancellationToken).ConfigureAwait(false);
                payload = buffer;
            }

            yield return new(type, timestamp, sequence, payload);
        }

        // ReSharper disable once IteratorNeverReturns
    }

    public async ValueTask FlushAsync(CancellationToken cancellationToken)
    {
        await this._writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await this._stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            this._writeLock.Release();
        }
    }

    private static int WriteVarInt(Span<byte> destination, int value)
    {
        var index = 0;
        var current = (uint)value;
        while (current >= 0x80)
        {
            destination[index++] = (byte)(current | 0x80);
            current >>= 7;
        }

        destination[index++] = (byte)current;

        return index;
    }

    private static async ValueTask<int> ReadVarIntAsync(Stream stream, CancellationToken cancellationToken)
    {
        var result = 0;
        var shift = 0;
        var buffer = new byte[1];
        while (true)
        {
            await stream.ReadExactlyAsync(buffer, cancellationToken).ConfigureAwait(false);
            var b = buffer[0];
            result |= (b & 0x7F) << shift;

            if (b < 0x80)
                return result;

            shift += 7;

            if (shift > 35)
                throw new InvalidOperationException("Varint length is too large.");
        }
    }
}