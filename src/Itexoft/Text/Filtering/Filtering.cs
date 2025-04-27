// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Buffers;

namespace Itexoft.Text.Filtering;

/// <summary>
/// Determines how a matched rule should be applied to the output stream.
/// </summary>
public enum MatchAction
{
    /// <summary>
    /// Keep the matched text unchanged while still triggering callbacks.
    /// </summary>
    None = 0,

    /// <summary>
    /// Remove the matched text from the output.
    /// </summary>
    Remove = 1,

    /// <summary>
    /// Replace the matched text with a provided literal or a factory-produced string.
    /// </summary>
    Replace = 2
}

/// <summary>
/// Controls how competing matches are prioritized when overlaps occur.
/// </summary>
public enum MatchSelection
{
    /// <summary>
    /// Prefer the longest match, then resolve ties by lowest priority value.
    /// </summary>
    LongestThenPriority = 0,

    /// <summary>
    /// Prefer the lowest priority value first, then the longest match.
    /// </summary>
    PriorityThenLongest = 1
}

/// <summary>
/// Determines how pending text is handled when flushing the writer.
/// </summary>
public enum FlushBehavior
{
    /// <summary>
    /// Flush only the safe prefix, keeping trailing characters that might complete a future match.
    /// </summary>
    PreserveMatchTail = 0,

    /// <summary>
    /// Commit all buffered content and reset matcher state.
    /// </summary>
    Commit = 1
}

/// <summary>
/// Callback invoked when a rule is matched.
/// </summary>
/// <param name="ruleId">Rule identifier assigned during plan creation.</param>
/// <param name="match">Span containing the matched text.</param>
public delegate void MatchHandler(int ruleId, ReadOnlySpan<char> match);

/// <summary>
/// Callback that produces a replacement string for a match.
/// </summary>
/// <param name="ruleId">Rule identifier assigned during plan creation.</param>
/// <param name="match">Span containing the matched text.</param>
/// <returns>Replacement text, or null/empty to skip replacement.</returns>
public delegate string? ReplacementFactory(int ruleId, ReadOnlySpan<char> match);

/// <summary>
/// Callback that produces a replacement string using extended context.
/// </summary>
/// <param name="ruleId">Rule identifier assigned during plan creation.</param>
/// <param name="match">Span containing the matched text.</param>
/// <param name="metrics">Current stream metrics.</param>
/// <returns>Replacement text, or null/empty to skip replacement.</returns>
public delegate string? ReplacementFactoryWithContext(int ruleId, ReadOnlySpan<char> match, FilteringMetrics metrics);

/// <summary>
/// Callback that returns the length of the tail segment considered a match.
/// </summary>
/// <param name="tail">Latest buffered characters.</param>
/// <returns>Length of the match ending at the tail; 0 for no match.</returns>
public delegate int TailMatcher(ReadOnlySpan<char> tail);

/// <summary>
/// Snapshot of streaming metrics available to callbacks.
/// </summary>
public readonly record struct FilteringMetrics(
    long ProcessedChars,
    long MatchesApplied,
    long Replacements,
    long Removals,
    long Flushes,
    int BufferedChars);

/// <summary>
/// Context passed to match interception callbacks.
/// </summary>
public readonly record struct MatchContext(int RuleId, int MatchLength, FilteringMetrics Metrics);

/// <summary>
/// Options controlling the plan compilation behavior.
/// </summary>
public sealed class TextFilterPlanOptions
{
    /// <summary>
    /// Gets or sets the strategy used to pick between overlapping matches.
    /// </summary>
    public MatchSelection MatchSelection { get; init; } = MatchSelection.LongestThenPriority;
}

/// <summary>
/// Runtime tuning options for <see cref="FilteringTextWriter" />.
/// </summary>
public sealed class FilteringTextWriterOptions
{
    /// <summary>
    /// Gets or sets the number of safe characters that triggers a synchronous write; 0 writes immediately.
    /// </summary>
    public int RightWriteBlockSize { get; init; } = 4096;

    /// <summary>
    /// Gets or sets the initial buffer capacity when the writer first allocates.
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
}

/// <summary>
/// Immutable plan compiled from a <see cref="TextFilterBuilder" />.
/// </summary>
public sealed class TextFilterPlan
{
    internal readonly CustomRuleEntry[] customRules;
    internal readonly int maxMatchLength;
    internal readonly int maxPending;
    internal readonly AhoCorasickAutomaton? ordinalAutomaton;
    internal readonly AhoCorasickAutomaton? ordinalIgnoreCaseAutomaton;
    internal readonly RegexRuleEntry[] regexRules;
    internal readonly RuleEntry[] rules;
    internal readonly MatchSelection selection;

    internal TextFilterPlan(
        RuleEntry[] rules,
        AhoCorasickAutomaton? ordinalAutomaton,
        AhoCorasickAutomaton? ordinalIgnoreCaseAutomaton,
        RegexRuleEntry[] regexRules,
        CustomRuleEntry[] customRules,
        int maxMatchLength,
        int maxPending,
        MatchSelection selection)
    {
        this.rules = rules;
        this.ordinalAutomaton = ordinalAutomaton;
        this.ordinalIgnoreCaseAutomaton = ordinalIgnoreCaseAutomaton;
        this.regexRules = regexRules;
        this.customRules = customRules;
        this.maxMatchLength = maxMatchLength;
        this.maxPending = maxPending;
        this.selection = selection;
    }

    /// <summary>
    /// Gets the total number of compiled rules.
    /// </summary>
    public int RuleCount => this.rules.Length;
}