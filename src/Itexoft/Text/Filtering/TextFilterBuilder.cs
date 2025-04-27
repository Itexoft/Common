// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Itexoft.Text.Filtering;

/// <summary>
/// Fluent builder used to compose text filtering rules and compile them into a <see cref="TextFilterPlan" />.
/// </summary>
public sealed class TextFilterBuilder
{
    private readonly List<CustomRuleEntry> customRules = [];
    private readonly List<LiteralPattern> literalPatterns = [];
    private readonly List<RegexRuleEntry> regexRules = [];
    private readonly List<RuleEntry> rules = [];

    /// <summary>
    /// Registers a literal pattern that triggers a callback without modifying the output.
    /// </summary>
    /// <param name="pattern">Text to match.</param>
    /// <param name="onMatch">Callback invoked when the pattern is found.</param>
    /// <param name="priority">Lower values win when priorities are compared.</param>
    /// <param name="comparison">Ordinal or OrdinalIgnoreCase comparison.</param>
    /// <returns>The current builder instance.</returns>
    public TextFilterBuilder HookLiteral(
        string pattern,
        MatchHandler onMatch,
        int priority = 0,
        StringComparison comparison = StringComparison.Ordinal)
        => this.addLiteral(pattern, MatchAction.None, null, null, null, priority, comparison, onMatch);

    /// <summary>
    /// Registers a literal pattern that removes the matched text.
    /// </summary>
    /// <param name="pattern">Text to match.</param>
    /// <param name="priority">Lower values win when priorities are compared.</param>
    /// <param name="comparison">Ordinal or OrdinalIgnoreCase comparison.</param>
    /// <param name="onMatch">Optional callback invoked when the pattern is found.</param>
    /// <returns>The current builder instance.</returns>
    public TextFilterBuilder RemoveLiteral(
        string pattern,
        int priority = 0,
        StringComparison comparison = StringComparison.Ordinal,
        MatchHandler? onMatch = null)
        => this.addLiteral(pattern, MatchAction.Remove, null, null, null, priority, comparison, onMatch);

    /// <summary>
    /// Registers a literal pattern that replaces the matched text with a fixed string.
    /// </summary>
    /// <param name="pattern">Text to match.</param>
    /// <param name="replacement">Replacement applied to the output stream.</param>
    /// <param name="priority">Lower values win when priorities are compared.</param>
    /// <param name="comparison">Ordinal or OrdinalIgnoreCase comparison.</param>
    /// <param name="onMatch">Optional callback invoked when the pattern is found.</param>
    /// <returns>The current builder instance.</returns>
    public TextFilterBuilder ReplaceLiteral(
        string pattern,
        string replacement,
        int priority = 0,
        StringComparison comparison = StringComparison.Ordinal,
        MatchHandler? onMatch = null)
        => this.addLiteral(pattern, MatchAction.Replace, replacement, null, null, priority, comparison, onMatch);

    /// <summary>
    /// Registers a literal pattern that replaces the matched text via a factory.
    /// </summary>
    /// <param name="pattern">Text to match.</param>
    /// <param name="replacementFactory">Delegate that produces replacement text.</param>
    /// <param name="priority">Lower values win when priorities are compared.</param>
    /// <param name="comparison">Ordinal or OrdinalIgnoreCase comparison.</param>
    /// <param name="onMatch">Optional callback invoked when the pattern is found.</param>
    /// <returns>The current builder instance.</returns>
    public TextFilterBuilder ReplaceLiteral(
        string pattern,
        ReplacementFactory replacementFactory,
        int priority = 0,
        StringComparison comparison = StringComparison.Ordinal,
        MatchHandler? onMatch = null)
        => this.addLiteral(pattern, MatchAction.Replace, null, replacementFactory, null, priority, comparison, onMatch);

    /// <summary>
    /// Registers a literal pattern that replaces the matched text via a context-aware factory.
    /// </summary>
    /// <param name="pattern">Text to match.</param>
    /// <param name="replacementFactory">Delegate that produces replacement text with metrics.</param>
    /// <param name="priority">Lower values win when priorities are compared.</param>
    /// <param name="comparison">Ordinal or OrdinalIgnoreCase comparison.</param>
    /// <param name="onMatch">Optional callback invoked when the pattern is found.</param>
    /// <returns>The current builder instance.</returns>
    public TextFilterBuilder ReplaceLiteral(
        string pattern,
        ReplacementFactoryWithContext replacementFactory,
        int priority = 0,
        StringComparison comparison = StringComparison.Ordinal,
        MatchHandler? onMatch = null)
        => this.addLiteral(pattern, MatchAction.Replace, null, null, replacementFactory, priority, comparison, onMatch);

