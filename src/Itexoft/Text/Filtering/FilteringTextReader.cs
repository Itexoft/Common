// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Buffers;
using System.Runtime.CompilerServices;

namespace Itexoft.Text.Filtering;

/// <summary>
/// A <see cref="TextReader" /> that filters characters read from the underlying source.
/// </summary>
public sealed class FilteringTextReader : TextReader
{
    private readonly ArrayPool<char> arrayPool;
    private readonly FilteringTextProcessorOptions options;
    private readonly FilteringTextProcessor? processor;
    private readonly BufferingSink sink;
    private readonly TextReader underlyingReader;

    private bool disposed;
    private char[]? readBuffer;
    private bool sourceCompleted;

    /// <summary>
    /// Initializes a new instance of the <see cref="FilteringTextReader" /> class.
    /// </summary>
    /// <param name="underlyingReader">Source reader to pull text from.</param>
    /// <param name="plan">Compiled filter plan.</param>
    /// <param name="options">Optional runtime settings.</param>
    public FilteringTextReader(TextReader underlyingReader, TextFilterPlan plan, FilteringTextProcessorOptions? options = null)
    {
        this.underlyingReader = underlyingReader ?? throw new ArgumentNullException(nameof(underlyingReader));

        if (plan is null)
            throw new ArgumentNullException(nameof(plan));

        this.options = options ?? new FilteringTextProcessorOptions();
        this.arrayPool = this.options.ArrayPool ?? ArrayPool<char>.Shared;
        this.sink = new(this.arrayPool);

        if (plan.rules.Length != 0 || this.requiresProcessor(this.options))
            this.processor = new(plan, this.sink, this.options);
    }

    /// <inheritdoc />
    public override int Peek()
    {
        this.throwIfDisposed();

        if (this.sink.Available == 0)
            if (!this.FillOutputFromSource())
                return -1;

        var peek = this.sink.Peek();

        return peek.HasValue ? peek.Value : -1;
    }

    /// <inheritdoc />
    public override int Read()
    {
        Span<char> buffer = stackalloc char[1];
        var read = this.Read(buffer);

        return read == 0 ? -1 : buffer[0];
    }

    /// <inheritdoc />
    public override int Read(char[] buffer, int index, int count)
    {
        if (buffer is null)
            throw new ArgumentNullException(nameof(buffer));

        return this.Read(buffer.AsSpan(index, count));
    }

    /// <inheritdoc />
    public override int Read(Span<char> buffer)
    {
        this.throwIfDisposed();

        if (buffer.Length == 0)
            return 0;

        var written = this.sink.Drain(buffer);

        while (written < buffer.Length)
        {
            if (!this.FillOutputFromSource())
            {
                if (this.sourceCompleted)
                    break;

                continue;
            }

            written += this.sink.Drain(buffer.Slice(written));
        }

        return written;
    }

    /// <inheritdoc />
    public override Task<int> ReadAsync(char[] buffer, int index, int count)
    {
        if (buffer is null)
            throw new ArgumentNullException(nameof(buffer));

        return this.ReadAsync(buffer.AsMemory(index, count), CancellationToken.None).AsTask();
    }

    /// <inheritdoc />
    public async override ValueTask<int> ReadAsync(Memory<char> buffer, CancellationToken cancellationToken = default)
    {
        this.throwIfDisposed();

        if (buffer.Length == 0)
            return 0;

        var written = this.sink.Drain(buffer.Span);

        while (written < buffer.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!await this.FillOutputFromSourceAsync(cancellationToken).ConfigureAwait(false))
            {
                if (this.sourceCompleted)
                    break;

                continue;
            }

            written += this.sink.Drain(buffer.Slice(written).Span);
        }

        return written;
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (!disposing)
        {
            base.Dispose(disposing);

            return;
        }

        if (this.disposed)
        {
            base.Dispose(disposing);

            return;
        }

        try
        {
            this.processor?.Dispose();
            if (this.readBuffer is not null)
                this.arrayPool.Return(this.readBuffer, this.options.ClearPooledBuffersOnDispose);
            this.sink.Clear(this.options.ClearPooledBuffersOnDispose);
            this.underlyingReader.Dispose();
        }
        finally
        {
            this.disposed = true;
            base.Dispose(disposing);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (this.disposed)
            return;

        try
        {
            if (this.processor is not null)
                await this.processor.DisposeAsync().ConfigureAwait(false);

            if (this.readBuffer is not null)
                this.arrayPool.Return(this.readBuffer, this.options.ClearPooledBuffersOnDispose);

            this.sink.Clear(this.options.ClearPooledBuffersOnDispose);
            if (this.underlyingReader is IAsyncDisposable asyncDisposable)
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            else
                this.underlyingReader.Dispose();
        }
        finally
        {
            this.disposed = true;
        }
    }

