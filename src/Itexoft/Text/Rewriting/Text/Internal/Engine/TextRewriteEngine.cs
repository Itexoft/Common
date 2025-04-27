// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Itexoft.Text.Rewriting.Primitives;
using Itexoft.Text.Rewriting.Text.Internal.Gates;
using Itexoft.Text.Rewriting.Text.Internal.Matching;
using Itexoft.Text.Rewriting.Text.Internal.Sinks;
using Itexoft.Threading;

namespace Itexoft.Text.Rewriting.Text.Internal.Engine;

/// <summary>
/// Shared filtering core that applies a compiled plan while emitting to a sink.
/// </summary>
internal sealed class TextRewriteEngine : IAsyncDisposable, IDisposable
{
    private readonly ArrayPool<char> arrayPool;
    private readonly bool collectRuleMetrics;
    private readonly IFlushableTextSink? flushableSink;
    private readonly bool hasAsyncCallbacks;
    private readonly TextRewriteOptions options;
    private readonly TextRewritePlan plan;
    private readonly long[]? ruleElapsedTicks;
    private readonly bool[] ruleGateSnapshot;
    private readonly long[]? ruleHits;
    private readonly ITextSink sink;

    private bool disposed;
    private long flushes;
    private bool hasPendingMatch;
    private long matchesApplied;
    private int ordinalIgnoreCaseState;
    private int ordinalState;

    private char[] pendingBuffer;
    private int pendingLength;
    private MatchCandidate pendingMatch;
    private int pendingMatchOffset;
    private int pendingStart;
    private long processedChars;
    private long removals;
    private long replacements;

    public TextRewriteEngine(TextRewritePlan plan, ITextSink sink, TextRewriteOptions? options = null)
    {
        this.plan = plan ?? throw new ArgumentNullException(nameof(plan));
        this.options = options ?? new TextRewriteOptions();
        this.arrayPool = this.options.ArrayPool ?? ArrayPool<char>.Shared;
        this.collectRuleMetrics = this.options.OnRuleMetrics is not null || this.options.OnRuleMetricsAsync is not null;

        ArgumentNullException.ThrowIfNull(sink);

        this.sink = new OutputSink(
            sink,
            this.options,
            this.SnapshotMetrics,
            this.arrayPool);
        this.flushableSink = this.sink as IFlushableTextSink;

        this.pendingBuffer = [];
        this.pendingStart = 0;
        this.pendingLength = 0;

        this.ordinalState = 0;
        this.ordinalIgnoreCaseState = 0;

        this.disposed = false;
        this.processedChars = 0;
        this.matchesApplied = 0;
        this.replacements = 0;
        this.removals = 0;
        this.flushes = 0;
        this.hasPendingMatch = false;
        this.pendingMatch = MatchCandidate.None;
        this.pendingMatchOffset = 0;
        if (this.collectRuleMetrics)
        {
            this.ruleHits = new long[plan.RuleCount];
            this.ruleElapsedTicks = new long[plan.RuleCount];
        }

        this.hasAsyncCallbacks =
            plan.HasAsyncRules
            || this.options.BeforeApplyAsync is not null
            || this.options.AfterApplyAsync is not null
            || this.options.OutputFilterAsync is not null
            || this.options.RuleGateAsync is not null
            || this.options.OnMetricsAsync is not null;

        this.ruleGateSnapshot = plan.RuleCount == 0 ? [] : new bool[plan.RuleCount];

        if (this.options.InitialBufferSize < 0)
            throw new ArgumentOutOfRangeException(nameof(options), "InitialBufferSize must be >= 0.");
        if (this.options.MaxBufferedChars <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "MaxBufferedChars must be > 0.");
        if (this.options.RightWriteBlockSize < 0)
            throw new ArgumentOutOfRangeException(nameof(options), "RightWriteBlockSize must be >= 0.");
        if (this.options.SseMaxEventSize.HasValue && this.options.SseMaxEventSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "SseMaxEventSize must be > 0.");
    }

    public ValueTask DisposeAsync()
    {
        this.Dispose();

        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        if (this.disposed)
            return;

        if (this.sink is IDisposable disposableSink)
            disposableSink.Dispose();

        this.ReturnPendingBuffer();
        this.disposed = true;
        GC.SuppressFinalize(this);
    }

    public void Write(char value)
    {
        this.ThrowIfDisposed();

        var flushCount = this.ProcessChar(value);
        if (flushCount != 0)
            this.FlushSafePrefixSync(flushCount);

        this.FlushSafePrefixSyncForce();
    }

    public void Write(ReadOnlySpan<char> buffer)
    {
        this.ThrowIfDisposed();

        if (buffer.Length == 0)
            return;

        for (var i = 0; i < buffer.Length; i++)
        {
            var flushCount = this.ProcessChar(buffer[i]);
            if (flushCount != 0)
                this.FlushSafePrefixSync(flushCount);
        }

        this.FlushSafePrefixSyncForce();
    }

    public async Task WriteAsync(char value, CancellationToken cancellationToken)
    {
        this.ThrowIfDisposed();

        if (!this.hasAsyncCallbacks)
        {
            var flushCount = await this.ProcessCharAsync(value, cancellationToken);
            if (flushCount != 0)
                await this.FlushSafePrefixAsync(flushCount, cancellationToken).ConfigureAwait(false);

            await this.FlushSafePrefixAsyncForce(cancellationToken).ConfigureAwait(false);

            return;
        }

        var asyncFlushCount = await this.ProcessCharAsync(value, cancellationToken).ConfigureAwait(false);
        if (asyncFlushCount != 0)
            await this.FlushSafePrefixAsync(asyncFlushCount, cancellationToken).ConfigureAwait(false);

        await this.FlushSafePrefixAsyncForce(cancellationToken).ConfigureAwait(false);
    }

