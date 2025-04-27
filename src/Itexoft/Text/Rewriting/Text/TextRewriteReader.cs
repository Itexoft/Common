// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Buffers;
using System.Runtime.CompilerServices;
using Itexoft.Text.Rewriting.Text.Internal.Engine;
using Itexoft.Text.Rewriting.Text.Internal.Sinks;

namespace Itexoft.Text.Rewriting.Text;

/// <summary>
/// A <see cref="TextReader" /> that applies a compiled <see cref="TextRewritePlan" /> while reading from the underlying source.
/// </summary>
public sealed class TextRewriteReader : TextReader
{
    private readonly ArrayPool<char> arrayPool;
    private readonly TextRewriteOptions options;
    private readonly TextRewriteEngine? processor;
    private readonly BufferSink sink;
    private readonly TextReader underlyingReader;

    private bool disposed;
    private char[]? readBuffer;
    private bool sourceCompleted;

    /// <summary>
    /// Initializes a new instance of the <see cref="TextRewriteReader" /> class.
    /// </summary>
    /// <param name="underlyingReader">Source reader to pull text from.</param>
    /// <param name="plan">Compiled text rewrite plan.</param>
    /// <param name="options">Optional runtime settings.</param>
    public TextRewriteReader(TextReader underlyingReader, TextRewritePlan plan, TextRewriteOptions? options = null)
    {
        this.underlyingReader = underlyingReader ?? throw new ArgumentNullException(nameof(underlyingReader));

        ArgumentNullException.ThrowIfNull(plan);

        this.options = options ?? new TextRewriteOptions();
        this.arrayPool = this.options.ArrayPool ?? ArrayPool<char>.Shared;
        this.sink = new(this.arrayPool);

        if (plan.RuleCount != 0 || this.RequiresProcessor(this.options))
            this.processor = new(plan, this.sink, this.options);
    }

    /// <inheritdoc />
    public override int Peek()
    {
        this.ThrowIfDisposed();

        if (this.sink.Available == 0)
            if (!this.FillOutputFromSource())
                return -1;

        var peek = this.sink.Peek();

        return peek ?? -1;
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
        ArgumentNullException.ThrowIfNull(buffer);

        return this.Read(buffer.AsSpan(index, count));
    }

    /// <inheritdoc />
    public override int Read(Span<char> buffer)
    {
        this.ThrowIfDisposed();

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

            written += this.sink.Drain(buffer[written..]);
        }

        return written;
    }

    /// <inheritdoc />
    public override Task<int> ReadAsync(char[] buffer, int index, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        return this.ReadAsync(buffer.AsMemory(index, count), CancellationToken.None).AsTask();
    }

    /// <inheritdoc />
    public async override ValueTask<int> ReadAsync(Memory<char> buffer, CancellationToken cancellationToken = default)
    {
        this.ThrowIfDisposed();

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

            written += this.sink.Drain(buffer[written..].Span);
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

        this.processor?.Dispose();
        if (this.readBuffer is not null)
            this.arrayPool.Return(this.readBuffer, this.options.ClearPooledBuffersOnDispose);
        this.sink.Clear(this.options.ClearPooledBuffersOnDispose);
        this.underlyingReader.Dispose();

        this.disposed = true;
        base.Dispose(disposing);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (this.disposed)
            return;

        if (this.processor is not null)
            await this.processor.DisposeAsync().ConfigureAwait(false);

        if (this.readBuffer is not null)
            this.arrayPool.Return(this.readBuffer, this.options.ClearPooledBuffersOnDispose);

        this.sink.Clear(this.options.ClearPooledBuffersOnDispose);
        if (this.underlyingReader is IAsyncDisposable asyncDisposable)
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
        else
            this.underlyingReader.Dispose();

        this.disposed = true;
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
    private void ThrowIfDisposed()
    {
        if (!this.disposed)
            return;

        throw new ObjectDisposedException(nameof(TextRewriteReader));
    }

    private bool RequiresProcessor(TextRewriteOptions opts)
        => opts.InputNormalizer is not null
           || opts.OutputFilter is not null
           || opts.OutputFilterAsync is not null
           || opts.RuleGate is not null
           || opts.RuleGateAsync is not null
           || opts.BeforeApply is not null
           || opts.BeforeApplyAsync is not null
           || opts.AfterApply is not null
           || opts.AfterApplyAsync is not null
           || opts.OnMetrics is not null
           || opts.OnMetricsAsync is not null
           || !string.IsNullOrEmpty(opts.SseDelimiter);
}