    private bool FillOutputFromSource()
    {
        if (this.sourceCompleted)
            return false;

        if (this.readBuffer is null)
        {
            var size = this.options.RightWriteBlockSize == 0 ? 1024 : this.options.RightWriteBlockSize;
            if (size <= 0)
                size = 1024;

            this.readBuffer = this.arrayPool.Rent(size);
        }

        var read = this.underlyingReader.Read(this.readBuffer, 0, this.readBuffer.Length);
        if (read == 0)
        {
            this.sourceCompleted = true;
            if (this.processor is not null)
                this.processor.FlushAllAndResetSync();

            return this.sink.Available > 0;
        }

        if (this.processor is null)
            this.sink.Write(this.readBuffer.AsSpan(0, read));
        else
            this.processor.Write(this.readBuffer.AsSpan(0, read));

        return this.sink.Available > 0;
    }

    private async Task<bool> FillOutputFromSourceAsync(CancellationToken cancellationToken)
    {
        if (this.sourceCompleted)
            return false;

        if (this.readBuffer is null)
        {
            var size = this.options.RightWriteBlockSize == 0 ? 1024 : this.options.RightWriteBlockSize;
            if (size <= 0)
                size = 1024;

            this.readBuffer = this.arrayPool.Rent(size);
        }

        var read = await this.underlyingReader.ReadAsync(this.readBuffer.AsMemory(0, this.readBuffer.Length), cancellationToken)
            .ConfigureAwait(false);
        if (read == 0)
        {
            this.sourceCompleted = true;
            if (this.processor is not null)
                await this.processor.FlushAllAndResetAsync(cancellationToken).ConfigureAwait(false);

            return this.sink.Available > 0;
        }

        if (this.processor is null)
            this.sink.Write(this.readBuffer.AsSpan(0, read));
        else
            await this.processor.WriteAsync(this.readBuffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);

        return this.sink.Available > 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void throwIfDisposed()
    {
        if (!this.disposed)
            return;

        throw new ObjectDisposedException(nameof(FilteringTextReader));
    }

    private bool requiresProcessor(FilteringTextProcessorOptions opts)
        => opts.InputNormalizer is not null
           || opts.OutputFilter is not null
           || opts.RuleGate is not null
           || opts.BeforeApply is not null
           || opts.AfterApply is not null
           || opts.OnMetrics is not null;
}

internal sealed class BufferingSink : IFilteringTextSink
{
    private readonly ArrayPool<char> pool;
    private char[] buffer;
    private int length;
    private int start;

    public BufferingSink(ArrayPool<char> pool)
    {
        this.pool = pool;
        this.buffer = [];
        this.length = 0;
        this.start = 0;
    }

    public int Available => this.length;

    public void Write(ReadOnlySpan<char> buffer)
    {
        if (buffer.Length == 0)
            return;

        this.ensureCapacity(buffer.Length);
        buffer.CopyTo(this.buffer.AsSpan(this.start + this.length));
        this.length += buffer.Length;
    }

    public ValueTask WriteAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken)
    {
        this.Write(buffer.Span);

        return ValueTask.CompletedTask;
    }

    public int Drain(Span<char> destination)
    {
        var toCopy = Math.Min(destination.Length, this.length);

        if (toCopy == 0)
            return 0;

        this.buffer.AsSpan(this.start, toCopy).CopyTo(destination);

        this.start += toCopy;
        this.length -= toCopy;

        if (this.length == 0)
            this.start = 0;

        return toCopy;
    }

    public char? Peek()
    {
        if (this.length == 0)
            return null;

        return this.buffer[this.start];
    }

    public void Clear(bool clearArray)
    {
        if (this.buffer.Length != 0)
            this.pool.Return(this.buffer, clearArray);

        this.buffer = [];
        this.length = 0;
        this.start = 0;
    }

    private void ensureCapacity(int additional)
    {
        if (this.buffer.Length == 0)
        {
            var size = Math.Max(8, additional);
            this.buffer = this.pool.Rent(size);
            this.start = 0;
            this.length = 0;

            return;
        }

        if (this.start + this.length + additional <= this.buffer.Length)
            return;

        if (this.length + additional <= this.buffer.Length)
        {
            this.buffer.AsSpan(this.start, this.length).CopyTo(this.buffer.AsSpan());
            this.start = 0;

            return;
        }

        var required = this.length + additional;
        var newSize = this.buffer.Length * 2;
        if (newSize < required)
            newSize = required;

        var newBuffer = this.pool.Rent(newSize);
        this.buffer.AsSpan(this.start, this.length).CopyTo(newBuffer.AsSpan());

        this.pool.Return(this.buffer, false);

        this.buffer = newBuffer;
        this.start = 0;
    }
}
