// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Buffers;
using Itexoft.Extensions;
using Itexoft.Threading;

namespace Itexoft.IO;

public interface IStream;

public interface IRewindStreamRa : IStreamRa;

public interface ISyncStream : IStream, IDisposable;

public interface IAsyncStream : IStream, IAsyncDisposable;

public interface IStreamR : ISyncStream
{
    int Read(Span<byte> buffer);

    async ValueTask CopyToAsync(IStreamRwa streamRwa, CancelToken cancelToken = default)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(ushort.MaxValue);

        try
        {
            var memory = buffer.AsMemory();

            while (true)
            {
                var read = this.Read(memory.Span);
                if (read <= 0)
                    break;

                await streamRwa.WriteAsync(memory[..read], cancelToken);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
    
    byte[] ToArray()
    {
        if (this is IStreamS streamS && (this is not IStreamBcl bcl || bcl.CanSeek))
        {
            var start = streamS.Position;
            streamS.Seek(0, SeekOrigin.Begin);

            try
            {
                return ReadAllRemaining(this);
            }
            finally
            {
                streamS.Seek(start, SeekOrigin.Begin);
            }
        }

        return ReadAllRemaining(this);
    }

    private static byte[] ReadAllRemaining(IStreamR stream)
    {
        if (stream is IStreamL streamL)
        {
            var remaining = streamL.Length - streamL.Position;
            if (remaining <= int.MaxValue)
            {
                var result = GC.AllocateUninitializedArray<byte>((int)remaining);
                var offset = 0;
                var length = (int)remaining;

                while (offset < length)
                {
                    var read = stream.Read(result.AsSpan(offset, length - offset));
                    if (read <= 0)
                        break;
                    offset += read;
                }

                return offset == length ? result : result.AsSpan(0, offset).ToArray();
            }
        }

        var writer = new ArrayBufferWriter<byte>(ushort.MaxValue);
        while (true)
        {
            var read = stream.Read(writer.GetSpan());
            if (read <= 0)
                break;
            writer.Advance(read);
        }

        return writer.WrittenSpan.ToArray();
    }
}

public interface IStreamW : ISyncStream
{
    void Flush();
    void Write(ReadOnlySpan<byte> buffer);
}

public interface IStreamRw : IStreamR, IStreamW;

public interface IStreamTr : IStream
{
    TimeSpan ReadTimeout { get; set; }
}

public interface IStreamTw : IStream
{
    TimeSpan WriteTimeout { get; set; }
}

public interface IStreamRt : IStreamR, IStreamTr;

public interface IStreamWt : IStreamW, IStreamTw;

public interface IStreamRta : IStreamRa, IStreamTr;

public interface IStreamWta : IStreamWa, IStreamTw;

public interface IStreamRwt : IStreamWt, IStreamRt;

public interface IStreamRwta : IStreamWta, IStreamRta, IStreamRwa;

public interface IStreamRwa : IStreamRa, IStreamWa;

public interface IStreamWa : IAsyncStream
{
    ValueTask FlushAsync(CancelToken cancelToken = default);
    ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancelToken cancelToken = default);
}



public interface IStreamRa : IAsyncStream
{
    ValueTask<int> ReadAsync(Memory<byte> buffer, CancelToken cancelToken = default);

    async ValueTask<int> ReadByteAsync(CancelToken cancelToken = default)
    {
        var bytes = ArrayPool<byte>.Shared.Rent(1);

        try
        {
            return await this.ReadAsync(bytes.AsMemory(), cancelToken);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(bytes);
        }
    }

    async ValueTask<Memory<byte>> ReadBytesAsync(int count, CancelToken cancelToken = default)
    {
        if (count == 0)
            return Memory<byte>.Empty;

        var bytes = new byte[count.RequiredGreater(0)].AsMemory();
        count = await this.ReadAsync(bytes, cancelToken);

        return bytes[..count.RequiredPositiveOrZero()];
    }

    async ValueTask CopyToAsync(IStreamRwa streamRwa, CancelToken cancelToken = default)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(ushort.MaxValue);

        try
        {
            var memory = buffer.AsMemory();

            while (true)
            {
                var read = await this.ReadAsync(memory, cancelToken);
                if (read <= 0)
                    break;
                await streamRwa.WriteAsync(memory[..read], cancelToken);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    async ValueTask CopyToAsync(IStreamRw streamRw, CancelToken cancelToken = default)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(ushort.MaxValue);

        try
        {
            var memory = buffer.AsMemory();

            while (true)
            {
                var read = await this.ReadAsync(memory, cancelToken);
                if (read <= 0)
                    break;
                streamRw.Write(memory.Span[..read]);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}

public interface IStreamL : IStream
{
    long Length { get; }
    long Position { get; }
}

public interface IStreamRal : IStreamRa, IStreamL;
public interface IStreamWal : IStreamWa, IStreamL;
public interface IStreamRl : IStreamR, IStreamL;
public interface IStreamRals : IStreamRal, IStreamS;
public interface IStreamWals : IStreamWal, IStreamS;

public interface IStreamS : IStreamL
{
    public new long Position
    {
        get => this.Seek(0, SeekOrigin.Current);
        set => this.Seek(value, SeekOrigin.Begin);
    }

    long IStreamL.Position => this.Position;
    public long Seek(long offset, SeekOrigin origin);
}

public interface IStreamSl : IStreamS
{
    void SetLength(long value);
}

public interface IStreamSlrwa : IStreamRwa, IStreamSl, IStreamRals, IStreamWals;
public interface IStreamSlrw : IStreamRw,IStreamSl;

public interface IStreamBcl : IStreamRwta, IStreamSl, IStreamRals, IStreamWals, IStreamRw
{
    bool CanRead { get; }
    bool CanSeek { get; }
    bool CanWrite { get; }
    bool CanTimeout { get; }
    new ValueTask CopyToAsync(IStreamRwa streamRwa, CancelToken cancelToken) => ((IStreamR)this).CopyToAsync(streamRwa, cancelToken);
    ValueTask IStreamR.CopyToAsync(IStreamRwa streamRwa, CancelToken cancelToken) => ((IStreamR)this).CopyToAsync(streamRwa, cancelToken);
}