    /// <summary>
    /// Registers a regex rule that triggers a callback without modifying the output.
    /// </summary>
    /// <param name="regex">Regular expression to match against the buffered tail.</param>
    /// <param name="maxMatchLength">Maximum expected match length (used to bound buffering).</param>
    /// <param name="onMatch">Callback invoked when the pattern is found.</param>
    /// <param name="priority">Lower values win when priorities are compared.</param>
    /// <returns>The current builder instance.</returns>
    public TextFilterBuilder HookRegex(Regex regex, int maxMatchLength, MatchHandler onMatch, int priority = 0)
        => this.addRegex(regex, maxMatchLength, MatchAction.None, null, null, null, priority, onMatch);

    /// <summary>
    /// Registers a regex rule that removes the matched text.
    /// </summary>
    /// <param name="regex">Regular expression to match against the buffered tail.</param>
    /// <param name="maxMatchLength">Maximum expected match length (used to bound buffering).</param>
    /// <param name="priority">Lower values win when priorities are compared.</param>
    /// <param name="onMatch">Optional callback invoked when the pattern is found.</param>
    /// <returns>The current builder instance.</returns>
    public TextFilterBuilder RemoveRegex(Regex regex, int maxMatchLength, int priority = 0, MatchHandler? onMatch = null)
        => this.addRegex(regex, maxMatchLength, MatchAction.Remove, null, null, null, priority, onMatch);

    /// <summary>
    /// Registers a regex rule that replaces the matched text with a fixed string.
    /// </summary>
    /// <param name="regex">Regular expression to match against the buffered tail.</param>
    /// <param name="maxMatchLength">Maximum expected match length (used to bound buffering).</param>
    /// <param name="replacement">Replacement applied to the output stream.</param>
    /// <param name="priority">Lower values win when priorities are compared.</param>
    /// <param name="onMatch">Optional callback invoked when the pattern is found.</param>
    /// <returns>The current builder instance.</returns>
    public TextFilterBuilder ReplaceRegex(
        Regex regex,
        int maxMatchLength,
        string replacement,
        int priority = 0,
        MatchHandler? onMatch = null)
        => this.addRegex(regex, maxMatchLength, MatchAction.Replace, replacement, null, null, priority, onMatch);

    /// <summary>
    /// Registers a regex rule that replaces the matched text via a factory.
    /// </summary>
    /// <param name="regex">Regular expression to match against the buffered tail.</param>
    /// <param name="maxMatchLength">Maximum expected match length (used to bound buffering).</param>
    /// <param name="replacementFactory">Delegate that produces replacement text.</param>
    /// <param name="priority">Lower values win when priorities are compared.</param>
    /// <param name="onMatch">Optional callback invoked when the pattern is found.</param>
    /// <returns>The current builder instance.</returns>
    public TextFilterBuilder ReplaceRegex(
        Regex regex,
        int maxMatchLength,
        ReplacementFactory replacementFactory,
        int priority = 0,
        MatchHandler? onMatch = null)
        => this.addRegex(regex, maxMatchLength, MatchAction.Replace, null, replacementFactory, null, priority, onMatch);

    /// <summary>
    /// Registers a regex rule that replaces the matched text via a context-aware factory.
    /// </summary>
    /// <param name="regex">Regular expression to match against the buffered tail.</param>
    /// <param name="maxMatchLength">Maximum expected match length (used to bound buffering).</param>
    /// <param name="replacementFactory">Delegate that produces replacement text with metrics.</param>
    /// <param name="priority">Lower values win when priorities are compared.</param>
    /// <param name="onMatch">Optional callback invoked when the pattern is found.</param>
    /// <returns>The current builder instance.</returns>
    public TextFilterBuilder ReplaceRegex(
        Regex regex,
        int maxMatchLength,
        ReplacementFactoryWithContext replacementFactory,
        int priority = 0,
        MatchHandler? onMatch = null)
        => this.addRegex(regex, maxMatchLength, MatchAction.Replace, null, null, replacementFactory, priority, onMatch);

    /// <summary>
    /// Registers a tail matcher that triggers a callback without modifying the output.
    /// </summary>
    /// <param name="maxMatchLength">Maximum number of tail characters to pass to the matcher.</param>
    /// <param name="matcher">Delegate that returns a match length ending at the buffer tail.</param>
    /// <param name="onMatch">Callback invoked when the matcher reports a match.</param>
    /// <param name="priority">Lower values win when priorities are compared.</param>
    /// <returns>The current builder instance.</returns>
    public TextFilterBuilder HookTailMatcher(int maxMatchLength, TailMatcher matcher, MatchHandler onMatch, int priority = 0)
        => this.addTailMatcher(maxMatchLength, matcher, MatchAction.None, null, null, null, priority, onMatch);

