// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Buffers;
using Itexoft.Extensions;

namespace Itexoft.Tests.IO.VFS;

internal sealed class TestMemoryStream : Stream
{
    private readonly object syncRoot = new();
    private byte[] buffer;
    private bool disposed;
    private long length;
    private long position;

    public TestMemoryStream(int initialCapacity = 64 * 1024)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(initialCapacity);
        this.buffer = ArrayPool<byte>.Shared.Rent(initialCapacity);
    }

    public TestMemoryStream(ReadOnlySpan<byte> initialData) : this(Math.Max(initialData.Length, 1))
    {
        if (initialData.Length > 0)
        {
            initialData.CopyTo(this.buffer);
            this.length = initialData.Length;
        }
    }

    /// <inheritdoc />
    public override bool CanRead => true;

    /// <inheritdoc />
    public override bool CanSeek => true;

    /// <inheritdoc />
    public override bool CanWrite => true;

    /// <inheritdoc />
    public override long Length
    {
        get
        {
            lock (this.syncRoot)
            {
                this.ThrowIfDisposed();

                return this.length;
            }
        }
    }

    /// <inheritdoc />
    public override long Position
    {
        get
        {
            lock (this.syncRoot)
            {
                this.ThrowIfDisposed();

                return this.position;
            }
        }
        set
        {
            lock (this.syncRoot)
            {
                this.ThrowIfDisposed();

                ArgumentOutOfRangeException.ThrowIfNegative(value);
                this.position = value;
            }
        }
    }

    /// <inheritdoc />
    public override void Flush() => this.ThrowIfDisposed();

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count)
    {
        buffer.Required();

        if ((uint)offset > buffer.Length)
            throw new ArgumentOutOfRangeException(nameof(offset));

        if ((uint)count > buffer.Length - offset)
            throw new ArgumentOutOfRangeException(nameof(count));

        lock (this.syncRoot)
        {
            this.ThrowIfDisposed();

            if (count == 0 || this.position >= this.length)
                return 0;

            var available = (int)Math.Min(count, this.length - this.position);
            Array.Copy(this.buffer, this.position, buffer, offset, available);
            this.position += available;

            return available;
        }
    }

    /// <inheritdoc />
    public override int Read(Span<byte> destination)
    {
        lock (this.syncRoot)
        {
            this.ThrowIfDisposed();

            if (destination.Length == 0 || this.position >= this.length)
                return 0;

            var available = (int)Math.Min(destination.Length, this.length - this.position);
            new ReadOnlySpan<byte>(this.buffer, (int)this.position, available).CopyTo(destination);
            this.position += available;

            return available;
        }
    }

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count)
    {
        buffer.Required();

        if ((uint)offset > buffer.Length)
            throw new ArgumentOutOfRangeException(nameof(offset));

        if ((uint)count > buffer.Length - offset)
            throw new ArgumentOutOfRangeException(nameof(count));

        lock (this.syncRoot)
        {
            this.ThrowIfDisposed();
            this.InternalWrite(buffer.AsSpan(offset, count));
        }
    }

    /// <inheritdoc />
    public override void Write(ReadOnlySpan<byte> source)
    {
        lock (this.syncRoot)
        {
            this.ThrowIfDisposed();
            this.InternalWrite(source);
        }
    }

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin)
    {
        lock (this.syncRoot)
        {
            this.ThrowIfDisposed();

            var target = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => this.position + offset,
                SeekOrigin.End => this.length + offset,
                _ => throw new ArgumentOutOfRangeException(nameof(origin)),
            };

            if (target < 0)
                throw new IOException("Attempted to seek before the beginning of the stream.");

            this.position = target;

            return this.position;
        }
    }

    /// <inheritdoc />
    public override void SetLength(long value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);

        lock (this.syncRoot)
        {
            this.ThrowIfDisposed();
            this.EnsureCapacity(value);
            this.length = value;

            if (this.position > this.length)
                this.position = this.length;
        }
    }

    private void InternalWrite(ReadOnlySpan<byte> source)
    {
        if (source.Length == 0)
            return;

        var endPosition = this.position + source.Length;
        this.EnsureCapacity(endPosition);
        source.CopyTo(new(this.buffer, (int)this.position, source.Length));
        this.position = endPosition;

        if (this.position > this.length)
            this.length = this.position;
    }

    private void EnsureCapacity(long required)
    {
        if (required <= this.buffer.Length)
            return;

        var newCapacity = this.buffer.Length;

        while (newCapacity < required)
        {
            newCapacity = newCapacity switch
            {
                >= int.MaxValue / 2 => (int)required,
                _ => Math.Min(int.MaxValue, newCapacity * 2),
            };
        }

        var newBuffer = ArrayPool<byte>.Shared.Rent(newCapacity);
        Array.Copy(this.buffer, 0, newBuffer, 0, (int)this.length);
        ArrayPool<byte>.Shared.Return(this.buffer, true);
        this.buffer = newBuffer;
    }

    public byte[] ToArray()
    {
        lock (this.syncRoot)
        {
            this.ThrowIfDisposed();
            var result = new byte[this.length];
            Array.Copy(this.buffer, 0, result, 0, (int)this.length);

            return result;
        }
    }

    public bool TryGetBuffer(out ArraySegment<byte> segment)
    {
        lock (this.syncRoot)
        {
            this.ThrowIfDisposed();
            segment = new(this.buffer, 0, (int)this.length);

            return true;
        }
    }

    private void ThrowIfDisposed()
    {
        if (this.disposed)
            throw new ObjectDisposedException(nameof(TestMemoryStream));
    }

    protected override void Dispose(bool disposing)
    {
        if (!disposing)
            return;

        lock (this.syncRoot)
        {
            if (this.disposed)
                return;

            ArrayPool<byte>.Shared.Return(this.buffer, true);
            this.buffer = [];
            this.disposed = true;
        }

        base.Dispose(disposing);
    }
}
