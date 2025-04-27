// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;

namespace Itexoft.Text.Filtering;

/// <summary>
/// A <see cref="TextWriter" /> that applies a compiled <see cref="TextFilterPlan" /> to outgoing text.
/// </summary>
public sealed class FilteringTextWriter : TextWriter
{
    private readonly FilteringTextProcessorOptions options;
    private readonly FilteringTextProcessor? processor;
    private readonly TextWriter underlyingWriter;
    private bool disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="FilteringTextWriter" /> class.
    /// </summary>
    /// <param name="underlyingWriter">Target writer that receives filtered output.</param>
    /// <param name="plan">Compiled filter plan.</param>
    /// <param name="options">Optional runtime settings.</param>
    public FilteringTextWriter(TextWriter underlyingWriter, TextFilterPlan plan, FilteringTextProcessorOptions? options = null)
    {
        this.underlyingWriter = underlyingWriter ?? throw new ArgumentNullException(nameof(underlyingWriter));

        if (plan is null)
            throw new ArgumentNullException(nameof(plan));

        this.options = options ?? new FilteringTextProcessorOptions();
        this.disposed = false;

        if (plan.rules.Length != 0 || this.requiresProcessor(this.options))
            this.processor = new(plan, new TextWriterSink(underlyingWriter), this.options);
    }

    public override Encoding Encoding => this.underlyingWriter.Encoding;

    public override string NewLine
    {
        get => this.underlyingWriter.NewLine;
        [param: AllowNull] set => this.underlyingWriter.NewLine = value ?? string.Empty;
    }

    public override IFormatProvider FormatProvider => this.underlyingWriter.FormatProvider;

    /// <inheritdoc />
    public override void Write(char value)
    {
        this.throwIfDisposed();

        if (this.processor is null)
        {
            this.underlyingWriter.Write(value);

            return;
        }

        this.processor.Write(value);
    }

    /// <inheritdoc />
    public override void Write(ReadOnlySpan<char> buffer)
    {
        this.throwIfDisposed();

        if (buffer.Length == 0)
            return;

        if (this.processor is null)
        {
            this.underlyingWriter.Write(buffer);

            return;
        }

        this.processor.Write(buffer);
    }

    /// <inheritdoc />
    public override void Write(string? value)
    {
        if (value is null)
            return;

        this.Write(value.AsSpan());
    }

    /// <inheritdoc />
    public override void Write(char[] buffer, int index, int count)
    {
        if (buffer is null)
            throw new ArgumentNullException(nameof(buffer));

        this.Write(buffer.AsSpan(index, count));
    }

    /// <inheritdoc />
    public async override Task WriteAsync(char value)
    {
        this.throwIfDisposed();

        if (this.processor is null)
        {
            await this.underlyingWriter.WriteAsync(value).ConfigureAwait(false);

            return;
        }

        await this.processor.WriteAsync(value, CancellationToken.None).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override Task WriteAsync(string? value)
    {
        if (value is null)
            return Task.CompletedTask;

        return this.WriteAsync(value.AsMemory(), CancellationToken.None);
    }

    /// <inheritdoc />
    public override Task WriteAsync(char[] buffer, int index, int count)
    {
        if (buffer is null)
            throw new ArgumentNullException(nameof(buffer));

        return this.WriteAsync(buffer.AsMemory(index, count), CancellationToken.None);
    }

    /// <inheritdoc />
    public async override Task WriteAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken = default)
    {
        this.throwIfDisposed();

        if (buffer.Length == 0)
            return;

        if (this.processor is null)
        {
            await this.underlyingWriter.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);

            return;
        }

        await this.processor.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override void Flush()
    {
        this.throwIfDisposed();

        if (this.processor is not null)
            this.processor.Flush();

        this.underlyingWriter.Flush();
    }

    /// <inheritdoc />
    public async override Task FlushAsync()
    {
        this.throwIfDisposed();

        if (this.processor is not null)
            await this.processor.FlushAsync(CancellationToken.None).ConfigureAwait(false);

        await this.underlyingWriter.FlushAsync().ConfigureAwait(false);
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
            if (this.processor is null)
            {
                this.underlyingWriter.Dispose();
            }
            else
            {
                this.processor.FlushAllSync();
                this.underlyingWriter.Dispose();
                this.processor.Dispose();
            }
        }
        finally
        {
            this.disposed = true;
            base.Dispose(disposing);
        }
    }

    /// <inheritdoc />
    public async override ValueTask DisposeAsync()
    {
        if (this.disposed)
            return;

        try
        {
            if (this.processor is null)
            {
                await this.underlyingWriter.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                await this.processor.FlushAllAsync(CancellationToken.None).ConfigureAwait(false);
                await this.underlyingWriter.DisposeAsync().ConfigureAwait(false);
                await this.processor.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            this.disposed = true;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void throwIfDisposed()
    {
        if (!this.disposed)
            return;

        throw new ObjectDisposedException(nameof(FilteringTextWriter));
    }

    private bool requiresProcessor(FilteringTextProcessorOptions opts)
        => opts.InputNormalizer is not null
           || opts.OutputFilter is not null
           || opts.RuleGate is not null
           || opts.BeforeApply is not null
           || opts.AfterApply is not null
           || opts.OnMetrics is not null;

    private sealed class TextWriterSink : IFilteringTextSink
    {
        private readonly TextWriter writer;

        public TextWriterSink(TextWriter writer) => this.writer = writer;

        public void Write(ReadOnlySpan<char> buffer)
        {
            this.writer.Write(buffer);
        }

        public ValueTask WriteAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken) =>
            new(this.writer.WriteAsync(buffer, cancellationToken));
    }
}
