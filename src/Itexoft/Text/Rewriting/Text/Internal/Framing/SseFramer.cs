// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Buffers;
using Itexoft.Text.Rewriting.Primitives;
using Itexoft.Threading;

namespace Itexoft.Text.Rewriting.Text.Internal.Framing;

internal sealed class SseFramer : IDisposable
{
    private readonly string delimiter;
    private readonly ReadOnlyMemory<char> delimiterMemory;
    private readonly bool includeDelimiter;
    private readonly int? maxEventSize;
    private readonly OverflowBehavior overflowBehavior;
    private readonly ArrayPool<char> pool;
    private char[] buffer;
    private int length;

    public SseFramer(string delimiter, bool includeDelimiter, int? maxEventSize, OverflowBehavior overflowBehavior, ArrayPool<char> pool)
    {
        if (delimiter.Length == 0)
            throw new ArgumentException("SseDelimiter must be non-empty.", nameof(delimiter));

        this.delimiter = delimiter;
        this.delimiterMemory = new(delimiter.ToCharArray());
        this.includeDelimiter = includeDelimiter;
        this.maxEventSize = maxEventSize;
        this.overflowBehavior = overflowBehavior;
        this.pool = pool;
        this.buffer = pool.Rent(Math.Max(64, delimiter.Length * 2));
        this.length = 0;
    }

    public void Dispose()
    {
        if (this.buffer.Length == 0)
            return;

        this.pool.Return(this.buffer, false);
        this.buffer = [];
        this.length = 0;
    }

    public void Process(ReadOnlySpan<char> span, Action<ReadOnlySpan<char>, ReadOnlySpan<char>> emit)
    {
        this.Append(span);
        this.EmitEvents(emit);
        this.CheckTailSize();
    }

    public async ValueTask ProcessAsync(
        ReadOnlyMemory<char> span,
        Func<ReadOnlyMemory<char>, ReadOnlyMemory<char>, CancelToken, ValueTask> emit,
        CancelToken cancelToken)
    {
        this.Append(span.Span);
        await this.EmitEventsAsync(emit, cancelToken).ConfigureAwait(false);
        this.CheckTailSize();
    }

    public void FlushPending(Action<ReadOnlySpan<char>, ReadOnlySpan<char>> emit)
    {
        if (this.length == 0)
            return;

        emit(this.buffer.AsSpan(0, this.length), ReadOnlySpan<char>.Empty);
        this.length = 0;
    }

    public ValueTask FlushPendingAsync(Func<ReadOnlyMemory<char>, ReadOnlyMemory<char>, CancelToken, ValueTask> emit, CancelToken cancelToken)
    {
        if (this.length == 0)
            return ValueTask.CompletedTask;

        return emit(this.buffer.AsMemory(0, this.length), ReadOnlyMemory<char>.Empty, cancelToken);
    }

    private void Append(ReadOnlySpan<char> span)
    {
        this.EnsureCapacity(this.length + span.Length);
        span.CopyTo(this.buffer.AsSpan(this.length));
        this.length += span.Length;
    }

    private void EmitEvents(Action<ReadOnlySpan<char>, ReadOnlySpan<char>> emit)
    {
        var delim = this.delimiter.AsSpan();
        var searchStart = 0;

        while (true)
        {
            var idx = this.buffer.AsSpan(searchStart, this.length - searchStart).IndexOf(delim);

            if (idx < 0)
                break;

            var absolute = searchStart + idx;
            var payloadLen = this.includeDelimiter ? absolute + delim.Length : absolute;

            if (this.IsOversized(payloadLen))
            {
                if (this.overflowBehavior == OverflowBehavior.Drop)
                {
                    this.DropEvent(absolute + delim.Length, ref searchStart);

                    continue;
                }

                throw new FormatException("SSE event exceeded maximum allowed size.");
            }

            var delimiterSpan = this.includeDelimiter ? ReadOnlySpan<char>.Empty : delim;
            emit(this.buffer.AsSpan(0, payloadLen), delimiterSpan);

            var remainingStart = absolute + delim.Length;
            var remainingLen = this.length - remainingStart;

            if (remainingLen != 0)
                this.buffer.AsSpan(remainingStart, remainingLen).CopyTo(this.buffer);

            this.length = remainingLen;
            searchStart = 0;
        }
    }

    private async ValueTask EmitEventsAsync(Func<ReadOnlyMemory<char>, ReadOnlyMemory<char>, CancelToken, ValueTask> emit, CancelToken cancelToken)
    {
        var searchStart = 0;

        while (true)
        {
            var idx = this.buffer.AsSpan(searchStart, this.length - searchStart).IndexOf(this.delimiter.AsSpan());

            if (idx < 0)
                break;

            var absolute = searchStart + idx;
            var payloadLen = this.includeDelimiter ? absolute + this.delimiter.Length : absolute;

            if (this.IsOversized(payloadLen))
            {
                if (this.overflowBehavior == OverflowBehavior.Drop)
                {
                    this.DropEvent(absolute + this.delimiter.Length, ref searchStart);

                    continue;
                }

                throw new FormatException("SSE event exceeded maximum allowed size.");
            }

            var delimiterMemory = this.includeDelimiter ? ReadOnlyMemory<char>.Empty : this.delimiterMemory;
            await emit(this.buffer.AsMemory(0, payloadLen), delimiterMemory, cancelToken).ConfigureAwait(false);

            var remainingStart = absolute + this.delimiter.Length;
            var remainingLen = this.length - remainingStart;

            if (remainingLen != 0)
                this.buffer.AsSpan(remainingStart, remainingLen).CopyTo(this.buffer);

            this.length = remainingLen;
            searchStart = 0;
        }
    }

    private void EnsureCapacity(int required)
    {
        if (this.buffer.Length >= required)
            return;

        var newSize = this.buffer.Length * 2;

        if (newSize < required)
            newSize = required;

        var newBuffer = this.pool.Rent(newSize);
        this.buffer.AsSpan(0, this.length).CopyTo(newBuffer);
        this.pool.Return(this.buffer, false);
        this.buffer = newBuffer;
    }

    private bool IsOversized(int eventLength)
    {
        if (!this.maxEventSize.HasValue)
            return false;

        return eventLength > this.maxEventSize.Value;
    }

    private void DropEvent(int dropEnd, ref int searchStart)
    {
        var remainingLen = this.length - dropEnd;

        if (remainingLen != 0)
            this.buffer.AsSpan(dropEnd, remainingLen).CopyTo(this.buffer);

        this.length = remainingLen;
        searchStart = 0;
    }

    private void CheckTailSize()
    {
        if (!this.maxEventSize.HasValue || this.length <= this.maxEventSize.Value)
            return;

        if (this.overflowBehavior == OverflowBehavior.Drop)
        {
            this.length = 0;

            return;
        }

        throw new FormatException("SSE event exceeded maximum allowed size.");
    }
}
