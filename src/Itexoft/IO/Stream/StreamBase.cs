// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.CompilerServices;
using Itexoft.Core;
using Itexoft.Net.Core;
using Itexoft.Reflection;
using Itexoft.Threading;
using Itexoft.Threading.ControlFlow;

namespace Itexoft.IO;

public abstract class StreamBase : IStream
{
    private readonly Deferred<Stream> wrapper;
    private Disposed disposed;

    protected StreamBase() => this.wrapper = new(() => new ToBclStreamWrapper(this));
    private protected StreamBase(Stream stream) => this.wrapper = new(() => stream);

    public async ValueTask DisposeAsync()
    {
        if (this.disposed.Enter())
            return;

        GC.SuppressFinalize(this);

        var task = this.DisposeAny();

        if (this.wrapper.Dispose(out var stream) && stream is not null)
            await stream.DisposeAsync();

        await task;
    }

    public void Dispose()
    {
        if (this.disposed.Enter())
            return;

        GC.SuppressFinalize(this);

        var task = this.DisposeAny();

        if (this.wrapper.Dispose(out var stream) && stream is not null)
            stream.Dispose();

        if (!task.IsCompletedSuccessfully)
            task.GetAwaiter().GetResult();
    }

    protected void ThrowIfDisposed() => this.disposed.ThrowIf();

    protected abstract ValueTask DisposeAny();

    public static implicit operator Stream(StreamBase stream) => stream.wrapper.Value;
}

public abstract class StreamBase<T> : StreamBase, IStream<T> where T : unmanaged
{
    protected StreamBase()
    {
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
            throw new NotSupportedException();
    }
}

public static class StreamExtensions
{
    private static async ValueTask<int> ReadAtLeastAsyncCore(IStreamRa stream, Memory<byte> buffer, int minimumBytes, CancelToken cancelToken)
    {
        var totalRead = 0;

        while (totalRead < minimumBytes)
        {
            var read = await stream.ReadAsync(buffer[totalRead..], cancelToken).ConfigureAwait(false);

            if (read == 0)
                break;

            totalRead += read;
        }

        return totalRead;
    }

    public static IStreamBcl AsBclStream(this Stream stream) => new FromBclStreamWrapper(stream);
    public static INetStream AsINetStream(this Stream stream) => new FromBclStreamWrapper(stream);

    extension(IStreamR stream)
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Read(byte[] buffer, int offset, int count)
        {
            if (buffer is null)
                throw new ArgumentNullException(nameof(buffer));

            if (offset > buffer.Length)
                throw new ArgumentOutOfRangeException(nameof(offset));

            if (count > buffer.Length - offset)
                throw new ArgumentOutOfRangeException(nameof(count));

            return stream.Read(buffer.AsSpan(offset, count));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ReadByte()
        {
            Span<byte> one = stackalloc byte[1];
            var read = stream.Read(one);

            return read == 0 ? -1 : one[0];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ReadExactly(Span<byte> buffer) => stream.ReadAtLeast(buffer, buffer.Length, true);

        public int ReadAtLeast(Span<byte> buffer, int minimumBytes, bool throwOnEndOfStream = true)
        {
            if ((uint)minimumBytes > (uint)buffer.Length)
                throw new ArgumentOutOfRangeException(nameof(minimumBytes));

            if (minimumBytes == 0)
                return 0;

            var totalRead = 0;

            while (totalRead < minimumBytes)
            {
                var read = stream.Read(buffer.Slice(totalRead));

                if (read == 0)
                    break;

                totalRead += read;
            }

            if (totalRead < minimumBytes && throwOnEndOfStream)
                throw new EndOfStreamException();

            return totalRead;
        }
    }

    extension(IStreamW stream)
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Write(byte[] buffer, int offset, int count)
        {
            if (buffer is null)
                throw new ArgumentNullException(nameof(buffer));

            if (offset > buffer.Length)
                throw new ArgumentOutOfRangeException(nameof(offset));

            if (count > (uint)(buffer.Length - offset))
                throw new ArgumentOutOfRangeException(nameof(count));

            stream.Write(buffer.AsSpan(offset, count));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteByte(byte value) => stream.Write([value]);
    }

    extension(IStreamRa stream)
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ValueTask<int> ReadExactAsync(Memory<byte> buffer, CancelToken cancelToken = default)
        {
            if (buffer.Length == 0)
                return new(0);

            return ReadAtLeastAsyncCore(stream, buffer, buffer.Length, cancelToken);
        }

        public ValueTask<int> ReadAtLeastAsync(Memory<byte> buffer, int minimumBytes, CancelToken cancelToken = default)
        {
            if (minimumBytes < 0 || minimumBytes > buffer.Length)
                throw new ArgumentOutOfRangeException(nameof(minimumBytes));

            if (minimumBytes == 0 || buffer.Length == 0)
                return new(0);

            return ReadAtLeastAsyncCore(stream, buffer, minimumBytes, cancelToken);
        }
    }

    extension<T>(T stream) where T : class, IStream
    {
        public Stream AsStream()
        {
            if (stream is StreamBase streamBase)
                return streamBase;

            return new ToBclStreamWrapper(stream);
        }

        public T Overlay(T overlay) => Interfaces.Overlay(stream, overlay);
    }
}
