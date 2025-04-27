// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Buffers;
using System.Runtime.CompilerServices;
using Itexoft.Core;
using Itexoft.Extensions;
using Itexoft.Text.Rewriting.Primitives;
using Itexoft.Text.Rewriting.Text.Internal.Gates;
using Itexoft.Text.Rewriting.Text.Internal.Matching;
using Itexoft.Text.Rewriting.Text.Internal.Sinks;
using Itexoft.Threading;
using TextPendingScanner =
    Itexoft.Algorithms.StateMachines.MatchScanning.PendingMatchScanner<Itexoft.Text.Rewriting.Text.Internal.Matching.TextAutomataState>;

namespace Itexoft.Text.Rewriting.Text.Internal.Engine;

/// <summary>
/// Shared filtering core that applies a compiled plan while emitting to a sink.
/// </summary>
internal sealed class TextRewriteEngine : IAsyncDisposable, IDisposable
{
    private readonly ArrayPool<char> arrayPool;
    private readonly IFlushableTextSink? flushableSink;
    private readonly Dictionary<(int ruleId, int offset, int length), bool> gateCache = new();
    private readonly bool hasAsyncCallbacks;
    private readonly TextRewriteOptions options;
    private readonly TextPendingScanner pendingMatchScanner;
    private readonly TextRewritePlan plan;
    private readonly bool[] ruleGateSnapshot;
    private readonly RuleMetricsTracker ruleMetrics;
    private readonly ITextSink sink;
    private readonly TextPendingMatchAutomata textAutomata;

    private Disposed disposed;
    private long flushes;
    private bool hasPendingMatch;
    private long matchesApplied;

    private char[] pendingBuffer;
    private int pendingLength;
    private MatchCandidate pendingMatch;
    private int pendingMatchOffset;
    private int pendingStart;
    private long processedChars;
    private long removals;
    private long replacements;
    private int rescanMinOffset;

    public TextRewriteEngine(TextRewritePlan plan, ITextSink sink, TextRewriteOptions? options = null)
    {
        this.plan = plan ?? throw new ArgumentNullException(nameof(plan));
        this.options = options ?? new TextRewriteOptions();
        this.arrayPool = this.options.ArrayPool ?? ArrayPool<char>.Shared;
        this.ruleMetrics = new(plan.RuleCount, this.options.OnRuleMetrics is not null || this.options.OnRuleMetricsAsync is not null);
        this.textAutomata = new();
        this.pendingMatchScanner = new(this.textAutomata);

        sink.Required();

        this.sink = new OutputSink(sink, this.options, this.SnapshotMetrics, this.arrayPool);
        this.flushableSink = this.sink as IFlushableTextSink;

        this.pendingBuffer = [];
        this.pendingStart = 0;
        this.pendingLength = 0;
        this.processedChars = 0;
        this.matchesApplied = 0;
        this.replacements = 0;
        this.removals = 0;
        this.flushes = 0;
        this.hasPendingMatch = false;
        this.pendingMatch = MatchCandidate.None;
        this.pendingMatchOffset = 0;
        this.rescanMinOffset = 0;

        this.hasAsyncCallbacks = plan.HasAsyncRules
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
        if (this.disposed.Enter())
            return;

        if (this.sink is IDisposable disposableSink)
            disposableSink.Dispose();

        this.ReturnPendingBuffer();

        GC.SuppressFinalize(this);
    }

    public void Write(char value)
    {
        this.disposed.ThrowIf();

        var flushCount = this.ProcessChar(value);

        if (flushCount != 0)
            this.FlushSafePrefixSync(flushCount);

        this.FlushSafePrefixSyncForce();
    }

