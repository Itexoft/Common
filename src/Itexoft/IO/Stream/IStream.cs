// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Itexoft.Extensions;
using Itexoft.Threading;

namespace Itexoft.IO;

public interface IStream;

public interface IStream<T> : IStream where T : unmanaged;

public interface ISyncStream : IStream, IDisposable;

public interface ISyncStream<T> : ISyncStream, IStream<T> where T : unmanaged;

public interface IAsyncStream : IStream, IAsyncDisposable;

public interface IAsyncStream<T> : IAsyncStream, IStream<T> where T : unmanaged;

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

public interface IStreamR<T> : IStreamR, ISyncStream<T> where T : unmanaged
{
    unsafe int IStreamR.Read(Span<byte> destination)
    {
        if (destination.IsEmpty)
            return 0;

        var elementSize = sizeof(T);

        if (destination.Length % elementSize != 0)
            throw new ArgumentOutOfRangeException(nameof(destination));

        var typed = MemoryMarshal.Cast<byte, T>(destination);
        var read = this.Read(typed);

        return read * elementSize;
    }

    int Read(Span<T> destination);
}

public interface IStreamW : ISyncStream
{
    void Flush();
    void Write(ReadOnlySpan<byte> buffer);
}

public interface IStreamW<T> : IStreamW, ISyncStream<T> where T : unmanaged
{
    unsafe void IStreamW.Write(ReadOnlySpan<byte> buffer)
    {
        if (buffer.IsEmpty)
            return;

        var elementSize = sizeof(T);

        if (buffer.Length % elementSize != 0)
            throw new ArgumentOutOfRangeException(nameof(buffer));

        var typed = MemoryMarshal.Cast<byte, T>(buffer);
        this.Write(typed);
    }

    void Write(ReadOnlySpan<T> source);
}

public interface IStreamRw : IStreamR, IStreamW;

public interface IStreamRw<T> : IStreamRw, IStreamR<T>, IStreamW<T> where T : unmanaged;

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

public interface IStreamRa<T> : IStreamRa, IAsyncStream<T> where T : unmanaged
{
    async ValueTask<int> IStreamRa.ReadAsync(Memory<byte> destination, CancelToken cancelToken = default)
    {
        if (destination.IsEmpty)
            return 0;

        var elementSize = Unsafe.SizeOf<T>();

        if (destination.Length % elementSize != 0)
            throw new ArgumentOutOfRangeException(nameof(destination));

        var elementCount = destination.Length / elementSize;

        var buffer = ArrayPool<T>.Shared.Rent(elementCount);

        try
        {
            var read = await this.ReadAsync(buffer.AsMemory(0, elementCount), cancelToken);
            var bytes = MemoryMarshal.AsBytes(buffer.AsSpan(0, read));
            bytes.CopyTo(destination.Span);

            return read * elementSize;
        }
        finally
        {
            ArrayPool<T>.Shared.Return(buffer);
        }
    }

    ValueTask<int> ReadAsync(Memory<T> destination, CancelToken cancelToken = default);
}

public interface IStreamWa : IAsyncStream
{
    ValueTask FlushAsync(CancelToken cancelToken = default);
    ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancelToken cancelToken = default);
}

public interface IStreamWa<T> : IStreamWa, IAsyncStream<T> where T : unmanaged
{
    async ValueTask IStreamWa.WriteAsync(ReadOnlyMemory<byte> value, CancelToken cancelToken = default)
    {
        if (value.IsEmpty)
            return;

        var elementSize = Unsafe.SizeOf<T>();

        if (value.Length % elementSize != 0)
            throw new ArgumentOutOfRangeException(nameof(value));

        var elementCount = value.Length / elementSize;

        var buffer = ArrayPool<T>.Shared.Rent(elementCount);

        try
        {
            var span = buffer.AsSpan(0, elementCount);
            var bytes = MemoryMarshal.AsBytes(span);
            value.Span[..(elementCount * elementSize)].CopyTo(bytes);
            await this.WriteAsync(buffer.AsMemory(0, elementCount), cancelToken);
        }
        finally
        {
            ArrayPool<T>.Shared.Return(buffer);
        }
    }

    ValueTask WriteAsync(ReadOnlyMemory<T> value, CancelToken cancelToken = default);
}

public interface IStreamRwa : IStreamRa, IStreamWa;

public interface IStreamRwa<T> : IStreamRwa, IStreamRa<T>, IStreamWa<T> where T : unmanaged;

public interface IRewindStreamRa : IStreamRa;

public interface IRewindStreamRa<T> : IRewindStreamRa, IStreamRa<T> where T : unmanaged;

public interface IStreamTr : IStream
{
    TimeSpan ReadTimeout { get; set; }
}

public interface IStreamTr<T> : IStreamTr, IStream<T> where T : unmanaged { }

