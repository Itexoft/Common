// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Itexoft.Text.Filtering;

/// <summary>
/// Runtime tuning options for filtering pipelines.
/// </summary>
public sealed class FilteringTextProcessorOptions
{
    /// <summary>
    /// Gets or sets the number of safe characters that triggers a synchronous write; 0 writes immediately.
    /// </summary>
    public int RightWriteBlockSize { get; init; } = 4096;

    /// <summary>
    /// Gets or sets the initial buffer capacity when the processor first allocates.
    /// </summary>
    public int InitialBufferSize { get; init; } = 256;

    /// <summary>
    /// Gets or sets the maximum buffered characters before forcing a flush.
    /// </summary>
    public int MaxBufferedChars { get; init; } = 1_048_576;

    /// <summary>
    /// Gets or sets how buffered text is handled when flushing.
    /// </summary>
    public FlushBehavior FlushBehavior { get; init; } = FlushBehavior.PreserveMatchTail;

    /// <summary>
    /// Gets or sets a value indicating whether pooled buffers are zeroed when returned.
    /// </summary>
    public bool ClearPooledBuffersOnDispose { get; init; }

    /// <summary>
    /// Gets or sets the array pool to rent character buffers from.
    /// </summary>
    public ArrayPool<char>? ArrayPool { get; init; }

    /// <summary>
    /// Gets or sets an optional input normalizer applied to each incoming character before matching.
    /// </summary>
    public Func<char, char>? InputNormalizer { get; init; }

    /// <summary>
    /// Gets or sets an optional output filter applied to safe spans before they are written to the sink.
    /// </summary>
    public Func<ReadOnlySpan<char>, FilteringMetrics, string?>? OutputFilter { get; init; }

    /// <summary>
    /// Gets or sets a predicate that decides whether a rule is enabled for the current metrics.
    /// </summary>
    public Func<int, FilteringMetrics, bool>? RuleGate { get; init; }

    /// <summary>
    /// Gets or sets a callback invoked before applying a match; return false to skip mutation.
    /// </summary>
    public Func<MatchContext, bool>? BeforeApply { get; init; }

    /// <summary>
    /// Gets or sets a callback invoked after a match has been applied.
    /// </summary>
    public Action<MatchContext>? AfterApply { get; init; }

    /// <summary>
    /// Gets or sets a callback invoked when metrics change.
    /// </summary>
    public Action<FilteringMetrics>? OnMetrics { get; init; }
}

/// <summary>
/// Shared filtering core that applies a compiled plan while emitting to a sink.
/// </summary>
internal sealed class FilteringTextProcessor : IAsyncDisposable, IDisposable
{
    private readonly ArrayPool<char> arrayPool;
    private readonly FilteringTextProcessorOptions options;
    private readonly TextFilterPlan plan;
    private readonly IFilteringTextSink sink;

    private bool disposed;
    private long flushes;
    private long matchesApplied;
    private int ordinalIgnoreCaseState;
    private int ordinalState;

    private char[] pendingBuffer;
    private int pendingLength;
    private int pendingStart;
    private long processedChars;
    private long removals;
    private long replacements;

    public FilteringTextProcessor(TextFilterPlan plan, IFilteringTextSink sink, FilteringTextProcessorOptions? options = null)
    {
        this.plan = plan ?? throw new ArgumentNullException(nameof(plan));
        this.options = options ?? new FilteringTextProcessorOptions();
        this.arrayPool = this.options.ArrayPool ?? ArrayPool<char>.Shared;
        this.sink = sink ?? throw new ArgumentNullException(nameof(sink));

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

        if (this.options.InitialBufferSize < 0)
            throw new ArgumentOutOfRangeException(nameof(options), "InitialBufferSize must be >= 0.");
        if (this.options.MaxBufferedChars <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "MaxBufferedChars must be > 0.");
        if (this.options.RightWriteBlockSize < 0)
            throw new ArgumentOutOfRangeException(nameof(options), "RightWriteBlockSize must be >= 0.");
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

        this.returnPendingBuffer();
        this.disposed = true;
        GC.SuppressFinalize(this);
    }

