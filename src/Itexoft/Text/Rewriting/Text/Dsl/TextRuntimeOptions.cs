// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Text.Rewriting.Primitives;

namespace Itexoft.Text.Rewriting.Text.Dsl;

/// <summary>
/// Runtime tuning options for text rewrite sessions.
/// </summary>
public sealed record TextRuntimeOptions
{
    /// <summary>
    /// When greater than zero, flushes output in blocks of the specified size for right-aligned writes.
    /// </summary>
    public int RightWriteBlockSize { get; init; } = 0;

    /// <summary>
    /// Controls how buffered tail is handled when flushing.
    /// </summary>
    public FlushBehavior FlushBehavior { get; init; } = FlushBehavior.PreserveMatchTail;

    /// <summary>
    /// Optional normalizer applied to every input character before matching.
    /// </summary>
    public Func<char, char>? InputNormalizer { get; init; }

    /// <summary>
    /// Optional filter that can mutate emitted output synchronously.
    /// </summary>
    public Func<ReadOnlySpan<char>, RewriteMetrics, string?>? OutputFilter { get; init; }

    /// <summary>
    /// Optional filter that can mutate emitted output asynchronously.
    /// </summary>
    public Func<ReadOnlyMemory<char>, RewriteMetrics, ValueTask<string?>>? OutputFilterAsync { get; init; }

    /// <summary>
    /// Optional synchronous gate that can enable or disable a rule before it runs.
    /// </summary>
    public Func<RuleInfo, RewriteMetrics, bool>? RuleGate { get; init; }

    /// <summary>
    /// Optional asynchronous gate that can enable or disable a rule before it runs.
    /// </summary>
    public Func<RuleInfo, RewriteMetrics, ValueTask<bool>>? RuleGateAsync { get; init; }

    /// <summary>
    /// Optional synchronous hook executed before applying a match; returning false skips the match.
    /// </summary>
    public Func<TextMatchContext, bool>? BeforeApply { get; init; }

    /// <summary>
    /// Optional asynchronous hook executed before applying a match; returning false skips the match.
    /// </summary>
    public Func<TextMatchContext, ValueTask<bool>>? BeforeApplyAsync { get; init; }

    /// <summary>
    /// Optional synchronous hook executed after applying a match.
    /// </summary>
    public Action<TextMatchContext>? AfterApply { get; init; }

    /// <summary>
    /// Optional asynchronous hook executed after applying a match.
    /// </summary>
    public Func<TextMatchContext, ValueTask>? AfterApplyAsync { get; init; }

    /// <summary>
    /// Optional callback for emitting metrics updates.
    /// </summary>
    public Action<RewriteMetrics>? OnMetrics { get; init; }

    /// <summary>
    /// Optional asynchronous callback for emitting metrics updates.
    /// </summary>
    public Func<RewriteMetrics, ValueTask>? OnMetricsAsync { get; init; }

    /// <summary>
    /// Optional Server-Sent Events framing options.
    /// </summary>
    public SseOptions? Sse { get; init; }

    /// <summary>
    /// Optional text framing strategy applied before output filters.
    /// </summary>
    public ITextFraming? TextFraming { get; init; }

    /// <summary>
    /// Optional set of enabled rule groups; when provided, rules with a group outside this set are skipped.
    /// </summary>
    public IReadOnlyCollection<string>? EnabledGroups { get; init; }

    /// <summary>
    /// Optional callback receiving per-rule metrics (hits and elapsed time).
    /// </summary>
    public Action<IReadOnlyList<RuleStat>>? OnRuleMetrics { get; init; }

    /// <summary>
    /// Optional async callback receiving per-rule metrics (hits and elapsed time).
    /// </summary>
    public Func<IReadOnlyList<RuleStat>, ValueTask>? OnRuleMetricsAsync { get; init; }
}