    /// <summary>
    /// Registers a tail matcher that removes the matched text.
    /// </summary>
    /// <param name="maxMatchLength">Maximum number of tail characters to pass to the matcher.</param>
    /// <param name="matcher">Delegate that returns a match length ending at the buffer tail.</param>
    /// <param name="priority">Lower values win when priorities are compared.</param>
    /// <param name="onMatch">Optional callback invoked when the matcher reports a match.</param>
    /// <returns>The current builder instance.</returns>
    public TextFilterBuilder RemoveTailMatcher(int maxMatchLength, TailMatcher matcher, int priority = 0, MatchHandler? onMatch = null)
        => this.addTailMatcher(maxMatchLength, matcher, MatchAction.Remove, null, null, null, priority, onMatch);

    /// <summary>
    /// Registers a tail matcher that replaces the matched text with a fixed string.
    /// </summary>
    /// <param name="maxMatchLength">Maximum number of tail characters to pass to the matcher.</param>
    /// <param name="matcher">Delegate that returns a match length ending at the buffer tail.</param>
    /// <param name="replacement">Replacement applied to the output stream.</param>
    /// <param name="priority">Lower values win when priorities are compared.</param>
    /// <param name="onMatch">Optional callback invoked when the matcher reports a match.</param>
    /// <returns>The current builder instance.</returns>
    public TextFilterBuilder ReplaceTailMatcher(
        int maxMatchLength,
        TailMatcher matcher,
        string replacement,
        int priority = 0,
        MatchHandler? onMatch = null)
        => this.addTailMatcher(maxMatchLength, matcher, MatchAction.Replace, replacement, null, null, priority, onMatch);

    /// <summary>
    /// Registers a tail matcher that replaces the matched text via a factory.
    /// </summary>
    /// <param name="maxMatchLength">Maximum number of tail characters to pass to the matcher.</param>
    /// <param name="matcher">Delegate that returns a match length ending at the buffer tail.</param>
    /// <param name="replacementFactory">Delegate that produces replacement text.</param>
    /// <param name="priority">Lower values win when priorities are compared.</param>
    /// <param name="onMatch">Optional callback invoked when the matcher reports a match.</param>
    /// <returns>The current builder instance.</returns>
    public TextFilterBuilder ReplaceTailMatcher(
        int maxMatchLength,
        TailMatcher matcher,
        ReplacementFactory replacementFactory,
        int priority = 0,
        MatchHandler? onMatch = null)
        => this.addTailMatcher(maxMatchLength, matcher, MatchAction.Replace, null, replacementFactory, null, priority, onMatch);

    /// <summary>
    /// Registers a tail matcher that replaces the matched text via a context-aware factory.
    /// </summary>
    /// <param name="maxMatchLength">Maximum number of tail characters to pass to the matcher.</param>
    /// <param name="matcher">Delegate that returns a match length ending at the buffer tail.</param>
    /// <param name="replacementFactory">Delegate that produces replacement text with metrics.</param>
    /// <param name="priority">Lower values win when priorities are compared.</param>
    /// <param name="onMatch">Optional callback invoked when the matcher reports a match.</param>
    /// <returns>The current builder instance.</returns>
    public TextFilterBuilder ReplaceTailMatcher(
        int maxMatchLength,
        TailMatcher matcher,
        ReplacementFactoryWithContext replacementFactory,
        int priority = 0,
        MatchHandler? onMatch = null)
        => this.addTailMatcher(maxMatchLength, matcher, MatchAction.Replace, null, null, replacementFactory, priority, onMatch);