    public async Task WriteAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken)
    {
        this.ThrowIfDisposed();

        if (buffer.Length == 0)
            return;

        if (!this.hasAsyncCallbacks)
        {
            for (var i = 0; i < buffer.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var flushCount = await this.ProcessCharAsync(buffer.Span[i], cancellationToken);
                if (flushCount != 0)
                    await this.FlushSafePrefixAsync(flushCount, cancellationToken).ConfigureAwait(false);
            }

            await this.FlushSafePrefixAsyncForce(cancellationToken).ConfigureAwait(false);

            return;
        }

        for (var i = 0; i < buffer.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var flushCount = await this.ProcessCharAsync(buffer.Span[i], cancellationToken).ConfigureAwait(false);
            if (flushCount != 0)
                await this.FlushSafePrefixAsync(flushCount, cancellationToken).ConfigureAwait(false);
        }

        await this.FlushSafePrefixAsyncForce(cancellationToken).ConfigureAwait(false);
    }

    public void Flush()
    {
        this.ThrowIfDisposed();

        if (this.options.FlushBehavior == FlushBehavior.Commit)
            this.FlushAllAndResetSync();
        else
            this.FlushSafePrefixSyncForce();
    }

    public async Task FlushAsync(CancellationToken cancellationToken)
    {
        this.ThrowIfDisposed();

        if (this.options.FlushBehavior == FlushBehavior.Commit)
            await this.FlushAllAndResetAsync(cancellationToken).ConfigureAwait(false);
        else
            await this.FlushSafePrefixAsyncForce(cancellationToken).ConfigureAwait(false);
    }

    public void FlushAllSync()
    {
        this.ThrowIfDisposed();

        this.FlushAllSyncInternal();
    }

    public async Task FlushAllAsync(CancellationToken cancellationToken)
    {
        this.ThrowIfDisposed();

        await this.FlushAllAsyncInternal(cancellationToken).ConfigureAwait(false);
    }

    public void FlushAllAndResetSync()
    {
        this.ThrowIfDisposed();

        this.FlushAllAndResetSyncInternal();
    }

    public async Task FlushAllAndResetAsync(CancellationToken cancellationToken)
    {
        this.ThrowIfDisposed();

        await this.FlushAllAndResetAsyncInternal(cancellationToken).ConfigureAwait(false);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ThrowIfDisposed()
    {
        if (!this.disposed)
            return;

        throw new ObjectDisposedException(nameof(TextRewriteEngine));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int ProcessChar(char c)
    {
        var normalized = this.options.InputNormalizer?.Invoke(c) ?? c;
        this.processedChars++;
        if (normalized == '\0')
        {
            this.PublishMetrics();

            return 0;
        }

        this.AppendChar(normalized);

        var candidate = this.FindBestCandidate(normalized);
        if (candidate.ruleId >= 0)
        {
            var offset = this.pendingLength - candidate.matchLength;
            this.SetPendingMatch(candidate, offset);
        }
        else
        {
            this.ApplyPendingMatchIfReady(false);
        }

        this.ApplyPendingMatchIfReady(false);

        var safeCount = this.pendingLength - this.plan.maxPending;
        if (this.hasPendingMatch && safeCount > this.pendingMatchOffset)
            safeCount = this.pendingMatchOffset;

        if (safeCount <= 0)
            return 0;

        if (this.options.RightWriteBlockSize == 0 || safeCount >= this.options.RightWriteBlockSize)
        {
            this.PublishMetrics();

            return safeCount;
        }

        if (this.pendingLength >= this.options.MaxBufferedChars)
        {
            this.PublishMetrics();

            return safeCount;
        }

        this.PublishMetrics();

        return 0;
    }

    private async ValueTask<int> ProcessCharAsync(char c, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var normalized = this.options.InputNormalizer?.Invoke(c) ?? c;
        this.processedChars++;
        if (normalized == '\0')
        {
            await this.PublishMetricsAsync().ConfigureAwait(false);

            return 0;
        }

        this.AppendChar(normalized);

        var candidate = await this.FindBestCandidateAsync(normalized, cancellationToken).ConfigureAwait(false);
        if (candidate.ruleId >= 0)
        {
            var offset = this.pendingLength - candidate.matchLength;
            this.SetPendingMatch(candidate, offset);
        }
        else
        {
            await this.ApplyPendingMatchIfReadyAsync(false, cancellationToken).ConfigureAwait(false);
        }

        await this.ApplyPendingMatchIfReadyAsync(false, cancellationToken).ConfigureAwait(false);

        var safeCount = this.pendingLength - this.plan.maxPending;
        if (this.hasPendingMatch && safeCount > this.pendingMatchOffset)
            safeCount = this.pendingMatchOffset;

        if (safeCount <= 0)
        {
            await this.PublishMetricsAsync().ConfigureAwait(false);

            return 0;
        }

        if (this.options.RightWriteBlockSize == 0 || safeCount >= this.options.RightWriteBlockSize)
        {
            await this.PublishMetricsAsync().ConfigureAwait(false);

            return safeCount;
        }

        if (this.pendingLength >= this.options.MaxBufferedChars)
        {
            await this.PublishMetricsAsync().ConfigureAwait(false);

            return safeCount;
        }

        await this.PublishMetricsAsync().ConfigureAwait(false);

        return 0;
    }

    private MatchCandidate FindBestCandidate(char currentChar)
    {
        var best = MatchCandidate.None;
        var gate = this.CreateGateLazy();

        if (this.plan.ordinalAutomaton is not null)
        {
            this.ordinalState = this.plan.ordinalAutomaton.Step(this.ordinalState, currentChar);
            var ruleId = this.plan.ordinalAutomaton.GetBestRuleId(this.ordinalState);
            if (ruleId >= 0 && gate.GetOrCreate().Allows(ruleId))
                best = this.ChooseBetter(best, ruleId, this.plan.Rules[ruleId].FixedLength);
        }

        if (this.plan.ordinalIgnoreCaseAutomaton is not null)
        {
            var folded = FoldCharOrdinalIgnoreCase(currentChar);
            this.ordinalIgnoreCaseState = this.plan.ordinalIgnoreCaseAutomaton.Step(this.ordinalIgnoreCaseState, folded);
            var ruleId = this.plan.ordinalIgnoreCaseAutomaton.GetBestRuleId(this.ordinalIgnoreCaseState);
            if (ruleId >= 0 && gate.GetOrCreate().Allows(ruleId))
                best = this.ChooseBetter(best, ruleId, this.plan.Rules[ruleId].FixedLength);
        }

        if (this.plan.regexRules.Length != 0 || this.plan.customRules.Length != 0)
        {
            var pendingTotal = this.pendingLength;
            if (pendingTotal != 0)
            {
                for (var i = 0; i < this.plan.regexRules.Length; i++)
                {
                    var rr = this.plan.regexRules[i];
                    var tailLen = pendingTotal < rr.MaxMatchLength ? pendingTotal : rr.MaxMatchLength;

                    if (tailLen == 0)
                        continue;

                    var tailSpan = this.pendingBuffer.AsSpan(this.pendingStart + pendingTotal - tailLen, tailLen);

                    var bestLen = 0;
                    foreach (var m in rr.regex.EnumerateMatches(tailSpan))
                        if (m.Length != 0 && m.Index + m.Length == tailLen && m.Length > bestLen)
                            bestLen = m.Length;

                    if (bestLen != 0 && gate.GetOrCreate().Allows(rr.ruleId))
                        best = this.ChooseBetter(best, rr.ruleId, bestLen);
                }

                for (var i = 0; i < this.plan.customRules.Length; i++)
                {
                    var cr = this.plan.customRules[i];
                    var tailLen = pendingTotal < cr.MaxMatchLength ? pendingTotal : cr.MaxMatchLength;

                    if (tailLen == 0)
                        continue;

                    var tailSpan = this.pendingBuffer.AsSpan(this.pendingStart + pendingTotal - tailLen, tailLen);
                    var matchLen = cr.matcher(tailSpan);

                    if (matchLen > 0 && matchLen <= tailLen && gate.GetOrCreate().Allows(cr.ruleId))
                        best = this.ChooseBetter(best, cr.ruleId, matchLen);
                }
            }
        }

        return best;
    }

    private async ValueTask<MatchCandidate> FindBestCandidateAsync(char currentChar, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var best = MatchCandidate.None;
        var gate = this.CreateGateLazyAsync();

        if (this.plan.ordinalAutomaton is not null)
        {
            this.ordinalState = this.plan.ordinalAutomaton.Step(this.ordinalState, currentChar);
            var ruleId = this.plan.ordinalAutomaton.GetBestRuleId(this.ordinalState);
            if (ruleId >= 0 && (await gate.GetOrCreateAsync(cancellationToken).ConfigureAwait(false)).Allows(ruleId))
                best = this.ChooseBetter(best, ruleId, this.plan.Rules[ruleId].FixedLength);
        }

        if (this.plan.ordinalIgnoreCaseAutomaton is not null)
        {
            var folded = FoldCharOrdinalIgnoreCase(currentChar);
            this.ordinalIgnoreCaseState = this.plan.ordinalIgnoreCaseAutomaton.Step(this.ordinalIgnoreCaseState, folded);
            var ruleId = this.plan.ordinalIgnoreCaseAutomaton.GetBestRuleId(this.ordinalIgnoreCaseState);
            if (ruleId >= 0 && (await gate.GetOrCreateAsync(cancellationToken).ConfigureAwait(false)).Allows(ruleId))
                best = this.ChooseBetter(best, ruleId, this.plan.Rules[ruleId].FixedLength);
        }

        if (this.plan.regexRules.Length != 0 || this.plan.customRules.Length != 0)
        {
            var pendingTotal = this.pendingLength;
            if (pendingTotal != 0)
            {
                for (var i = 0; i < this.plan.regexRules.Length; i++)
                {
                    var rr = this.plan.regexRules[i];
                    var tailLen = pendingTotal < rr.MaxMatchLength ? pendingTotal : rr.MaxMatchLength;

                    if (tailLen == 0)
                        continue;

                    var tailSpan = this.pendingBuffer.AsSpan(this.pendingStart + pendingTotal - tailLen, tailLen);

                    var bestLen = 0;
                    foreach (var m in rr.regex.EnumerateMatches(tailSpan))
                        if (m.Length != 0 && m.Index + m.Length == tailLen && m.Length > bestLen)
                            bestLen = m.Length;

                    if (bestLen != 0
                        && (await gate.GetOrCreateAsync(cancellationToken).ConfigureAwait(false)).Allows(rr.ruleId))
                        best = this.ChooseBetter(best, rr.ruleId, bestLen);
                }

                for (var i = 0; i < this.plan.customRules.Length; i++)
                {
                    var cr = this.plan.customRules[i];
                    var tailLen = pendingTotal < cr.MaxMatchLength ? pendingTotal : cr.MaxMatchLength;

                    if (tailLen == 0)
                        continue;

                    var tailSpan = this.pendingBuffer.AsSpan(this.pendingStart + pendingTotal - tailLen, tailLen);
                    var matchLen = cr.matcher(tailSpan);

                    if (matchLen > 0
                        && matchLen <= tailLen
                        && (await gate.GetOrCreateAsync(cancellationToken).ConfigureAwait(false)).Allows(cr.ruleId))
                        best = this.ChooseBetter(best, cr.ruleId, matchLen);
                }
            }
        }

        return best;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private MatchCandidate ChooseBetter(MatchCandidate current, int ruleId, int matchLength)
    {
        var rule = this.plan.Rules[ruleId];

        if (!current.HasValue)
            return new(ruleId, matchLength, rule.Priority, rule.Order);

        if (this.plan.selection == MatchSelection.LongestThenPriority)
        {
            if (matchLength > current.matchLength)
                return new(ruleId, matchLength, rule.Priority, rule.Order);

            if (matchLength < current.matchLength)
                return current;
        }
        else
        {
            if (rule.Priority < current.priority)
                return new(ruleId, matchLength, rule.Priority, rule.Order);

            if (rule.Priority > current.priority)
                return current;
        }

        if (rule.Priority < current.priority)
            return new(ruleId, matchLength, rule.Priority, rule.Order);

        if (rule.Priority > current.priority)
            return current;

        if (rule.Order < current.order)
            return new(ruleId, matchLength, rule.Priority, rule.Order);

        if (rule.Order > current.order)
            return current;

        if (matchLength > current.matchLength)
            return new(ruleId, matchLength, rule.Priority, rule.Order);

        return current;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private RuleGateSnapshot CreateGateSnapshot()
    {
        if (this.options.RuleGate is null)
            return RuleGateSnapshot.AllEnabled;

        var metrics = this.SnapshotMetrics();
        var buffer = this.ruleGateSnapshot;
        for (var i = 0; i < buffer.Length; i++)
            buffer[i] = this.options.RuleGate(i, metrics);

        return new(buffer, true);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private async ValueTask<RuleGateSnapshot> CreateGateSnapshotAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (this.options.RuleGateAsync is not null)
        {
            var metrics = this.SnapshotMetrics();
            var buffer = this.ruleGateSnapshot;

            for (var i = 0; i < buffer.Length; i++)
                buffer[i] = await this.options.RuleGateAsync(i, metrics).ConfigureAwait(false);

            return new(buffer, true);
        }

        if (this.options.RuleGate is null)
            return RuleGateSnapshot.AllEnabled;

        return this.CreateGateSnapshot();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private SyncAsyncLazy<RuleGateSnapshot> CreateGateLazy() => SyncAsyncLazy<RuleGateSnapshot>.CreateOrDefault(
        this.options.RuleGate is null ? null : this.CreateGateSnapshot,
        null,
        RuleGateSnapshot.AllEnabled);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private SyncAsyncLazy<RuleGateSnapshot> CreateGateLazyAsync() => SyncAsyncLazy<RuleGateSnapshot>.CreateOrDefault(
        this.options.RuleGate is null ? null : this.CreateGateSnapshot,
        this.options.RuleGateAsync is null ? null : this.CreateGateSnapshotAsync,
        RuleGateSnapshot.AllEnabled);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SetPendingMatch(MatchCandidate candidate, int offset)
    {
        if (!this.hasPendingMatch)
        {
            this.pendingMatch = candidate;
            this.pendingMatchOffset = offset;
            this.hasPendingMatch = true;

            return;
        }

        var chosen = this.ChooseBetter(this.pendingMatch, candidate.ruleId, candidate.matchLength);
        if (chosen.ruleId == this.pendingMatch.ruleId
            && chosen.matchLength == this.pendingMatch.matchLength
            && chosen.priority == this.pendingMatch.priority
            && chosen.order == this.pendingMatch.order)
        {
            if (offset >= this.pendingMatchOffset)
                return;
        }
        else if (chosen.ruleId != candidate.ruleId)
        {
            return;
        }

        this.pendingMatch = candidate;
        this.pendingMatchOffset = offset;
        this.hasPendingMatch = true;
    }

    private void ApplyPendingMatchIfReady(bool force)
    {
        if (!this.hasPendingMatch)
            return;

        var trailing = this.pendingLength - (this.pendingMatchOffset + this.pendingMatch.matchLength);
        var requiredTrailing = this.plan.maxMatchLength - this.pendingMatch.matchLength;
        if (requiredTrailing < 0)
            requiredTrailing = 0;

        if (!force && trailing < requiredTrailing)
            return;

        this.ApplyCandidateAtOffset(this.pendingMatch.ruleId, this.pendingMatchOffset, this.pendingMatch.matchLength);
        this.hasPendingMatch = false;
        this.pendingMatch = MatchCandidate.None;
        this.pendingMatchOffset = 0;
    }

    private async ValueTask ApplyPendingMatchIfReadyAsync(bool force, CancellationToken cancellationToken)
    {
        if (!this.hasPendingMatch)
            return;

        var trailing = this.pendingLength - (this.pendingMatchOffset + this.pendingMatch.matchLength);
        var requiredTrailing = this.plan.maxMatchLength - this.pendingMatch.matchLength;
        if (requiredTrailing < 0)
            requiredTrailing = 0;

        if (!force && trailing < requiredTrailing)
            return;

        await this.ApplyCandidateAtOffsetAsync(
                this.pendingMatch.ruleId,
                this.pendingMatchOffset,
                this.pendingMatch.matchLength,
                cancellationToken)
            .ConfigureAwait(false);
        this.hasPendingMatch = false;
        this.pendingMatch = MatchCandidate.None;
        this.pendingMatchOffset = 0;
    }

    private void RescanPendingBufferForMatch(int minOffset = 0)
    {
        this.hasPendingMatch = false;
        this.pendingMatch = MatchCandidate.None;
        this.pendingMatchOffset = 0;

        if (this.pendingLength == 0)
            return;

        var ordinalState = 0;
        var ordinalIgnoreCaseState = 0;
        var gate = this.CreateGateLazy();

        for (var i = 0; i < this.pendingLength; i++)
        {
            var ch = this.pendingBuffer[this.pendingStart + i];
            var best = MatchCandidate.None;

            if (this.plan.ordinalAutomaton is not null)
            {
                ordinalState = this.plan.ordinalAutomaton.Step(ordinalState, ch);
                var ruleId = this.plan.ordinalAutomaton.GetBestRuleId(ordinalState);
                if (ruleId >= 0 && gate.GetOrCreate().Allows(ruleId))
                    best = this.ChooseBetter(best, ruleId, this.plan.Rules[ruleId].FixedLength);
            }

            if (this.plan.ordinalIgnoreCaseAutomaton is not null)
            {
                var folded = FoldCharOrdinalIgnoreCase(ch);
                ordinalIgnoreCaseState = this.plan.ordinalIgnoreCaseAutomaton.Step(ordinalIgnoreCaseState, folded);
                var ruleId = this.plan.ordinalIgnoreCaseAutomaton.GetBestRuleId(ordinalIgnoreCaseState);
                if (ruleId >= 0 && gate.GetOrCreate().Allows(ruleId))
                    best = this.ChooseBetter(best, ruleId, this.plan.Rules[ruleId].FixedLength);
            }

            var processed = i + 1;
            if (this.plan.regexRules.Length != 0 || this.plan.customRules.Length != 0)
            {
                for (var r = 0; r < this.plan.regexRules.Length; r++)
                {
                    var rr = this.plan.regexRules[r];
                    var tailLen = processed < rr.MaxMatchLength ? processed : rr.MaxMatchLength;

                    if (tailLen == 0)
                        continue;

                    var tailSpan = this.pendingBuffer.AsSpan(this.pendingStart + processed - tailLen, tailLen);

                    var bestLen = 0;
                    foreach (var m in rr.regex.EnumerateMatches(tailSpan))
                        if (m.Length != 0 && m.Index + m.Length == tailLen && m.Length > bestLen)
                            bestLen = m.Length;

                    if (bestLen != 0 && gate.GetOrCreate().Allows(rr.ruleId))
                        best = this.ChooseBetter(best, rr.ruleId, bestLen);
                }

                for (var r = 0; r < this.plan.customRules.Length; r++)
                {
                    var cr = this.plan.customRules[r];
                    var tailLen = processed < cr.MaxMatchLength ? processed : cr.MaxMatchLength;

                    if (tailLen == 0)
                        continue;

                    var tailSpan = this.pendingBuffer.AsSpan(this.pendingStart + processed - tailLen, tailLen);
                    var matchLen = cr.matcher(tailSpan);

                    if (matchLen > 0 && matchLen <= tailLen && gate.GetOrCreate().Allows(cr.ruleId))
                        best = this.ChooseBetter(best, cr.ruleId, matchLen);
                }
            }

            if (best.ruleId >= 0)
            {
                var offset = processed - best.matchLength;
                if (offset >= minOffset)
                    this.SetPendingMatch(best, offset);
            }
        }
    }

    private async Task RescanPendingBufferForMatchAsync(int minOffset, CancellationToken cancellationToken)
    {
        this.hasPendingMatch = false;
        this.pendingMatch = MatchCandidate.None;
        this.pendingMatchOffset = 0;

        if (this.pendingLength == 0)
            return;

        var ordinalState = 0;
        var ordinalIgnoreCaseState = 0;
        var gate = this.CreateGateLazyAsync();

        for (var i = 0; i < this.pendingLength; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var ch = this.pendingBuffer[this.pendingStart + i];
            var best = MatchCandidate.None;

            if (this.plan.ordinalAutomaton is not null)
            {
                ordinalState = this.plan.ordinalAutomaton.Step(ordinalState, ch);
                var ruleId = this.plan.ordinalAutomaton.GetBestRuleId(ordinalState);
                if (ruleId >= 0 && (await gate.GetOrCreateAsync(cancellationToken).ConfigureAwait(false)).Allows(ruleId))
                    best = this.ChooseBetter(best, ruleId, this.plan.Rules[ruleId].FixedLength);
            }

            if (this.plan.ordinalIgnoreCaseAutomaton is not null)
            {
                var folded = FoldCharOrdinalIgnoreCase(ch);
                ordinalIgnoreCaseState = this.plan.ordinalIgnoreCaseAutomaton.Step(ordinalIgnoreCaseState, folded);
                var ruleId = this.plan.ordinalIgnoreCaseAutomaton.GetBestRuleId(ordinalIgnoreCaseState);
                if (ruleId >= 0 && (await gate.GetOrCreateAsync(cancellationToken).ConfigureAwait(false)).Allows(ruleId))
                    best = this.ChooseBetter(best, ruleId, this.plan.Rules[ruleId].FixedLength);
            }

            var processed = i + 1;
            if (this.plan.regexRules.Length != 0 || this.plan.customRules.Length != 0)
            {
                foreach (var rr in this.plan.regexRules)
                {
                    var tailLen = processed < rr.MaxMatchLength ? processed : rr.MaxMatchLength;

                    if (tailLen == 0)
                        continue;

                    var tailSpan = this.pendingBuffer.AsSpan(this.pendingStart + processed - tailLen, tailLen);

                    var bestLen = 0;
                    foreach (var m in rr.regex.EnumerateMatches(tailSpan))
                        if (m.Length != 0 && m.Index + m.Length == tailLen && m.Length > bestLen)
                            bestLen = m.Length;

                    if (bestLen != 0 && (await gate.GetOrCreateAsync(cancellationToken).ConfigureAwait(false)).Allows(rr.ruleId))
                        best = this.ChooseBetter(best, rr.ruleId, bestLen);
                }

                foreach (var cr in this.plan.customRules)
                {
                    var tailLen = processed < cr.MaxMatchLength ? processed : cr.MaxMatchLength;

                    if (tailLen == 0)
                        continue;

                    var tailSpan = this.pendingBuffer.AsSpan(this.pendingStart + processed - tailLen, tailLen);
                    var matchLen = cr.matcher(tailSpan);

                    if (matchLen > 0
                        && matchLen <= tailLen
                        && (await gate.GetOrCreateAsync(cancellationToken).ConfigureAwait(false)).Allows(cr.ruleId))
                        best = this.ChooseBetter(best, cr.ruleId, matchLen);
                }
            }

            if (best.ruleId >= 0)
            {
                var offset = processed - best.matchLength;
                if (offset >= minOffset)
                    this.SetPendingMatch(best, offset);
            }
        }
    }

    private void ApplyAllPendingMatchesSync()
    {
        var minOffset = 0;
        this.RescanPendingBufferForMatch(minOffset);

        while (this.hasPendingMatch)
        {
            var appliedOffset = this.pendingMatchOffset;
            var beforeLength = this.pendingLength;
            var appliedLength = this.pendingMatch.matchLength;
            this.ApplyPendingMatchIfReady(true);
            var afterLength = this.pendingLength;
            var replacementLength = appliedLength + (afterLength - beforeLength);
            minOffset = appliedOffset + Math.Max(1, replacementLength);
            this.RescanPendingBufferForMatch(minOffset);
        }
    }

    private async Task ApplyAllPendingMatchesAsync(CancellationToken cancellationToken)
    {
        var minOffset = 0;
        await this.RescanPendingBufferForMatchAsync(minOffset, cancellationToken).ConfigureAwait(false);

        while (this.hasPendingMatch)
        {
            var appliedOffset = this.pendingMatchOffset;
            var beforeLength = this.pendingLength;
            var appliedLength = this.pendingMatch.matchLength;
            await this.ApplyPendingMatchIfReadyAsync(true, cancellationToken).ConfigureAwait(false);
            var afterLength = this.pendingLength;
            var replacementLength = appliedLength + (afterLength - beforeLength);
            minOffset = appliedOffset + Math.Max(1, replacementLength);
            await this.RescanPendingBufferForMatchAsync(minOffset, cancellationToken).ConfigureAwait(false);
        }
    }

    private void ApplyCandidateAtOffset(int ruleId, int offset, int matchLength)
    {
        var startTicks = this.StartRuleMetric(ruleId);
        var rule = this.plan.Rules[ruleId];
        var matchSpan = this.pendingBuffer.AsSpan(this.pendingStart + offset, matchLength);

        var context = new MatchContext(ruleId, matchLength, this.SnapshotMetrics());

        if (this.options.BeforeApply is not null && !this.options.BeforeApply(context))
            return;

        string? replacement = null;
        if (rule.Action == MatchAction.Replace)
        {
            replacement = rule.Replacement;
            if (replacement is null && rule.ReplacementFactoryWithContext is not null)
                replacement = rule.ReplacementFactoryWithContext(ruleId, matchSpan, this.SnapshotMetrics());
            else if (replacement is null && rule.ReplacementFactory is not null)
                replacement = rule.ReplacementFactory(ruleId, matchSpan);
        }

        rule.OnMatch?.Invoke(ruleId, matchSpan);

        if (rule.Action == MatchAction.None)
        {
            this.StopRuleMetric(ruleId, startTicks);

            return;
        }

        this.matchesApplied++;

        var originalLength = this.pendingLength;
        var tailCount = originalLength - (offset + matchLength);
        this.pendingLength = originalLength - matchLength;

        if (rule.Action == MatchAction.Remove || string.IsNullOrEmpty(replacement))
        {
            if (tailCount > 0)
                this.pendingBuffer.AsSpan(this.pendingStart + offset + matchLength, tailCount)
                    .CopyTo(this.pendingBuffer.AsSpan(this.pendingStart + offset));

            if (rule.Action == MatchAction.Remove)
                this.removals++;

            this.PublishMetrics();
            this.options.AfterApply?.Invoke(context);

            return;
        }

        this.EnsurePendingCapacity(replacement.Length - matchLength);

        if (tailCount > 0)
            this.pendingBuffer.AsSpan(this.pendingStart + offset + matchLength, tailCount)
                .CopyTo(this.pendingBuffer.AsSpan(this.pendingStart + offset + replacement.Length));

        replacement.AsSpan().CopyTo(this.pendingBuffer.AsSpan(this.pendingStart + offset));
        this.pendingLength = originalLength - matchLength + replacement.Length;

        this.replacements++;
        this.PublishMetrics();

        this.options.AfterApply?.Invoke(context);
    }

    private async ValueTask ApplyCandidateAtOffsetAsync(int ruleId, int offset, int matchLength, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var startTicks = this.StartRuleMetric(ruleId);
        var rule = this.plan.Rules[ruleId];
        var matchMemory = this.pendingBuffer.AsMemory(this.pendingStart + offset, matchLength);
        var context = new MatchContext(ruleId, matchLength, this.SnapshotMetrics());

        if (this.options.BeforeApplyAsync is not null)
        {
            if (!await this.options.BeforeApplyAsync(context).ConfigureAwait(false))
                return;
        }
        else if (this.options.BeforeApply is not null && !this.options.BeforeApply(context))
        {
            return;
        }

        string? replacement = null;
        if (rule.Action == MatchAction.Replace)
        {
            if (rule.Replacement is not null)
                replacement = rule.Replacement;
            else if (rule.ReplacementFactoryWithContextAsync is not null)
                replacement = await rule.ReplacementFactoryWithContextAsync(ruleId, matchMemory, context.Metrics).ConfigureAwait(false);
            else if (rule.ReplacementFactoryWithContext is not null)
                replacement = rule.ReplacementFactoryWithContext(ruleId, matchMemory.Span, context.Metrics);
            else if (rule.ReplacementFactoryAsync is not null)
                replacement = await rule.ReplacementFactoryAsync(ruleId, matchMemory).ConfigureAwait(false);
            else if (rule.ReplacementFactory is not null)
                replacement = rule.ReplacementFactory(ruleId, matchMemory.Span);
        }

        if (rule.OnMatchAsync is not null)
            await rule.OnMatchAsync(ruleId, matchMemory).ConfigureAwait(false);
        else
            rule.OnMatch?.Invoke(ruleId, matchMemory.Span);

        if (rule.Action == MatchAction.None)
        {
            this.StopRuleMetric(ruleId, startTicks);

            return;
        }

        this.matchesApplied++;

        var originalLength = this.pendingLength;
        var tailCount = originalLength - (offset + matchLength);
        this.pendingLength = originalLength - matchLength;

        if (rule.Action == MatchAction.Remove || string.IsNullOrEmpty(replacement))
        {
            if (tailCount > 0)
                this.pendingBuffer.AsSpan(this.pendingStart + offset + matchLength, tailCount)
                    .CopyTo(this.pendingBuffer.AsSpan(this.pendingStart + offset));

            if (rule.Action == MatchAction.Remove)
                this.removals++;

            this.StopRuleMetric(ruleId, startTicks);
            await this.PublishMetricsAsync().ConfigureAwait(false);
            await this.AfterApplyAsync(context).ConfigureAwait(false);

            return;
        }

        this.EnsurePendingCapacity(replacement.Length - matchLength);

        if (tailCount > 0)
            this.pendingBuffer.AsSpan(this.pendingStart + offset + matchLength, tailCount)
                .CopyTo(this.pendingBuffer.AsSpan(this.pendingStart + offset + replacement.Length));

        replacement.AsSpan().CopyTo(this.pendingBuffer.AsSpan(this.pendingStart + offset));
        this.pendingLength = originalLength - matchLength + replacement.Length;

        this.replacements++;
        this.StopRuleMetric(ruleId, startTicks);
        await this.PublishMetricsAsync().ConfigureAwait(false);

        await this.AfterApplyAsync(context).ConfigureAwait(false);
    }

    private void AppendChar(char c)
    {
        this.EnsurePendingCapacity(1);
        this.pendingBuffer[this.pendingStart + this.pendingLength] = c;
        this.pendingLength++;
    }

    private void EnsurePendingCapacity(int additional)
    {
        if (this.pendingBuffer.Length == 0)
        {
            var size = Math.Max(8, this.options.InitialBufferSize);
            if (size < additional)
                size = additional;

            this.pendingBuffer = this.arrayPool.Rent(size);
            this.pendingStart = 0;
            this.pendingLength = 0;

            return;
        }

        if (this.pendingStart + this.pendingLength + additional <= this.pendingBuffer.Length)
            return;

        if (this.pendingLength + additional <= this.pendingBuffer.Length)
        {
            this.CompactToFront();

            return;
        }

        var required = this.pendingLength + additional;
        var newSize = this.pendingBuffer.Length * 2;
        if (newSize < required)
            newSize = required;

        var newBuffer = this.arrayPool.Rent(newSize);
        this.pendingBuffer.AsSpan(this.pendingStart, this.pendingLength).CopyTo(newBuffer.AsSpan());

        this.arrayPool.Return(this.pendingBuffer, false);

        this.pendingBuffer = newBuffer;
        this.pendingStart = 0;
    }

    private void CompactToFront()
    {
        if (this.pendingStart == 0 || this.pendingLength == 0)
        {
            this.pendingStart = 0;

            return;
        }

        this.pendingBuffer.AsSpan(this.pendingStart, this.pendingLength).CopyTo(this.pendingBuffer.AsSpan());
        this.pendingStart = 0;
    }

    private void FlushSafePrefixSync(int safeCount)
    {
        if (safeCount <= 0)
            return;

        var span = this.pendingBuffer.AsSpan(this.pendingStart, safeCount);
        this.sink.Write(span);

        this.flushes++;
        this.PublishMetrics();

        this.pendingStart += safeCount;
        this.pendingLength -= safeCount;

        if (this.hasPendingMatch)
            this.pendingMatchOffset -= safeCount;

        if (this.pendingLength == 0)
        {
            this.pendingStart = 0;

            return;
        }

        if (this.pendingStart > this.pendingBuffer.Length / 2)
            this.CompactToFront();
    }

    private void FlushSafePrefixSyncForce()
    {
        var safeCount = this.pendingLength - this.plan.maxPending;
        if (this.hasPendingMatch && safeCount > this.pendingMatchOffset)
            safeCount = this.pendingMatchOffset;

        if (safeCount <= 0)
            return;

        this.FlushSafePrefixSync(safeCount);
    }

    private async Task FlushSafePrefixAsync(int safeCount, CancellationToken cancellationToken)
    {
        if (safeCount <= 0)
            return;

        var mem = this.pendingBuffer.AsMemory(this.pendingStart, safeCount);
        await this.sink.WriteAsync(mem, cancellationToken).ConfigureAwait(false);

        this.flushes++;
        await this.PublishMetricsAsync().ConfigureAwait(false);

        this.pendingStart += safeCount;
        this.pendingLength -= safeCount;

        if (this.hasPendingMatch)
            this.pendingMatchOffset -= safeCount;

        if (this.pendingLength == 0)
        {
            this.pendingStart = 0;

            return;
        }

        if (this.pendingStart > this.pendingBuffer.Length / 2)
            this.CompactToFront();
    }

    private async Task FlushSafePrefixAsyncForce(CancellationToken cancellationToken)
    {
        var safeCount = this.pendingLength - this.plan.maxPending;
        if (this.hasPendingMatch && safeCount > this.pendingMatchOffset)
            safeCount = this.pendingMatchOffset;

        if (safeCount <= 0)
            return;

        await this.FlushSafePrefixAsync(safeCount, cancellationToken).ConfigureAwait(false);
    }

    private void FlushAllSyncInternal()
    {
        this.ApplyAllPendingMatchesSync();

        if (this.pendingLength != 0)
        {
            var span = this.pendingBuffer.AsSpan(this.pendingStart, this.pendingLength);
            this.sink.Write(span);

            this.flushes++;
            this.PublishMetrics();
        }

        this.flushableSink?.FlushPending();

        this.pendingStart = 0;
        this.pendingLength = 0;
    }

    private async Task FlushAllAsyncInternal(CancellationToken cancellationToken)
    {
        await this.ApplyAllPendingMatchesAsync(cancellationToken).ConfigureAwait(false);

        if (this.pendingLength != 0)
        {
            var mem = this.pendingBuffer.AsMemory(this.pendingStart, this.pendingLength);
            await this.sink.WriteAsync(mem, cancellationToken).ConfigureAwait(false);

            this.flushes++;
            await this.PublishMetricsAsync().ConfigureAwait(false);
        }

        if (this.flushableSink is not null)
            await this.flushableSink.FlushPendingAsync(cancellationToken).ConfigureAwait(false);

        this.pendingStart = 0;
        this.pendingLength = 0;
    }

    private void FlushAllAndResetSyncInternal()
    {
        this.FlushAllSyncInternal();
        this.ordinalState = 0;
        this.ordinalIgnoreCaseState = 0;
    }

    private async Task FlushAllAndResetAsyncInternal(CancellationToken cancellationToken)
    {
        await this.FlushAllAsyncInternal(cancellationToken).ConfigureAwait(false);
        this.ordinalState = 0;
        this.ordinalIgnoreCaseState = 0;
    }

    private void ReturnPendingBuffer()
    {
        if (this.pendingBuffer.Length == 0)
            return;

        this.arrayPool.Return(this.pendingBuffer, this.options.ClearPooledBuffersOnDispose);
        this.pendingBuffer = [];
        this.pendingStart = 0;
        this.pendingLength = 0;
    }

    private RewriteMetrics SnapshotMetrics()
        => new(this.processedChars, this.matchesApplied, this.replacements, this.removals, this.flushes, this.pendingLength);

    private void PublishMetrics()
    {
        this.options.OnMetrics?.Invoke(this.SnapshotMetrics());
        this.PublishRuleMetrics();
    }

    private ValueTask PublishMetricsAsync()
    {
        if (this.options.OnMetricsAsync is not null)
            return this.InvokeAsyncMetrics();

        this.options.OnMetrics?.Invoke(this.SnapshotMetrics());
        this.PublishRuleMetrics();

        return ValueTask.CompletedTask;
    }

    private ValueTask AfterApplyAsync(MatchContext context)
    {
        if (this.options.AfterApplyAsync is not null)
            return this.options.AfterApplyAsync(context);

        this.options.AfterApply?.Invoke(context);

        return ValueTask.CompletedTask;
    }

    private async ValueTask InvokeAsyncMetrics()
    {
        await this.options.OnMetricsAsync!(this.SnapshotMetrics()).ConfigureAwait(false);
        this.PublishRuleMetrics();
    }

    private long StartRuleMetric(int ruleId)
    {
        if (!this.collectRuleMetrics || this.ruleHits is null || this.ruleElapsedTicks is null)
            return 0;

        this.ruleHits[ruleId]++;

        return Stopwatch.GetTimestamp();
    }

    private void StopRuleMetric(int ruleId, long startTicks)
    {
        if (!this.collectRuleMetrics || this.ruleElapsedTicks is null)
            return;

        if (startTicks == 0)
            return;

        this.ruleElapsedTicks[ruleId] += Stopwatch.GetTimestamp() - startTicks;
    }

    private void PublishRuleMetrics()
    {
        if (!this.collectRuleMetrics || (this.ruleHits is null && this.ruleElapsedTicks is null))
            return;

        if (this.options.OnRuleMetrics is null && this.options.OnRuleMetricsAsync is null)
            return;

        var frequency = Stopwatch.Frequency;
        var list = new List<RuleStat>(this.plan.RuleCount);
        for (var i = 0; i < this.plan.RuleCount; i++)
        {
            var hits = this.ruleHits![i];
            var elapsed = this.ruleElapsedTicks![i];

            if (hits == 0 && elapsed == 0)
                continue;

            var name = i < this.options.RuleNames.Length ? this.options.RuleNames[i] : null;
            var group = i < this.options.RuleGroups.Length ? this.options.RuleGroups[i] : null;
            list.Add(new(i, name, group, hits, TimeSpan.FromSeconds(elapsed / (double)frequency)));
        }

        if (list.Count == 0)
            return;

        if (this.options.OnRuleMetricsAsync is not null)
            this.options.OnRuleMetricsAsync(list).AsTask().GetAwaiter().GetResult();
        else
            this.options.OnRuleMetrics?.Invoke(list);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static char FoldCharOrdinalIgnoreCase(char c)
    {
        if ((uint)(c - 'a') <= (uint)('z' - 'a'))
            return (char)(c - 32);

        return char.ToUpperInvariant(c);
    }
}