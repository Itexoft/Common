// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Text.Rewriting.Primitives;

namespace Itexoft.Text.Rewriting.Json.Dsl;

/// <summary>
/// Per-session options used when creating JSON rewrite sessions.
/// </summary>
public sealed record JsonKernelOptions
{
    /// <summary>
    /// Optional prefix to strip from input before parsing JSON.
    /// </summary>
    public string? UnwrapPrefix { get; init; }

    /// <summary>
    /// When true, input must start with <see cref="UnwrapPrefix" />; otherwise it is passed through <see cref="OnMalformedJson" />.
    /// </summary>
    public bool PrefixRequired { get; init; }

    /// <summary>
    /// Callback used to heal malformed JSON payloads when parsing fails.
    /// </summary>
    public Func<string, string?>? OnMalformedJson { get; init; }

    /// <summary>
    /// Framing strategy used to cut the stream into JSON documents; when null, callers must invoke <c>Commit</c> manually.
    /// </summary>
    public IJsonFraming? Framing { get; init; }

    /// <summary>
    /// Optional set of enabled rule groups.
    /// </summary>
    public IReadOnlyCollection<string>? EnabledGroups { get; init; }

    /// <summary>
    /// Optional gate that receives rule metadata and decides if the rule is enabled.
    /// </summary>
    public Func<RuleInfo, bool>? RuleGate { get; init; }

    /// <summary>
    /// Optional synchronous callback for per-rule metrics.
    /// </summary>
    public Action<IReadOnlyList<RuleStat>>? OnRuleMetrics { get; init; }

    /// <summary>
    /// Optional async callback for per-rule metrics.
    /// </summary>
    public Func<IReadOnlyList<RuleStat>, ValueTask>? OnRuleMetricsAsync { get; init; }

    /// <summary>
    /// Maximum allowed frame size when using framing; null disables the limit.
    /// </summary>
    public int? MaxFrameSize { get; init; }

    /// <summary>
    /// Behavior applied when a frame exceeds <see cref="MaxFrameSize" />.
    /// </summary>
    public OverflowBehavior FrameOverflowBehavior { get; init; } = OverflowBehavior.Error;
}