    /// <summary>
    /// Compiles the configured rules into an immutable plan.
    /// </summary>
    /// <param name="options">Optional plan settings.</param>
    /// <returns>Compiled <see cref="TextFilterPlan" />.</returns>
    public TextFilterPlan Build(TextFilterPlanOptions? options = null)
    {
        options ??= new();

        var rulesArray = this.rules.ToArray();
        var selection = options.MatchSelection;

        var ordinal = new List<LiteralPattern>();
        var ordinalIgnoreCase = new List<LiteralPattern>();

        for (var i = 0; i < this.literalPatterns.Count; i++)
        {
            var p = this.literalPatterns[i];

            if (p.comparison == StringComparison.Ordinal)
                ordinal.Add(p);
            else if (p.comparison == StringComparison.OrdinalIgnoreCase)
                ordinalIgnoreCase.Add(new(foldOrdinalIgnoreCase(p.pattern), p.ruleId, p.comparison));
            else
                throw new NotSupportedException($"Only Ordinal and OrdinalIgnoreCase are supported. Got {p.comparison}.");
        }

        AhoCorasickAutomaton? ordinalAutomaton = null;
        if (ordinal.Count != 0)
            ordinalAutomaton = AhoCorasickAutomaton.Build(ordinal, rulesArray, selection);

        AhoCorasickAutomaton? ordinalIgnoreCaseAutomaton = null;
        if (ordinalIgnoreCase.Count != 0)
            ordinalIgnoreCaseAutomaton = AhoCorasickAutomaton.Build(ordinalIgnoreCase, rulesArray, selection);

        var regexRulesArray = this.regexRules.ToArray();
        var customRulesArray = this.customRules.ToArray();

        var maxMatchLength = 0;
        for (var i = 0; i < rulesArray.Length; i++)
            maxMatchLength = Math.Max(maxMatchLength, rulesArray[i].maxMatchLength);

        var maxPending = maxMatchLength > 0 ? maxMatchLength - 1 : 0;

        return new(
            rulesArray,
            ordinalAutomaton,
            ordinalIgnoreCaseAutomaton,
            regexRulesArray,
            customRulesArray,
            maxMatchLength,
            maxPending,
            selection);
    }

    private TextFilterBuilder addLiteral(
        string pattern,
        MatchAction action,
        string? replacement,
        ReplacementFactory? replacementFactory,
        ReplacementFactoryWithContext? replacementFactoryWithContext,
        int priority,
        StringComparison comparison,
        MatchHandler? onMatch)
    {
        if (pattern is null)
            throw new ArgumentNullException(nameof(pattern));
        if (pattern.Length == 0)
            throw new ArgumentException("Pattern must be non-empty.", nameof(pattern));

        if (comparison != StringComparison.Ordinal && comparison != StringComparison.OrdinalIgnoreCase)
            throw new NotSupportedException($"Only Ordinal and OrdinalIgnoreCase are supported. Got {comparison}.");

        var ruleId = this.rules.Count;
        this.rules.Add(
            new(
                action,
                priority,
                ruleId,
                pattern.Length,
                pattern.Length,
                replacement,
                replacementFactory,
                replacementFactoryWithContext,
                onMatch));

        this.literalPatterns.Add(new(pattern, ruleId, comparison));

        return this;
    }

    private TextFilterBuilder addRegex(
        Regex regex,
        int maxMatchLength,
        MatchAction action,
        string? replacement,
        ReplacementFactory? replacementFactory,
        ReplacementFactoryWithContext? replacementFactoryWithContext,
        int priority,
        MatchHandler? onMatch)
    {
        if (regex is null)
            throw new ArgumentNullException(nameof(regex));
        if (maxMatchLength <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxMatchLength), "maxMatchLength must be > 0.");

        var ruleId = this.rules.Count;
        this.rules.Add(
            new(action, priority, ruleId, 0, maxMatchLength, replacement, replacementFactory, replacementFactoryWithContext, onMatch));

        this.regexRules.Add(new(ruleId, regex, maxMatchLength));

        return this;
    }

    private TextFilterBuilder addTailMatcher(
        int maxMatchLength,
        TailMatcher matcher,
        MatchAction action,
        string? replacement,
        ReplacementFactory? replacementFactory,
        ReplacementFactoryWithContext? replacementFactoryWithContext,
        int priority,
        MatchHandler? onMatch)
    {
        if (matcher is null)
            throw new ArgumentNullException(nameof(matcher));
        if (maxMatchLength <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxMatchLength), "maxMatchLength must be > 0.");

        var ruleId = this.rules.Count;
        this.rules.Add(
            new(action, priority, ruleId, 0, maxMatchLength, replacement, replacementFactory, replacementFactoryWithContext, onMatch));

        this.customRules.Add(new(ruleId, matcher, maxMatchLength));

        return this;
    }

    private static string foldOrdinalIgnoreCase(string s)
        => string.Create(
            s.Length,
            s,
            static (dst, src) =>
            {
                for (var i = 0; i < dst.Length; i++)
                    dst[i] = foldCharOrdinalIgnoreCase(src[i]);
            });

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static char foldCharOrdinalIgnoreCase(char c)
    {
        if ((uint)(c - 'a') <= (uint)('z' - 'a'))
            return (char)(c - 32);

        return char.ToUpperInvariant(c);
    }

    internal readonly record struct LiteralPattern(string pattern, int ruleId, StringComparison comparison);
}