public interface IStreamTw : IStream
{
    TimeSpan WriteTimeout { get; set; }
}

public interface IStreamTw<T> : IStreamTw, IStream<T> where T : unmanaged { }

public interface IStreamRt : IStreamR, IStreamTr;

public interface IStreamRt<T> : IStreamRt, IStreamR<T>, IStreamTr<T> where T : unmanaged;

public interface IStreamWt : IStreamW, IStreamTw;

public interface IStreamWt<T> : IStreamWt, IStreamW<T>, IStreamTw<T> where T : unmanaged;

public interface IStreamRta : IStreamRa, IStreamTr;

public interface IStreamRta<T> : IStreamRta, IStreamRa<T>, IStreamTr<T> where T : unmanaged;

public interface IStreamWta : IStreamWa, IStreamTw;

public interface IStreamWta<T> : IStreamWta, IStreamWa<T>, IStreamTw<T> where T : unmanaged;

public interface IStreamRwt : IStreamWt, IStreamRt;

public interface IStreamRwt<T> : IStreamRwt, IStreamWt<T>, IStreamRt<T> where T : unmanaged;

public interface IStreamRwta : IStreamWta, IStreamRta, IStreamRwa;

public interface IStreamRwta<T> : IStreamRwta, IStreamWta<T>, IStreamRta<T>, IStreamRwa<T> where T : unmanaged;

public interface IStreamL : IStream
{
    long Length { get; }
    long Position { get; }
}

public unsafe interface IStreamL<T> : IStreamL, IStream<T> where T : unmanaged
{
    new long Length => ((IStreamL)this).Length / sizeof(T);
    new long Position => ((IStreamL)this).Position / sizeof(T);
}

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

public unsafe interface IStreamS<T> : IStreamS, IStreamL<T> where T : unmanaged
{
    public new long Position
    {
        get => ((IStreamS)this).Seek(0, SeekOrigin.Current) / sizeof(T);
        set => ((IStreamS)this).Seek(value * sizeof(T), SeekOrigin.Begin);
    }

    long IStreamL<T>.Position => this.Position;

    public new long Seek(long offset, SeekOrigin origin) => ((IStreamS)this).Seek(offset * sizeof(T), origin);
}

public interface IStreamSl : IStreamS
{
    void SetLength(long value);
}

public unsafe interface IStreamSl<T> : IStreamSl, IStreamS<T> where T : unmanaged
{
    new void SetLength(long value) => ((IStreamSl)this).SetLength(value * sizeof(T));
}

public interface IStreamRal : IStreamRa, IStreamL;

public interface IStreamRal<T> : IStreamRal, IStreamRa<T>, IStreamL<T> where T : unmanaged;

public interface IStreamWal : IStreamWa, IStreamL;

public interface IStreamWal<T> : IStreamWal, IStreamWa<T>, IStreamL<T> where T : unmanaged;

public interface IStreamRl : IStreamR, IStreamL;

public interface IStreamRl<T> : IStreamRl, IStreamR<T>, IStreamL<T> where T : unmanaged;

public interface IStreamRals : IStreamRal, IStreamS;

public interface IStreamRals<T> : IStreamRals, IStreamRal<T>, IStreamS<T> where T : unmanaged;

public interface IStreamWals : IStreamWal, IStreamS;

public interface IStreamWals<T> : IStreamWals, IStreamWal<T>, IStreamS<T> where T : unmanaged;

public interface IStreamSlrwa : IStreamRwa, IStreamSl, IStreamRals, IStreamWals;

public interface IStreamSlrwa<T> : IStreamSlrwa, IStreamRwa<T>, IStreamSl<T>, IStreamRals<T>, IStreamWals<T> where T : unmanaged;

public interface IStreamSlrw : IStreamRw, IStreamSl;

public interface IStreamSlrw<T> : IStreamSlrw, IStreamRw<T>, IStreamSl<T> where T : unmanaged;

public interface IStreamBcl : IStreamRwta, IStreamSl, IStreamRals, IStreamWals, IStreamRw
{
    bool CanRead { get; }
    bool CanSeek { get; }
    bool CanWrite { get; }
    bool CanTimeout { get; }
    ValueTask IStreamR.CopyToAsync(IStreamRwa streamRwa, CancelToken cancelToken) => ((IStreamR)this).CopyToAsync(streamRwa, cancelToken);
    new ValueTask CopyToAsync(IStreamRwa streamRwa, CancelToken cancelToken) => ((IStreamR)this).CopyToAsync(streamRwa, cancelToken);
}

public interface IStreamBcl<T> : IStreamBcl, IStreamRwta<T>, IStreamSl<T>, IStreamRals<T>, IStreamWals<T>, IStreamRw<T> where T : unmanaged { }
