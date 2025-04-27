// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Diagnostics.CodeAnalysis;
using System.Text;
using Itexoft.Core;
using Itexoft.Extensions;
using Itexoft.Text.Rewriting.Text.Internal.Engine;
using Itexoft.Text.Rewriting.Text.Internal.Sinks;
using Itexoft.Threading;

namespace Itexoft.Text.Rewriting.Text;

/// <summary>
/// A <see cref="TextWriter" /> that applies a compiled <see cref="TextRewritePlan" /> to outgoing text.
/// </summary>
public sealed class TextRewriteWriter : TextWriter
{
    private readonly TextRewriteEngine? processor;
    private readonly TextWriter underlyingWriter;
    private Disposed disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="TextRewriteWriter" /> class.
    /// </summary>
    /// <param name="underlyingWriter">Target writer that receives rewritten output.</param>
    /// <param name="plan">Compiled text rewrite plan.</param>
    /// <param name="options">Optional runtime options.</param>
    public TextRewriteWriter(TextWriter underlyingWriter, TextRewritePlan plan, TextRewriteOptions? options = null)
    {
        this.underlyingWriter = underlyingWriter ?? throw new ArgumentNullException(nameof(underlyingWriter));

        plan.Required();

        var options1 = options ?? new TextRewriteOptions();

        if (plan.RuleCount != 0 || this.RequiresProcessor(options1))
            this.processor = new(plan, new TextWriterSink(underlyingWriter), options1);
    }

    public override Encoding Encoding => this.underlyingWriter.Encoding;

    public override string NewLine
    {
        get => this.underlyingWriter.NewLine;
#pragma warning disable CS8765 // Nullability of type of parameter doesn't match overridden member (possibly because of nullability attributes).
        [param: AllowNull] set => this.underlyingWriter.NewLine = value ! ?? string.Empty;
#pragma warning restore CS8765 // Nullability of type of parameter doesn't match overridden member (possibly because of nullability attributes).
    }

    public override IFormatProvider FormatProvider => this.underlyingWriter.FormatProvider;

    /// <inheritdoc />
    public override void Write(char value)
    {
        this.disposed.ThrowIf();

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
        this.disposed.ThrowIf();

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
        buffer.Required();

        this.Write(buffer.AsSpan(index, count));
    }

    /// <inheritdoc />
    public async override Task WriteAsync(char value)
    {
        this.disposed.ThrowIf();

        if (this.processor is null)
        {
            await this.underlyingWriter.WriteAsync(value).ConfigureAwait(false);

            return;
        }

        await this.processor.WriteAsync(value, CancelToken.None).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override Task WriteAsync(string? value)
    {
        if (value is null)
            return Task.CompletedTask;

        return this.WriteAsync(value.AsMemory(), CancelToken.None);
    }

    /// <inheritdoc />
    public override Task WriteAsync(char[] buffer, int index, int count)
    {
        buffer.Required();

        return this.WriteAsync(buffer.AsMemory(index, count), CancelToken.None);
    }

    /// <inheritdoc />
    public async Task WriteAsync(ReadOnlyMemory<char> buffer, CancelToken cancelToken = default)
    {
        this.disposed.ThrowIf();

        if (buffer.Length == 0)
            return;

        if (this.processor is null)
        {
            using (cancelToken.Bridge(out var token))
                await this.underlyingWriter.WriteAsync(buffer, token).ConfigureAwait(false);

            return;
        }

        await this.processor.WriteAsync(buffer, cancelToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override void Flush()
    {
        this.disposed.ThrowIf();

        if (this.processor is not null)
            this.processor.Flush();

        this.underlyingWriter.Flush();
    }

    /// <inheritdoc />
    public async override Task FlushAsync()
    {
        this.disposed.ThrowIf();

        if (this.processor is not null)
            await this.processor.FlushAsync(CancelToken.None).ConfigureAwait(false);

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

        if (this.disposed.Enter())
            return;

        base.Dispose(disposing);

        if (this.processor is null)
            this.underlyingWriter.Dispose();
        else
        {
            this.processor.FlushAllSync();
            this.underlyingWriter.Dispose();
            this.processor.Dispose();
        }
    }

    /// <inheritdoc />
    public async override ValueTask DisposeAsync()
    {
        if (this.disposed.Enter())
            return;

        if (this.processor is null)
            await this.underlyingWriter.DisposeAsync().ConfigureAwait(false);
        else
        {
            await this.processor.FlushAllAsync(CancelToken.None).ConfigureAwait(false);
            await this.underlyingWriter.DisposeAsync().ConfigureAwait(false);
            await this.processor.DisposeAsync().ConfigureAwait(false);
        }
    }

    private bool RequiresProcessor(TextRewriteOptions opts) => opts.InputNormalizer is not null
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

    private sealed class TextWriterSink(TextWriter writer) : ITextSink
    {
        public void Write(ReadOnlySpan<char> buffer) => writer.Write(buffer);

        public async ValueTask WriteAsync(ReadOnlyMemory<char> buffer, CancelToken cancelToken)
        {
            using (cancelToken.Bridge(out var token))
                await writer.WriteAsync(buffer, token);
        }
    }
}
