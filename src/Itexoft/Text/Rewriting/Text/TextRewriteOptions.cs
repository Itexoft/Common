// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Buffers;
using Itexoft.Text.Rewriting.Primitives;
using Itexoft.Text.Rewriting.Text.Dsl;

namespace Itexoft.Text.Rewriting.Text;

/// <summary>
/// Runtime tuning options for text rewrite pipelines.
/// </summary>
public sealed class TextRewriteOptions
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
    public Func<ReadOnlySpan<char>, RewriteMetrics, string?>? OutputFilter { get; init; }

    /// <summary>
    /// Gets or sets an optional async output filter applied to safe spans before they are written to the sink.
    /// </summary>
    public Func<ReadOnlyMemory<char>, RewriteMetrics, ValueTask<string?>>? OutputFilterAsync { get; init; }

    /// <summary>
    /// Gets or sets a predicate that decides whether a rule (identified by compiled rule id) is enabled for the current
    /// metrics snapshot.
    /// </summary>
    public Func<int, RewriteMetrics, bool>? RuleGate { get; init; }

    /// <summary>
    /// Gets or sets an async predicate that decides whether a rule (identified by compiled rule id) is enabled for the
    /// current metrics snapshot.
    /// </summary>
    public Func<int, RewriteMetrics, ValueTask<bool>>? RuleGateAsync { get; init; }

    /// <summary>
    /// Gets or sets a callback invoked before applying a match; return false to skip mutation.
    /// </summary>
    public Func<MatchContext, bool>? BeforeApply { get; init; }

    /// <summary>
    /// Gets or sets an async callback invoked before applying a match; return false to skip mutation.
    /// </summary>
    public Func<MatchContext, ValueTask<bool>>? BeforeApplyAsync { get; init; }

    /// <summary>
    /// Gets or sets a callback invoked after a match has been applied.
    /// </summary>
    public Action<MatchContext>? AfterApply { get; init; }

    /// <summary>
    /// Gets or sets an async callback invoked after a match has been applied.
    /// </summary>
    public Func<MatchContext, ValueTask>? AfterApplyAsync { get; init; }

    /// <summary>
    /// Gets or sets a callback invoked when metrics change.
    /// </summary>
    public Action<RewriteMetrics>? OnMetrics { get; init; }

    /// <summary>
    /// Gets or sets an async callback invoked when metrics change.
    /// </summary>
    public Func<RewriteMetrics, ValueTask>? OnMetricsAsync { get; init; }

    /// <summary>
    /// Gets or sets a callback invoked with per-rule metrics when collected.
    /// </summary>
    public Action<IReadOnlyList<RuleStat>>? OnRuleMetrics { get; init; }

    /// <summary>
    /// Gets or sets an async callback invoked with per-rule metrics when collected.
    /// </summary>
    public Func<IReadOnlyList<RuleStat>, ValueTask>? OnRuleMetricsAsync { get; init; }

    /// <summary>
    /// Optional filter that enables only specified rule groups; null means all groups enabled.
    /// </summary>
    public IReadOnlyCollection<string>? EnabledGroups { get; init; }

    /// <summary>
    /// Names aligned to compiled rule ids (internal use).
    /// </summary>
    public string?[] RuleNames { get; init; } = [];

    /// <summary>
    /// Groups aligned to compiled rule ids (internal use).
    /// </summary>
    public string?[] RuleGroups { get; init; } = [];

    /// <summary>
    /// Optional delimiter that frames output as SSE events for OutputFilter callbacks.
    /// </summary>
    public string? SseDelimiter { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether the SSE delimiter should be included in the event passed to OutputFilter.
    /// </summary>
    public bool SseIncludeDelimiter { get; init; }

    /// <summary>
    /// Optional maximum allowed event size (in characters) when SSE delimiter framing is enabled.
    /// </summary>
    public int? SseMaxEventSize { get; init; }

    /// <summary>
    /// Behavior applied when an SSE event exceeds <see cref="SseMaxEventSize" />.
    /// </summary>
    public OverflowBehavior SseOverflowBehavior { get; init; } = OverflowBehavior.Error;

    /// <summary>
    /// Optional text framing applied before output filters; when set, output is chunked into frames for filters.
    /// </summary>
    public ITextFraming? TextFraming { get; init; }
}
