// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Text.Rewriting.Primitives;

namespace Itexoft.Text.Rewriting.Json;

/// <summary>
/// Runtime options for JSON rewriting.
/// </summary>
public sealed class JsonRewriteOptions
{
    /// <summary>
    /// Optional prefix to strip before parsing JSON (e.g. "data: ").
    /// </summary>
    public string? UnwrapPrefix { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether the <see cref="UnwrapPrefix" /> must be present; when true and the prefix
    /// is missing, a <see cref="FormatException" /> is thrown.
    /// </summary>
    public bool PrefixRequired { get; init; }

    /// <summary>
    /// Optional repair handler invoked when parsing fails; return a fixed string to retry or null/empty to rethrow.
    /// </summary>
    public Func<string, string?>? OnMalformedJson { get; init; }

    /// <summary>
    /// Optional gate that enables or disables a rule identified by compiled rule id.
    /// </summary>
    public Func<int, bool>? RuleGate { get; init; }

    /// <summary>
    /// Optional callback receiving per-rule metrics.
    /// </summary>
    public Action<IReadOnlyList<RuleStat>>? OnRuleMetrics { get; init; }

    /// <summary>
    /// Optional async callback receiving per-rule metrics.
    /// </summary>
    public Func<IReadOnlyList<RuleStat>, ValueTask>? OnRuleMetricsAsync { get; init; }

    /// <summary>
    /// Optional set of enabled rule groups; when provided, rules with a group outside this set are skipped.
    /// </summary>
    public IReadOnlyCollection<string>? EnabledGroups { get; init; }

    /// <summary>
    /// Names aligned with rule ids (internal use).
    /// </summary>
    public string?[] RuleNames { get; init; } = [];

    /// <summary>
    /// Groups aligned with rule ids (internal use).
    /// </summary>
    public string?[] RuleGroups { get; init; } = [];

    /// <summary>
    /// Maximum allowed frame size when using framing; null disables the limit.
    /// </summary>
    public int? MaxFrameSize { get; init; }

    /// <summary>
    /// Overflow behavior applied when a frame exceeds <see cref="MaxFrameSize" />.
    /// </summary>
    public OverflowBehavior FrameOverflowBehavior { get; init; } = OverflowBehavior.Error;
}
