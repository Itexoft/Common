// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Buffers;
using System.Text;
using Itexoft.Text.Rewriting.Primitives;
using Itexoft.Text.Rewriting.Text.Dsl;
using Itexoft.Text.Rewriting.Text.Internal.Framing;
using Itexoft.Threading;

namespace Itexoft.Text.Rewriting.Text.Internal.Sinks;

internal sealed class OutputSink : IFlushableTextSink, IDisposable
{
    private readonly ITextFraming? framing;
    private readonly StringBuilder? framingBuffer;
    private readonly ITextSink inner;
    private readonly Func<RewriteMetrics> metricsProvider;
    private readonly TextRewriteOptions options;
    private readonly SseFramer? sseFramer;

    public OutputSink(ITextSink inner, TextRewriteOptions options, Func<RewriteMetrics> metricsProvider, ArrayPool<char> pool)
    {
        this.inner = inner;
        this.options = options;
        this.metricsProvider = metricsProvider;
        this.framing = options.TextFraming;

        if (this.framing is not null)
            this.framingBuffer = new();

        if (!string.IsNullOrEmpty(options.SseDelimiter))
            this.sseFramer = new(options.SseDelimiter!, options.SseIncludeDelimiter, options.SseMaxEventSize, options.SseOverflowBehavior, pool);
    }

    public void Dispose() => this.sseFramer?.Dispose();

    public void Write(ReadOnlySpan<char> buffer)
    {
        if (this.framing is not null)
        {
            this.AppendFrameBuffer(buffer);
            this.EmitFramed(this.EmitSync);

            return;
        }

        if (this.sseFramer is not null)
        {
            this.sseFramer.Process(buffer, this.EmitSync);

            return;
        }

        this.EmitSync(buffer, ReadOnlySpan<char>.Empty);
    }

    public async ValueTask WriteAsync(ReadOnlyMemory<char> buffer, CancelToken cancelToken)
    {
        if (this.framing is not null)
        {
            this.AppendFrameBuffer(buffer.Span);
            await this.EmitFramedAsync(this.EmitAsync, cancelToken).ConfigureAwait(false);

            return;
        }

        if (this.sseFramer is not null)
        {
            await this.sseFramer.ProcessAsync(buffer, this.EmitAsync, cancelToken).ConfigureAwait(false);

            return;
        }

        await this.EmitAsync(buffer, ReadOnlyMemory<char>.Empty, cancelToken).ConfigureAwait(false);
    }

    public void FlushPending()
    {
        if (this.framingBuffer is not null && this.framingBuffer.Length != 0)
        {
            var pending = this.framingBuffer.ToString();
            this.framingBuffer.Clear();
            this.EmitSync(pending.AsSpan(), ReadOnlySpan<char>.Empty);
        }
        else
            this.sseFramer?.FlushPending(this.EmitSync);
    }

    public ValueTask FlushPendingAsync(CancelToken cancelToken)
    {
        if (this.framingBuffer is not null && this.framingBuffer.Length != 0)
        {
            var pending = this.framingBuffer.ToString();
            this.framingBuffer.Clear();

            return this.EmitAsync(pending.AsMemory(), ReadOnlyMemory<char>.Empty, cancelToken);
        }

        if (this.sseFramer is not null)
            return this.sseFramer.FlushPendingAsync(this.EmitAsync, cancelToken);

        return ValueTask.CompletedTask;
    }

    private void EmitSync(ReadOnlySpan<char> span, ReadOnlySpan<char> delimiter)
    {
        var filter = this.options.OutputFilter;

        if (filter is null)
        {
            this.inner.Write(span);

            if (!delimiter.IsEmpty)
                this.inner.Write(delimiter);

            return;
        }

        var transformed = filter(span, this.metricsProvider());

        if (transformed is null)
        {
            this.inner.Write(span);

            if (!delimiter.IsEmpty)
                this.inner.Write(delimiter);

            return;
        }

        if (transformed.Length != 0)
            this.inner.Write(transformed.AsSpan());

        if (!delimiter.IsEmpty)
            this.inner.Write(delimiter);
    }

    private async ValueTask EmitAsync(ReadOnlyMemory<char> span, ReadOnlyMemory<char> delimiter, CancelToken cancelToken)
    {
        var filterAsync = this.options.OutputFilterAsync;

        if (filterAsync is not null)
        {
            var transformed = await filterAsync(span, this.metricsProvider()).ConfigureAwait(false);

            if (transformed is null)
            {
                await this.inner.WriteAsync(span, cancelToken).ConfigureAwait(false);

                if (!delimiter.IsEmpty)
                    await this.inner.WriteAsync(delimiter, cancelToken).ConfigureAwait(false);

                return;
            }

            if (transformed.Length != 0)
                await this.inner.WriteAsync(transformed.AsMemory(), cancelToken).ConfigureAwait(false);

            if (!delimiter.IsEmpty)
                await this.inner.WriteAsync(delimiter, cancelToken).ConfigureAwait(false);

            return;
        }

        var filter = this.options.OutputFilter;

        if (filter is null)
        {
            await this.inner.WriteAsync(span, cancelToken).ConfigureAwait(false);

            if (!delimiter.IsEmpty)
                await this.inner.WriteAsync(delimiter, cancelToken).ConfigureAwait(false);

            return;
        }

        var transformedSync = filter(span.Span, this.metricsProvider());

        if (transformedSync is null)
        {
            await this.inner.WriteAsync(span, cancelToken).ConfigureAwait(false);

            if (!delimiter.IsEmpty)
                await this.inner.WriteAsync(delimiter, cancelToken).ConfigureAwait(false);

            return;
        }

        if (transformedSync.Length != 0)
            await this.inner.WriteAsync(transformedSync.AsMemory(), cancelToken).ConfigureAwait(false);

        if (!delimiter.IsEmpty)
            await this.inner.WriteAsync(delimiter, cancelToken).ConfigureAwait(false);
    }

    private void AppendFrameBuffer(ReadOnlySpan<char> span) => this.framingBuffer!.Append(span);

    private void EmitFramed(Action<ReadOnlySpan<char>, ReadOnlySpan<char>> emit)
    {
        var text = this.framingBuffer!.ToString();
        var remaining = text.AsSpan();

        while (this.framing!.TryCutFrame(ref remaining, out var frame))
            emit(frame, ReadOnlySpan<char>.Empty);

        this.framingBuffer.Clear();

        if (!remaining.IsEmpty)
            this.framingBuffer.Append(remaining);
    }

    private async ValueTask EmitFramedAsync(Func<ReadOnlyMemory<char>, ReadOnlyMemory<char>, CancelToken, ValueTask> emit, CancelToken cancelToken)
    {
        var text = this.framingBuffer!.ToString();
        var remaining = text.AsMemory();

        while (this.framing!.TryCutFrame(ref remaining, out var frame))
            await emit(frame, ReadOnlyMemory<char>.Empty, cancelToken).ConfigureAwait(false);

        this.framingBuffer.Clear();

        if (!remaining.IsEmpty)
            this.framingBuffer.Append(remaining.Span);
    }
}