    public void Write(ReadOnlySpan<char> buffer)
    {
        this.disposed.ThrowIf();

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

    public async Task WriteAsync(char value, CancelToken cancelToken)
    {
        this.disposed.ThrowIf();

        if (!this.hasAsyncCallbacks)
        {
            var flushCount = await this.ProcessCharAsync(value, cancelToken);

            if (flushCount != 0)
                await this.FlushSafePrefixAsync(flushCount, cancelToken).ConfigureAwait(false);

            await this.FlushSafePrefixAsyncForce(cancelToken).ConfigureAwait(false);

            return;
        }

        var asyncFlushCount = await this.ProcessCharAsync(value, cancelToken).ConfigureAwait(false);

        if (asyncFlushCount != 0)
            await this.FlushSafePrefixAsync(asyncFlushCount, cancelToken).ConfigureAwait(false);

        await this.FlushSafePrefixAsyncForce(cancelToken).ConfigureAwait(false);
    }

    public async Task WriteAsync(ReadOnlyMemory<char> buffer, CancelToken cancelToken)
    {
        this.disposed.ThrowIf();

        if (buffer.Length == 0)
            return;

        if (!this.hasAsyncCallbacks)
        {
            for (var i = 0; i < buffer.Length; i++)
            {
                cancelToken.ThrowIf();

                var flushCount = await this.ProcessCharAsync(buffer.Span[i], cancelToken);

                if (flushCount != 0)
                    await this.FlushSafePrefixAsync(flushCount, cancelToken).ConfigureAwait(false);
            }

            await this.FlushSafePrefixAsyncForce(cancelToken).ConfigureAwait(false);

            return;
        }

        for (var i = 0; i < buffer.Length; i++)
        {
            cancelToken.ThrowIf();

            var flushCount = await this.ProcessCharAsync(buffer.Span[i], cancelToken).ConfigureAwait(false);

            if (flushCount != 0)
                await this.FlushSafePrefixAsync(flushCount, cancelToken).ConfigureAwait(false);
        }

        await this.FlushSafePrefixAsyncForce(cancelToken).ConfigureAwait(false);
    }

    public void Flush()
    {
        this.disposed.ThrowIf();

        if (this.options.FlushBehavior == FlushBehavior.Commit)
            this.FlushAllAndResetSync();
        else
            this.FlushSafePrefixSyncForce();
    }

    public async Task FlushAsync(CancelToken cancelToken)
    {
        this.disposed.ThrowIf();

        if (this.options.FlushBehavior == FlushBehavior.Commit)
            await this.FlushAllAndResetAsync(cancelToken).ConfigureAwait(false);
        else
            await this.FlushSafePrefixAsyncForce(cancelToken).ConfigureAwait(false);
    }

    public void FlushAllSync()
    {
        if (this.disposed)
            return;

        this.FlushAllSyncInternal();
    }

    public async Task FlushAllAsync(CancelToken cancelToken)
    {
        if (this.disposed)
            return;

        await this.FlushAllAsyncInternal(cancelToken).ConfigureAwait(false);
    }

    public void FlushAllAndResetSync()
    {
        if (this.disposed)
            return;

        this.FlushAllAndResetSyncInternal();
    }

    public async Task FlushAllAndResetAsync(CancelToken cancelToken)
    {
        if (this.disposed)
            return;

        await this.FlushAllAndResetAsyncInternal(cancelToken).ConfigureAwait(false);
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

        this.RescanPendingBufferForMatch(this.rescanMinOffset);
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

    private async ValueTask<int> ProcessCharAsync(char c, CancelToken cancelToken)
    {
        cancelToken.ThrowIf();

        var normalized = this.options.InputNormalizer?.Invoke(c) ?? c;
        this.processedChars++;

        if (normalized == '\0')
        {
            await this.PublishMetricsAsync().ConfigureAwait(false);

            return 0;
        }

        this.AppendChar(normalized);

        await this.RescanPendingBufferForMatchAsync(this.rescanMinOffset, cancelToken).ConfigureAwait(false);
        await this.ApplyPendingMatchIfReadyAsync(false, cancelToken).ConfigureAwait(false);

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
    private async ValueTask<RuleGateSnapshot> CreateGateSnapshotAsync(CancelToken cancelToken)
    {
        cancelToken.ThrowIf();

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

        var chosen = this.ChooseBetter(this.pendingMatch, candidate.RuleId, candidate.MatchLength);

        if (chosen.RuleId == this.pendingMatch.RuleId
            && chosen.MatchLength == this.pendingMatch.MatchLength
            && chosen.Priority == this.pendingMatch.Priority
            && chosen.Order == this.pendingMatch.Order)
        {
            if (offset >= this.pendingMatchOffset)
                return;
        }
        else if (chosen.RuleId != candidate.RuleId)
            return;

        this.pendingMatch = candidate;
        this.pendingMatchOffset = offset;
        this.hasPendingMatch = true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private MatchCandidate ChooseBetter(MatchCandidate current, int ruleId, int matchLength)
    {
        if (ruleId < 0)
            return current;

        var rule = this.plan.Rules[ruleId];

        if (!current.HasValue)
            return new(ruleId, matchLength, rule.Priority, rule.Order);

        if (this.plan.selection == MatchSelection.LongestThenPriority)
        {
            if (matchLength > current.MatchLength)
                return new(ruleId, matchLength, rule.Priority, rule.Order);

            if (matchLength < current.MatchLength)
                return current;
        }
        else
        {
            if (rule.Priority < current.Priority)
                return new(ruleId, matchLength, rule.Priority, rule.Order);

            if (rule.Priority > current.Priority)
                return current;
        }

        if (rule.Priority < current.Priority)
            return new(ruleId, matchLength, rule.Priority, rule.Order);

        if (rule.Priority > current.Priority)
            return current;

        if (rule.Order < current.Order)
            return new(ruleId, matchLength, rule.Priority, rule.Order);

        if (rule.Order > current.Order)
            return current;

        if (matchLength > current.MatchLength)
            return new(ruleId, matchLength, rule.Priority, rule.Order);

        return current;
    }

    private void ApplyPendingMatchIfReady(bool force)
    {
        if (!this.hasPendingMatch)
            return;

        var appliedOffset = this.pendingMatchOffset;
        var appliedLength = this.pendingMatch.MatchLength;
        var beforeLength = this.pendingLength;

        var trailing = this.pendingLength - (this.pendingMatchOffset + this.pendingMatch.MatchLength);
        var requiredTrailing = this.plan.maxMatchLength - this.pendingMatch.MatchLength;

        if (requiredTrailing < 0)
            requiredTrailing = 0;

        if (!force && trailing < requiredTrailing)
            return;

        this.ApplyCandidateAtOffset(this.pendingMatch.RuleId, this.pendingMatchOffset, this.pendingMatch.MatchLength);
        var afterLength = this.pendingLength;
        var replacementLength = appliedLength + (afterLength - beforeLength);
        var newMinOffset = appliedOffset + Math.Max(1, replacementLength);

        if (newMinOffset < 0)
            newMinOffset = 0;

        if (newMinOffset > this.pendingLength)
            newMinOffset = this.pendingLength;

        this.rescanMinOffset = newMinOffset;
        this.hasPendingMatch = false;
        this.pendingMatch = MatchCandidate.None;
        this.pendingMatchOffset = 0;
        this.gateCache.Clear();
    }

    private async ValueTask ApplyPendingMatchIfReadyAsync(bool force, CancelToken cancelToken)
    {
        if (!this.hasPendingMatch)
            return;

        var appliedOffset = this.pendingMatchOffset;
        var appliedLength = this.pendingMatch.MatchLength;
        var beforeLength = this.pendingLength;

        var trailing = this.pendingLength - (this.pendingMatchOffset + this.pendingMatch.MatchLength);
        var requiredTrailing = this.plan.maxMatchLength - this.pendingMatch.MatchLength;

        if (requiredTrailing < 0)
            requiredTrailing = 0;

        if (!force && trailing < requiredTrailing)
            return;

        await this.ApplyCandidateAtOffsetAsync(this.pendingMatch.RuleId, this.pendingMatchOffset, this.pendingMatch.MatchLength, cancelToken)
            .ConfigureAwait(false);

        var afterLength = this.pendingLength;
        var replacementLength = appliedLength + (afterLength - beforeLength);
        var newMinOffset = appliedOffset + Math.Max(1, replacementLength);

        if (newMinOffset < 0)
            newMinOffset = 0;

        if (newMinOffset > this.pendingLength)
            newMinOffset = this.pendingLength;

        this.rescanMinOffset = newMinOffset;
        this.hasPendingMatch = false;
        this.pendingMatch = MatchCandidate.None;
        this.pendingMatchOffset = 0;
        this.gateCache.Clear();
    }

    private void RescanPendingBufferForMatch(int minOffset = 0, SyncAsyncLazy<RuleGateSnapshot>? gateLazy = null)
    {
        this.hasPendingMatch = false;
        this.pendingMatch = MatchCandidate.None;
        this.pendingMatchOffset = 0;

        if (this.pendingLength == 0)
            return;

        var state = this.pendingMatchScanner.Create(this.pendingBuffer, this.pendingStart, this.pendingLength);
        state.automataState.plan = this.plan;
        state.automataState.minOffset = minOffset;
        state.automataState.ordinalState = 0;
        state.automataState.ordinalIgnoreCaseState = 0;

        var gate = gateLazy ?? this.CreateGateLazy();
        var gateHasValue = false;
        var gateResult = false;

        while (true)
        {
            TextPendingScanner.PendingScanAction action;
            int ruleId;
            int matchLength;
            int offset;

            this.pendingMatchScanner.Step(ref state, gateHasValue, gateResult, out action, out ruleId, out matchLength, out offset);

            if (action == TextPendingScanner.PendingScanAction.Completed)
                return;

            if (action == TextPendingScanner.PendingScanAction.GateRequest)
            {
                if (this.gateCache.TryGetValue((ruleId, offset, matchLength), out var cached))
                {
                    gateResult = cached;
                    gateHasValue = true;

                    continue;
                }

                gateResult = gate.GetOrCreate().Allows(ruleId);
                gateHasValue = true;
                this.gateCache[(ruleId, offset, matchLength)] = gateResult;

                continue;
            }

            var rule = this.plan.Rules[ruleId];
            var candidate = new MatchCandidate(ruleId, matchLength, rule.Priority, rule.Order);
            this.SetPendingMatch(candidate, offset);
            gateHasValue = false;
        }
    }

    private async Task RescanPendingBufferForMatchAsync(int minOffset, CancelToken cancelToken, SyncAsyncLazy<RuleGateSnapshot>? gateLazy = null)
    {
        this.hasPendingMatch = false;
        this.pendingMatch = MatchCandidate.None;
        this.pendingMatchOffset = 0;

        if (this.pendingLength == 0)
            return;

        var state = this.pendingMatchScanner.Create(this.pendingBuffer, this.pendingStart, this.pendingLength);
        state.automataState.plan = this.plan;
        state.automataState.minOffset = minOffset;
        state.automataState.ordinalState = 0;
        state.automataState.ordinalIgnoreCaseState = 0;

        var gate = gateLazy ?? this.CreateGateLazyAsync();
        var gateHasValue = false;
        var gateResult = false;

        while (true)
        {
            cancelToken.ThrowIf();

            TextPendingScanner.PendingScanAction action;
            int ruleId;
            int matchLength;
            int offset;

            this.pendingMatchScanner.Step(ref state, gateHasValue, gateResult, out action, out ruleId, out matchLength, out offset);

            if (action == TextPendingScanner.PendingScanAction.Completed)
                return;

            if (action == TextPendingScanner.PendingScanAction.GateRequest)
            {
                if (this.gateCache.TryGetValue((ruleId, offset, matchLength), out var cached))
                {
                    gateResult = cached;
                    gateHasValue = true;

                    continue;
                }

                gateResult = (await gate.GetOrCreateAsync(cancelToken).ConfigureAwait(false)).Allows(ruleId);
                gateHasValue = true;
                this.gateCache[(ruleId, offset, matchLength)] = gateResult;

                continue;
            }

            var rule = this.plan.Rules[ruleId];
            var candidate = new MatchCandidate(ruleId, matchLength, rule.Priority, rule.Order);
            this.SetPendingMatch(candidate, offset);
            gateHasValue = false;
        }
    }

    private void ApplyAllPendingMatchesSync()
    {
        this.rescanMinOffset = 0;
        var minOffset = 0;
        var gate = this.CreateGateLazy();
        this.RescanPendingBufferForMatch(minOffset, gate);

        while (this.hasPendingMatch)
        {
            var appliedOffset = this.pendingMatchOffset;
            var beforeLength = this.pendingLength;
            var appliedLength = this.pendingMatch.MatchLength;
            this.ApplyPendingMatchIfReady(true);
            var afterLength = this.pendingLength;
            var replacementLength = appliedLength + (afterLength - beforeLength);
            minOffset = appliedOffset + Math.Max(1, replacementLength);
            this.RescanPendingBufferForMatch(minOffset, gate);
        }
    }

    private async Task ApplyAllPendingMatchesAsync(CancelToken cancelToken)
    {
        this.rescanMinOffset = 0;
        var minOffset = 0;
        var gate = this.CreateGateLazyAsync();
        await this.RescanPendingBufferForMatchAsync(minOffset, cancelToken, gate).ConfigureAwait(false);

        while (this.hasPendingMatch)
        {
            var appliedOffset = this.pendingMatchOffset;
            var beforeLength = this.pendingLength;
            var appliedLength = this.pendingMatch.MatchLength;
            await this.ApplyPendingMatchIfReadyAsync(true, cancelToken).ConfigureAwait(false);
            var afterLength = this.pendingLength;
            var replacementLength = appliedLength + (afterLength - beforeLength);
            minOffset = appliedOffset + Math.Max(1, replacementLength);
            await this.RescanPendingBufferForMatchAsync(minOffset, cancelToken, gate).ConfigureAwait(false);
        }
    }

    private void ApplyCandidateAtOffset(int ruleId, int offset, int matchLength)
    {
        var startTicks = this.StartRuleMetric(ruleId);
        var rule = this.plan.Rules[ruleId];
        var matchSpan = this.pendingBuffer.AsSpan(this.pendingStart + offset, matchLength);

        var metrics = this.SnapshotMetrics();
        var matchProcessedChars = this.processedChars - (this.pendingLength - (offset + matchLength));

        if (matchProcessedChars < 0)
            matchProcessedChars = 0;

        metrics = metrics with { ProcessedChars = matchProcessedChars };

        var context = new MatchContext(ruleId, matchLength, metrics);

        if (this.options.BeforeApply is not null && !this.options.BeforeApply(context))
            return;

        string? replacement = null;

        if (rule.Action == MatchAction.Replace)
        {
            replacement = rule.Replacement;

            if (replacement is null && rule.ReplacementFactoryWithContext is not null)
                replacement = rule.ReplacementFactoryWithContext(ruleId, matchSpan, metrics);
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
            {
                this.pendingBuffer.AsSpan(this.pendingStart + offset + matchLength, tailCount)
                    .CopyTo(this.pendingBuffer.AsSpan(this.pendingStart + offset));
            }

            if (rule.Action == MatchAction.Remove)
                this.removals++;

            this.PublishMetrics();
            this.options.AfterApply?.Invoke(context);

            return;
        }

        this.EnsurePendingCapacity(replacement.Length - matchLength);

        if (tailCount > 0)
        {
            this.pendingBuffer.AsSpan(this.pendingStart + offset + matchLength, tailCount)
                .CopyTo(this.pendingBuffer.AsSpan(this.pendingStart + offset + replacement.Length));
        }

        replacement.AsSpan().CopyTo(this.pendingBuffer.AsSpan(this.pendingStart + offset));
        this.pendingLength = originalLength - matchLength + replacement.Length;

        this.replacements++;
        this.PublishMetrics();

        this.options.AfterApply?.Invoke(context);
    }

    private async ValueTask ApplyCandidateAtOffsetAsync(int ruleId, int offset, int matchLength, CancelToken cancelToken)
    {
        cancelToken.ThrowIf();

        var startTicks = this.StartRuleMetric(ruleId);
        var rule = this.plan.Rules[ruleId];
        var matchMemory = this.pendingBuffer.AsMemory(this.pendingStart + offset, matchLength);

        var metrics = this.SnapshotMetrics();
        var matchProcessedChars = this.processedChars - (this.pendingLength - (offset + matchLength));

        if (matchProcessedChars < 0)
            matchProcessedChars = 0;

        metrics = metrics with { ProcessedChars = matchProcessedChars };

        var context = new MatchContext(ruleId, matchLength, metrics);

        if (this.options.BeforeApplyAsync is not null)
        {
            if (!await this.options.BeforeApplyAsync(context).ConfigureAwait(false))
                return;
        }
        else if (this.options.BeforeApply is not null && !this.options.BeforeApply(context))
            return;

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
            {
                this.pendingBuffer.AsSpan(this.pendingStart + offset + matchLength, tailCount)
                    .CopyTo(this.pendingBuffer.AsSpan(this.pendingStart + offset));
            }

            if (rule.Action == MatchAction.Remove)
                this.removals++;

            this.StopRuleMetric(ruleId, startTicks);
            await this.PublishMetricsAsync().ConfigureAwait(false);
            await this.AfterApplyAsync(context).ConfigureAwait(false);

            return;
        }

        this.EnsurePendingCapacity(replacement.Length - matchLength);

        if (tailCount > 0)
        {
            this.pendingBuffer.AsSpan(this.pendingStart + offset + matchLength, tailCount)
                .CopyTo(this.pendingBuffer.AsSpan(this.pendingStart + offset + replacement.Length));
        }

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

        if (this.rescanMinOffset > 0)
        {
            this.rescanMinOffset -= safeCount;

            if (this.rescanMinOffset < 0)
                this.rescanMinOffset = 0;
        }

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

    private async Task FlushSafePrefixAsync(int safeCount, CancelToken cancelToken)
    {
        if (safeCount <= 0)
            return;

        var mem = this.pendingBuffer.AsMemory(this.pendingStart, safeCount);
        await this.sink.WriteAsync(mem, cancelToken).ConfigureAwait(false);

        this.flushes++;
        await this.PublishMetricsAsync().ConfigureAwait(false);

        this.pendingStart += safeCount;
        this.pendingLength -= safeCount;

        if (this.rescanMinOffset > 0)
        {
            this.rescanMinOffset -= safeCount;

            if (this.rescanMinOffset < 0)
                this.rescanMinOffset = 0;
        }

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

    private async Task FlushSafePrefixAsyncForce(CancelToken cancelToken)
    {
        var safeCount = this.pendingLength - this.plan.maxPending;

        if (this.hasPendingMatch && safeCount > this.pendingMatchOffset)
            safeCount = this.pendingMatchOffset;

        if (safeCount <= 0)
            return;

        await this.FlushSafePrefixAsync(safeCount, cancelToken).ConfigureAwait(false);
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
        this.rescanMinOffset = 0;
        this.gateCache.Clear();
    }

    private async Task FlushAllAsyncInternal(CancelToken cancelToken)
    {
        await this.ApplyAllPendingMatchesAsync(cancelToken).ConfigureAwait(false);

        if (this.pendingLength != 0)
        {
            var mem = this.pendingBuffer.AsMemory(this.pendingStart, this.pendingLength);
            await this.sink.WriteAsync(mem, cancelToken).ConfigureAwait(false);

            this.flushes++;
            await this.PublishMetricsAsync().ConfigureAwait(false);
        }

        if (this.flushableSink is not null)
            await this.flushableSink.FlushPendingAsync(cancelToken).ConfigureAwait(false);

        this.pendingStart = 0;
        this.pendingLength = 0;
        this.rescanMinOffset = 0;
        this.gateCache.Clear();
    }

    private void FlushAllAndResetSyncInternal()
    {
        this.FlushAllSyncInternal();
        this.gateCache.Clear();
    }

    private async Task FlushAllAndResetAsyncInternal(CancelToken cancelToken)
    {
        await this.FlushAllAsyncInternal(cancelToken).ConfigureAwait(false);
        this.gateCache.Clear();
    }

    private void ReturnPendingBuffer()
    {
        if (this.pendingBuffer.Length == 0)
            return;

        this.arrayPool.Return(this.pendingBuffer, this.options.ClearPooledBuffersOnDispose);
        this.pendingBuffer = [];
        this.pendingStart = 0;
        this.pendingLength = 0;
        this.rescanMinOffset = 0;
        this.gateCache.Clear();
    }

    private RewriteMetrics SnapshotMetrics() => new(
        this.processedChars,
        this.matchesApplied,
        this.replacements,
        this.removals,
        this.flushes,
        this.pendingLength);

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

        if (this.options.OnRuleMetricsAsync is not null)
            return this.PublishRuleMetricsAsync();

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
        await this.PublishRuleMetricsAsync().ConfigureAwait(false);
    }

    private long StartRuleMetric(int ruleId) => this.ruleMetrics.Start(ruleId);

    private void StopRuleMetric(int ruleId, long startTicks) => this.ruleMetrics.Stop(ruleId, startTicks);

    private void PublishRuleMetrics()
    {
        var stats = this.ruleMetrics.BuildStats(this.plan, this.options);

        if (stats.Count == 0)
            return;

        if (this.options.OnRuleMetricsAsync is not null)
            _ = this.options.OnRuleMetricsAsync(stats);

        this.options.OnRuleMetrics?.Invoke(stats);
    }

    private async ValueTask PublishRuleMetricsAsync()
    {
        var stats = this.ruleMetrics.BuildStats(this.plan, this.options);

        if (stats.Count == 0)
            return;

        if (this.options.OnRuleMetricsAsync is not null)
            await this.options.OnRuleMetricsAsync(stats).ConfigureAwait(false);

        this.options.OnRuleMetrics?.Invoke(stats);
    }
}