    public void Write(char value)
    {
        this.throwIfDisposed();

        var flushCount = this.processChar(value);
        if (flushCount != 0)
            this.flushSafePrefixSync(flushCount);

        this.flushSafePrefixSyncForce();
    }

    public void Write(ReadOnlySpan<char> buffer)
    {
        this.throwIfDisposed();

        if (buffer.Length == 0)
            return;

        for (var i = 0; i < buffer.Length; i++)
        {
            var flushCount = this.processChar(buffer[i]);
            if (flushCount != 0)
                this.flushSafePrefixSync(flushCount);
        }

        this.flushSafePrefixSyncForce();
    }

    public async Task WriteAsync(char value, CancellationToken cancellationToken)
    {
        this.throwIfDisposed();

        var flushCount = this.processChar(value);
        if (flushCount != 0)
            await this.flushSafePrefixAsync(flushCount, cancellationToken).ConfigureAwait(false);

        await this.flushSafePrefixAsyncForce(cancellationToken).ConfigureAwait(false);
    }

    public async Task WriteAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken)
    {
        this.throwIfDisposed();

        if (buffer.Length == 0)
            return;

        for (var i = 0; i < buffer.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var flushCount = this.processChar(buffer.Span[i]);
            if (flushCount != 0)
                await this.flushSafePrefixAsync(flushCount, cancellationToken).ConfigureAwait(false);
        }

        await this.flushSafePrefixAsyncForce(cancellationToken).ConfigureAwait(false);
    }

    public void Flush()
    {
        this.throwIfDisposed();

        if (this.options.FlushBehavior == FlushBehavior.Commit)
            this.flushAllAndResetSync();
        else
            this.flushSafePrefixSyncForce();
    }

    public async Task FlushAsync(CancellationToken cancellationToken)
    {
        this.throwIfDisposed();

        if (this.options.FlushBehavior == FlushBehavior.Commit)
            await this.flushAllAndResetAsync(cancellationToken).ConfigureAwait(false);
        else
            await this.flushSafePrefixAsyncForce(cancellationToken).ConfigureAwait(false);
    }

    public void FlushAllSync()
    {
        this.throwIfDisposed();

        this.flushAllSync();
    }

    public async Task FlushAllAsync(CancellationToken cancellationToken)
    {
        this.throwIfDisposed();

        await this.flushAllAsync(cancellationToken).ConfigureAwait(false);
    }

    public void FlushAllAndResetSync()
    {
        this.throwIfDisposed();

        this.flushAllAndResetSync();
    }

    public async Task FlushAllAndResetAsync(CancellationToken cancellationToken)
    {
        this.throwIfDisposed();

        await this.flushAllAndResetAsync(cancellationToken).ConfigureAwait(false);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void throwIfDisposed()
    {
        if (!this.disposed)
            return;

        throw new ObjectDisposedException(nameof(FilteringTextProcessor));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int processChar(char c)
    {
        var normalized = this.options.InputNormalizer is null ? c : this.options.InputNormalizer(c);
        this.processedChars++;
        if (normalized == '\0')
        {
            this.publishMetrics();

            return 0;
        }

        this.appendChar(normalized);

        var candidate = this.findBestCandidate(normalized);
        if (candidate.ruleId >= 0)
            this.applyCandidate(candidate.ruleId, candidate.matchLength);

        var safeCount = this.pendingLength - this.plan.maxPending;

        if (safeCount <= 0)
            return 0;

        if (this.options.RightWriteBlockSize == 0 || safeCount >= this.options.RightWriteBlockSize)
        {
            this.publishMetrics();

            return safeCount;
        }

        if (this.pendingLength >= this.options.MaxBufferedChars)
        {
            this.publishMetrics();

            return safeCount;
        }

        this.publishMetrics();

        return 0;
    }

    private MatchCandidate findBestCandidate(char currentChar)
    {
        var best = MatchCandidate.None;

        if (this.plan.ordinalAutomaton is not null)
        {
            this.ordinalState = this.plan.ordinalAutomaton.Step(this.ordinalState, currentChar);
            var ruleId = this.plan.ordinalAutomaton.GetBestRuleId(this.ordinalState);
            if (ruleId >= 0 && this.isRuleEnabled(ruleId))
                best = this.chooseBetter(best, ruleId, this.plan.rules[ruleId].fixedLength);
        }

        if (this.plan.ordinalIgnoreCaseAutomaton is not null)
        {
            var folded = foldCharOrdinalIgnoreCase(currentChar);
            this.ordinalIgnoreCaseState = this.plan.ordinalIgnoreCaseAutomaton.Step(this.ordinalIgnoreCaseState, folded);
            var ruleId = this.plan.ordinalIgnoreCaseAutomaton.GetBestRuleId(this.ordinalIgnoreCaseState);
            if (ruleId >= 0 && this.isRuleEnabled(ruleId))
                best = this.chooseBetter(best, ruleId, this.plan.rules[ruleId].fixedLength);
        }

        if (this.plan.regexRules.Length != 0 || this.plan.customRules.Length != 0)
        {
            var pendingTotal = this.pendingLength;
            if (pendingTotal != 0)
            {
                for (var i = 0; i < this.plan.regexRules.Length; i++)
                {
                    var rr = this.plan.regexRules[i];
                    var tailLen = pendingTotal < rr.maxMatchLength ? pendingTotal : rr.maxMatchLength;

                    if (tailLen == 0)
                        continue;

                    var tailSpan = this.pendingBuffer.AsSpan(this.pendingStart + pendingTotal - tailLen, tailLen);

                    var bestLen = 0;
                    foreach (var m in rr.regex.EnumerateMatches(tailSpan))
                        if (m.Length != 0 && m.Index + m.Length == tailLen && m.Length > bestLen)
                            bestLen = m.Length;

                    if (bestLen != 0)
                        best = this.isRuleEnabled(rr.ruleId) ? this.chooseBetter(best, rr.ruleId, bestLen) : best;
                }

                for (var i = 0; i < this.plan.customRules.Length; i++)
                {
                    var cr = this.plan.customRules[i];
                    var tailLen = pendingTotal < cr.maxMatchLength ? pendingTotal : cr.maxMatchLength;

                    if (tailLen == 0)
                        continue;

                    var tailSpan = this.pendingBuffer.AsSpan(this.pendingStart + pendingTotal - tailLen, tailLen);
                    var matchLen = cr.matcher(tailSpan);

                    if (matchLen > 0 && matchLen <= tailLen)
                        best = this.isRuleEnabled(cr.ruleId) ? this.chooseBetter(best, cr.ruleId, matchLen) : best;
                }
            }
        }

        return best;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool isRuleEnabled(int ruleId)
    {
        if (this.options.RuleGate is null)
            return true;

        return this.options.RuleGate(ruleId, this.snapshotMetrics());
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private MatchCandidate chooseBetter(MatchCandidate current, int ruleId, int matchLength)
    {
        var rule = this.plan.rules[ruleId];

        if (!current.HasValue)
            return new(ruleId, matchLength, rule.priority, rule.order);

        if (this.plan.selection == MatchSelection.LongestThenPriority)
        {
            if (matchLength > current.matchLength)
                return new(ruleId, matchLength, rule.priority, rule.order);

            if (matchLength < current.matchLength)
                return current;
        }
        else
        {
            if (rule.priority < current.priority)
                return new(ruleId, matchLength, rule.priority, rule.order);

            if (rule.priority > current.priority)
                return current;
        }

        if (rule.priority < current.priority)
            return new(ruleId, matchLength, rule.priority, rule.order);

        if (rule.priority > current.priority)
            return current;

        if (rule.order < current.order)
            return new(ruleId, matchLength, rule.priority, rule.order);

        if (rule.order > current.order)
            return current;

        if (matchLength > current.matchLength)
            return new(ruleId, matchLength, rule.priority, rule.order);

        return current;
    }

    private void applyCandidate(int ruleId, int matchLength)
    {
        var rule = this.plan.rules[ruleId];
        var matchSpan = this.pendingBuffer.AsSpan(this.pendingStart + this.pendingLength - matchLength, matchLength);

        var context = new MatchContext(ruleId, matchLength, this.snapshotMetrics());

        if (this.options.BeforeApply is not null && !this.options.BeforeApply(context))
            return;

        string? replacement = null;
        if (rule.action == MatchAction.Replace)
        {
            replacement = rule.replacement;
            if (replacement is null && rule.replacementFactoryWithContext is not null)
                replacement = rule.replacementFactoryWithContext(ruleId, matchSpan, this.snapshotMetrics());
            else if (replacement is null && rule.replacementFactory is not null)
                replacement = rule.replacementFactory(ruleId, matchSpan);
        }

        rule.onMatch?.Invoke(ruleId, matchSpan);

        if (rule.action == MatchAction.None)
            return;

        this.pendingLength -= matchLength;

        this.matchesApplied++;

        if (rule.action == MatchAction.Remove)
        {
            this.removals++;
            this.publishMetrics();
            this.options.AfterApply?.Invoke(context);

            return;
        }

        if (string.IsNullOrEmpty(replacement))
        {
            this.publishMetrics();
            this.options.AfterApply?.Invoke(context);

            return;
        }

        this.replacements++;
        this.appendSpan(replacement.AsSpan());
        this.publishMetrics();

        this.options.AfterApply?.Invoke(context);
    }

    private void appendChar(char c)
    {
        this.ensurePendingCapacity(1);
        this.pendingBuffer[this.pendingStart + this.pendingLength] = c;
        this.pendingLength++;
    }

    private void appendSpan(ReadOnlySpan<char> s)
    {
        if (s.Length == 0)
            return;

        this.ensurePendingCapacity(s.Length);
        s.CopyTo(this.pendingBuffer.AsSpan(this.pendingStart + this.pendingLength));
        this.pendingLength += s.Length;
    }

    private void ensurePendingCapacity(int additional)
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
            this.compactToFront();

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

    private void compactToFront()
    {
        if (this.pendingStart == 0 || this.pendingLength == 0)
        {
            this.pendingStart = 0;

            return;
        }

        this.pendingBuffer.AsSpan(this.pendingStart, this.pendingLength).CopyTo(this.pendingBuffer.AsSpan());
        this.pendingStart = 0;
    }

    private void flushSafePrefixSync(int safeCount)
    {
        if (safeCount <= 0)
            return;

        var span = this.pendingBuffer.AsSpan(this.pendingStart, safeCount);
        var filtered = this.options.OutputFilter?.Invoke(span, this.snapshotMetrics());

        if (filtered is null)
            this.sink.Write(span);
        else if (filtered.Length != 0)
            this.sink.Write(filtered.AsSpan());

        this.flushes++;
        this.publishMetrics();

        this.pendingStart += safeCount;
        this.pendingLength -= safeCount;

        if (this.pendingLength == 0)
        {
            this.pendingStart = 0;

            return;
        }

        if (this.pendingStart > this.pendingBuffer.Length / 2)
            this.compactToFront();
    }

    private void flushSafePrefixSyncForce()
    {
        var safeCount = this.pendingLength - this.plan.maxPending;

        if (safeCount <= 0)
            return;

        this.flushSafePrefixSync(safeCount);
    }

    private async Task flushSafePrefixAsync(int safeCount, CancellationToken cancellationToken)
    {
        if (safeCount <= 0)
            return;

        var mem = this.pendingBuffer.AsMemory(this.pendingStart, safeCount);
        var filter = this.options.OutputFilter;
        if (filter is null)
        {
            await this.sink.WriteAsync(mem, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var transformed = filter(mem.Span, this.snapshotMetrics());
            if (!string.IsNullOrEmpty(transformed))
                await this.sink.WriteAsync(transformed.AsMemory(), cancellationToken).ConfigureAwait(false);
        }

        this.flushes++;
        this.publishMetrics();

        this.pendingStart += safeCount;
        this.pendingLength -= safeCount;

        if (this.pendingLength == 0)
        {
            this.pendingStart = 0;

            return;
        }

        if (this.pendingStart > this.pendingBuffer.Length / 2)
            this.compactToFront();
    }

    private async Task flushSafePrefixAsyncForce(CancellationToken cancellationToken)
    {
        var safeCount = this.pendingLength - this.plan.maxPending;

        if (safeCount <= 0)
            return;

        await this.flushSafePrefixAsync(safeCount, cancellationToken).ConfigureAwait(false);
    }

    private void flushAllSync()
    {
        if (this.pendingLength != 0)
        {
            var span = this.pendingBuffer.AsSpan(this.pendingStart, this.pendingLength);
            var filtered = this.options.OutputFilter?.Invoke(span, this.snapshotMetrics());
            if (filtered is null)
                this.sink.Write(span);
            else if (filtered.Length != 0)
                this.sink.Write(filtered.AsSpan());

            this.flushes++;
            this.publishMetrics();
        }

        this.pendingStart = 0;
        this.pendingLength = 0;
    }

    private async Task flushAllAsync(CancellationToken cancellationToken)
    {
        if (this.pendingLength != 0)
        {
            var mem = this.pendingBuffer.AsMemory(this.pendingStart, this.pendingLength);
            var filter = this.options.OutputFilter;
            if (filter is null)
            {
                await this.sink.WriteAsync(mem, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                var transformed = filter(mem.Span, this.snapshotMetrics());
                if (!string.IsNullOrEmpty(transformed))
                    await this.sink.WriteAsync(transformed.AsMemory(), cancellationToken).ConfigureAwait(false);
            }

            this.flushes++;
            this.publishMetrics();
        }

        this.pendingStart = 0;
        this.pendingLength = 0;
    }

    private void flushAllAndResetSync()
    {
        this.flushAllSync();
        this.ordinalState = 0;
        this.ordinalIgnoreCaseState = 0;
    }

    private async Task flushAllAndResetAsync(CancellationToken cancellationToken)
    {
        await this.flushAllAsync(cancellationToken).ConfigureAwait(false);
        this.ordinalState = 0;
        this.ordinalIgnoreCaseState = 0;
    }

    private void returnPendingBuffer()
    {
        if (this.pendingBuffer.Length == 0)
            return;

        this.arrayPool.Return(this.pendingBuffer, this.options.ClearPooledBuffersOnDispose);
        this.pendingBuffer = [];
        this.pendingStart = 0;
        this.pendingLength = 0;
    }

    private FilteringMetrics snapshotMetrics()
        => new(this.processedChars, this.matchesApplied, this.replacements, this.removals, this.flushes, this.pendingLength);

    private void publishMetrics()
    {
        this.options.OnMetrics?.Invoke(this.snapshotMetrics());
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static char foldCharOrdinalIgnoreCase(char c)
    {
        if ((uint)(c - 'a') <= (uint)('z' - 'a'))
            return (char)(c - 32);

        return char.ToUpperInvariant(c);
    }

    private readonly struct MatchCandidate
    {
        public static MatchCandidate None => new(-1, 0, 0, 0);

        public readonly int ruleId;
        public readonly int matchLength;
        public readonly int priority;
        public readonly int order;

        public MatchCandidate(int ruleId, int matchLength, int priority, int order)
        {
            this.ruleId = ruleId;
            this.matchLength = matchLength;
            this.priority = priority;
            this.order = order;
        }

        public bool HasValue => this.ruleId >= 0;
    }
}

internal interface IFilteringTextSink
{
    void Write(ReadOnlySpan<char> buffer);

    ValueTask WriteAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken);
}

internal readonly struct RuleEntry
{
    public readonly MatchAction action;
    public readonly int priority;
    public readonly int order;
    public readonly int fixedLength;
    public readonly int maxMatchLength;
    public readonly string? replacement;
    public readonly ReplacementFactory? replacementFactory;
    public readonly ReplacementFactoryWithContext? replacementFactoryWithContext;
    public readonly MatchHandler? onMatch;

    public RuleEntry(
        MatchAction action,
        int priority,
        int order,
        int fixedLength,
        int maxMatchLength,
        string? replacement,
        ReplacementFactory? replacementFactory,
        ReplacementFactoryWithContext? replacementFactoryWithContext,
        MatchHandler? onMatch)
    {
        this.action = action;
        this.priority = priority;
        this.order = order;
        this.fixedLength = fixedLength;
        this.maxMatchLength = maxMatchLength;
        this.replacement = replacement;
        this.replacementFactory = replacementFactory;
        this.replacementFactoryWithContext = replacementFactoryWithContext;
        this.onMatch = onMatch;
    }
}

internal readonly struct RegexRuleEntry
{
    public readonly int ruleId;
    public readonly Regex regex;
    public readonly int maxMatchLength;

    public RegexRuleEntry(int ruleId, Regex regex, int maxMatchLength)
    {
        this.ruleId = ruleId;
        this.regex = regex;
        this.maxMatchLength = maxMatchLength;
    }
}

internal readonly struct CustomRuleEntry
{
    public readonly int ruleId;
    public readonly TailMatcher matcher;
    public readonly int maxMatchLength;

    public CustomRuleEntry(int ruleId, TailMatcher matcher, int maxMatchLength)
    {
        this.ruleId = ruleId;
        this.matcher = matcher;
        this.maxMatchLength = maxMatchLength;
    }
}

internal sealed class AhoCorasickAutomaton
{
    private readonly Node[] nodes;
    private readonly char[] transitionChars;
    private readonly int[] transitionNext;

    private AhoCorasickAutomaton(Node[] nodes, char[] transitionChars, int[] transitionNext)
    {
        this.nodes = nodes;
        this.transitionChars = transitionChars;
        this.transitionNext = transitionNext;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Step(int state, char c)
    {
        while (true)
        {
            if (this.tryGetTransition(state, c, out var next))
                return next;

            if (state == 0)
                return 0;

            state = this.nodes[state].failure;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetBestRuleId(int state)
        => this.nodes[state].bestRuleId;

    public static AhoCorasickAutomaton Build(List<TextFilterBuilder.LiteralPattern> patterns, RuleEntry[] rules, MatchSelection selection)
    {
        var nodes = new List<BuildNode>(Math.Max(1, patterns.Count * 3))
        {
            new()
        };

        for (var i = 0; i < patterns.Count; i++)
        {
            var p = patterns[i];
            var state = 0;

            for (var j = 0; j < p.pattern.Length; j++)
            {
                var ch = p.pattern[j];
                if (!nodes[state].next.TryGetValue(ch, out var next))
                {
                    next = nodes.Count;
                    nodes.Add(new());
                    nodes[state].next[ch] = next;
                }

                state = next;
            }

            nodes[state].outputs.Add(p.ruleId);
        }

        var q = new Queue<int>();

        foreach (var kv in nodes[0].next)
        {
            var s = kv.Value;
            nodes[s].failure = 0;
            q.Enqueue(s);
        }

        while (q.Count != 0)
        {
            var r = q.Dequeue();
            foreach (var kv in nodes[r].next)
            {
                var a = kv.Key;
                var s = kv.Value;

                q.Enqueue(s);

                var st = nodes[r].failure;
                while (st != 0 && !nodes[st].next.ContainsKey(a))
                    st = nodes[st].failure;

                if (nodes[st].next.TryGetValue(a, out var fs))
                    nodes[s].failure = fs;
                else
                    nodes[s].failure = 0;

                var failOutputs = nodes[nodes[s].failure].outputs;
                if (failOutputs.Count != 0)
                    nodes[s].outputs.AddRange(failOutputs);
            }
        }

        for (var i = 0; i < nodes.Count; i++)
        {
            var bestRuleId = -1;
            var bestLength = 0;
            var bestPriority = int.MaxValue;
            var bestOrder = int.MaxValue;

            var outs = nodes[i].outputs;
            for (var j = 0; j < outs.Count; j++)
            {
                var ruleId = outs[j];
                var rule = rules[ruleId];
                var len = rule.fixedLength;

                if (bestRuleId < 0)
                {
                    bestRuleId = ruleId;
                    bestLength = len;
                    bestPriority = rule.priority;
                    bestOrder = rule.order;

                    continue;
                }

                if (selection == MatchSelection.LongestThenPriority)
                {
                    if (len > bestLength
                        || (len == bestLength
                            && (rule.priority < bestPriority || (rule.priority == bestPriority && rule.order < bestOrder))))
                    {
                        bestRuleId = ruleId;
                        bestLength = len;
                        bestPriority = rule.priority;
                        bestOrder = rule.order;
                    }
                }
                else
                {
                    if (rule.priority < bestPriority
                        || (rule.priority == bestPriority && (len > bestLength || (len == bestLength && rule.order < bestOrder))))
                    {
                        bestRuleId = ruleId;
                        bestLength = len;
                        bestPriority = rule.priority;
                        bestOrder = rule.order;
                    }
                }
            }

            nodes[i].bestRuleId = bestRuleId;
        }

        var compiledNodes = new Node[nodes.Count];

        var totalEdges = 0;
        for (var i = 0; i < nodes.Count; i++)
            totalEdges += nodes[i].next.Count;

        var transitionChars = new char[totalEdges];
        var transitionNext = new int[totalEdges];

        var edgeCursor = 0;

        for (var i = 0; i < nodes.Count; i++)
        {
            var bn = nodes[i];
            var count = bn.next.Count;
            var start = edgeCursor;

            if (count != 0)
            {
                var tmp = new List<KeyValuePair<char, int>>(count);
                foreach (var kv in bn.next)
                    tmp.Add(kv);

                tmp.Sort(static (a, b) => a.Key.CompareTo(b.Key));

                for (var j = 0; j < tmp.Count; j++)
                {
                    transitionChars[edgeCursor] = tmp[j].Key;
                    transitionNext[edgeCursor] = tmp[j].Value;
                    edgeCursor++;
                }
            }

            compiledNodes[i] = new(bn.failure, start, count, bn.bestRuleId);
        }

        return new(compiledNodes, transitionChars, transitionNext);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool tryGetTransition(int nodeIndex, char c, out int next)
    {
        var node = this.nodes[nodeIndex];
        var start = node.transitionStart;
        var count = node.transitionCount;

        if (count == 0)
        {
            next = 0;

            return false;
        }

        if (count <= 8)
        {
            for (var i = 0; i < count; i++)
                if (this.transitionChars[start + i] == c)
                {
                    next = this.transitionNext[start + i];

                    return true;
                }

            next = 0;

            return false;
        }

        var lo = start;
        var hi = start + count - 1;

        while (lo <= hi)
        {
            var mid = (int)((uint)(lo + hi) >> 1);
            var ch = this.transitionChars[mid];

            if (ch == c)
            {
                next = this.transitionNext[mid];

                return true;
            }

            if (ch < c)
                lo = mid + 1;
            else
                hi = mid - 1;
        }

        next = 0;

        return false;
    }

    private sealed class BuildNode
    {
        public readonly Dictionary<char, int> next = new();
        public readonly List<int> outputs = [];
        public int bestRuleId = -1;
        public int failure;
    }

    private readonly struct Node
    {
        public readonly int failure;
        public readonly int transitionStart;
        public readonly int transitionCount;
        public readonly int bestRuleId;

        public Node(int failure, int transitionStart, int transitionCount, int bestRuleId)
        {
            this.failure = failure;
            this.transitionStart = transitionStart;
            this.transitionCount = transitionCount;
            this.bestRuleId = bestRuleId;
        }
    }
}
