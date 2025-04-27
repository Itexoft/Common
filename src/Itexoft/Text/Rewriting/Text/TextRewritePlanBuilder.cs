// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Itexoft.Text.Rewriting.Primitives;
using Itexoft.Text.Rewriting.Text.Dsl;
using Itexoft.Text.Rewriting.Text.Internal.Matching;

namespace Itexoft.Text.Rewriting.Text;

/// <summary>
/// Fluent builder used to compose text rewrite rules and compile them into a <see cref="TextRewritePlan" />.
/// </summary>
public sealed class TextRewritePlanBuilder : RewritePlanBuilder<TextRewritePlanBuilder, TextRewritePlan>
{
    private readonly List<CustomRuleEntry> customRules = [];
    private readonly List<LiteralPattern> literalPatterns = [];
    private readonly List<RegexRuleEntry> regexRules = [];
    private readonly List<TextRewriteRuleEntry> rules = [];

    /// <summary>
    /// Registers a literal pattern that triggers a callback without modifying the output.
    /// </summary>
    /// <param name="pattern">Text to match.</param>
    /// <param name="onMatch">Callback invoked when the pattern is found.</param>
    /// <param name="priority">Lower values win when priorities are compared.</param>
    /// <param name="comparison">Ordinal or OrdinalIgnoreCase comparison.</param>
    /// <returns>The current builder instance.</returns>
    public TextRewritePlanBuilder HookLiteral(
        string pattern,
        MatchHandler onMatch,
        int priority = 0,
        StringComparison comparison = StringComparison.Ordinal)
    {
        var length = GetPatternLength(pattern);

        return this.AddLiteral(
            pattern,
            CreateRule(MatchAction.None, priority, length, length, null, null, null, null, null, onMatch, null),
            comparison,
            null,
            null);
    }

    /// <summary>
    /// Registers a literal pattern that triggers an async callback without modifying the output.
    /// </summary>
    /// <param name="pattern">Text to match.</param>
    /// <param name="onMatchAsync">Async callback invoked when the pattern is found.</param>
    /// <param name="priority">Lower values win when priorities are compared.</param>
    /// <param name="comparison">Ordinal or OrdinalIgnoreCase comparison.</param>
    /// <returns>The current builder instance.</returns>
    public TextRewritePlanBuilder HookLiteral(
        string pattern,
        MatchHandlerAsync onMatchAsync,
        int priority = 0,
        StringComparison comparison = StringComparison.Ordinal)
    {
        var length = GetPatternLength(pattern);

        return this.AddLiteral(
            pattern,
            CreateRule(MatchAction.None, priority, length, length, null, null, null, null, null, null, onMatchAsync),
            comparison,
            null,
            null);
    }

    /// <summary>
    /// Registers a literal pattern that removes the matched text.
    /// </summary>
    /// <param name="pattern">Text to match.</param>
    /// <param name="priority">Lower values win when priorities are compared.</param>
    /// <param name="comparison">Ordinal or OrdinalIgnoreCase comparison.</param>
    /// <param name="onMatch">Optional callback invoked when the pattern is found.</param>
    /// <returns>The current builder instance.</returns>
    public TextRewritePlanBuilder RemoveLiteral(
        string pattern,
        int priority = 0,
        StringComparison comparison = StringComparison.Ordinal,
        MatchHandler? onMatch = null)
    {
        var length = GetPatternLength(pattern);

        return this.AddLiteral(
            pattern,
            CreateRule(MatchAction.Remove, priority, length, length, null, null, null, null, null, onMatch, null),
            comparison,
            null,
            null);
    }

    /// <summary>
    /// Registers a literal pattern that removes the matched text with an async callback.
    /// </summary>
    /// <param name="pattern">Text to match.</param>
    /// <param name="priority">Lower values win when priorities are compared.</param>
    /// <param name="comparison">Ordinal or OrdinalIgnoreCase comparison.</param>
    /// <param name="onMatchAsync">Optional async callback invoked when the pattern is found.</param>
    /// <returns>The current builder instance.</returns>
    public TextRewritePlanBuilder RemoveLiteral(
        string pattern,
        MatchHandlerAsync onMatchAsync,
        int priority = 0,
        StringComparison comparison = StringComparison.Ordinal)
    {
        var length = GetPatternLength(pattern);

        return this.AddLiteral(
            pattern,
            CreateRule(MatchAction.Remove, priority, length, length, null, null, null, null, null, null, onMatchAsync),
            comparison,
            null,
            null);
    }

    /// <summary>
    /// Registers a literal pattern that replaces the matched text with a fixed string.
    /// </summary>
    /// <param name="pattern">Text to match.</param>
    /// <param name="replacement">Replacement applied to the output stream.</param>
    /// <param name="priority">Lower values win when priorities are compared.</param>
    /// <param name="comparison">Ordinal or OrdinalIgnoreCase comparison.</param>
    /// <param name="onMatch">Optional callback invoked when the pattern is found.</param>
    /// <returns>The current builder instance.</returns>
    public TextRewritePlanBuilder ReplaceLiteral(
        string pattern,
        string replacement,
        int priority = 0,
        StringComparison comparison = StringComparison.Ordinal,
        MatchHandler? onMatch = null)
    {
        var length = GetPatternLength(pattern);

        return this.AddLiteral(
            pattern,
            CreateRule(MatchAction.Replace, priority, length, length, replacement, null, null, null, null, onMatch, null),
            comparison,
            null,
            null);
    }

    /// <summary>
    /// Registers a literal pattern that replaces the matched text with a fixed string and async callback.
    /// </summary>
    /// <param name="pattern">Text to match.</param>
    /// <param name="replacement">Replacement applied to the output stream.</param>
    /// <param name="priority">Lower values win when priorities are compared.</param>
    /// <param name="comparison">Ordinal or OrdinalIgnoreCase comparison.</param>
    /// <param name="onMatchAsync">Optional async callback invoked when the pattern is found.</param>
    /// <returns>The current builder instance.</returns>
    public TextRewritePlanBuilder ReplaceLiteral(
        string pattern,
        string replacement,
        MatchHandlerAsync onMatchAsync,
        int priority = 0,
        StringComparison comparison = StringComparison.Ordinal)
    {
        var length = GetPatternLength(pattern);

        return this.AddLiteral(
            pattern,
            CreateRule(MatchAction.Replace, priority, length, length, replacement, null, null, null, null, null, onMatchAsync),
            comparison,
            null,
            null);
    }

    /// <summary>
    /// Registers a literal pattern that replaces the matched text via a factory.
    /// </summary>
    /// <param name="pattern">Text to match.</param>
    /// <param name="replacementFactory">Delegate that produces replacement text.</param>
    /// <param name="priority">Lower values win when priorities are compared.</param>
    /// <param name="comparison">Ordinal or OrdinalIgnoreCase comparison.</param>
    /// <param name="onMatch">Optional callback invoked when the pattern is found.</param>
    /// <returns>The current builder instance.</returns>
    public TextRewritePlanBuilder ReplaceLiteral(
        string pattern,
        ReplacementFactory replacementFactory,
        int priority = 0,
        StringComparison comparison = StringComparison.Ordinal,
        MatchHandler? onMatch = null)
    {
        var length = GetPatternLength(pattern);

        return this.AddLiteral(
            pattern,
            CreateRule(MatchAction.Replace, priority, length, length, null, replacementFactory, null, null, null, onMatch, null),
            comparison,
            null,
            null);
    }

    /// <summary>
    /// Registers a literal pattern that replaces the matched text via a factory with an async callback.
    /// </summary>
    /// <param name="pattern">Text to match.</param>
    /// <param name="replacementFactory">Delegate that produces replacement text.</param>
    /// <param name="priority">Lower values win when priorities are compared.</param>
    /// <param name="comparison">Ordinal or OrdinalIgnoreCase comparison.</param>
    /// <param name="onMatchAsync">Optional async callback invoked when the pattern is found.</param>
    /// <returns>The current builder instance.</returns>
    public TextRewritePlanBuilder ReplaceLiteral(
        string pattern,
        ReplacementFactory replacementFactory,
        MatchHandlerAsync onMatchAsync,
        int priority = 0,
        StringComparison comparison = StringComparison.Ordinal)
    {
        var length = GetPatternLength(pattern);

        return this.AddLiteral(
            pattern,
            CreateRule(MatchAction.Replace, priority, length, length, null, replacementFactory, null, null, null, null, onMatchAsync),
            comparison,
            null,
            null);
    }

    /// <summary>
    /// Registers a literal pattern that replaces the matched text via a context-aware factory.
    /// </summary>
    /// <param name="pattern">Text to match.</param>
    /// <param name="replacementFactory">Delegate that produces replacement text with metrics.</param>
    /// <param name="priority">Lower values win when priorities are compared.</param>
    /// <param name="comparison">Ordinal or OrdinalIgnoreCase comparison.</param>
    /// <param name="onMatch">Optional callback invoked when the pattern is found.</param>
    /// <returns>The current builder instance.</returns>
    public TextRewritePlanBuilder ReplaceLiteral(
        string pattern,
        ReplacementFactoryWithContext replacementFactory,
        int priority = 0,
        StringComparison comparison = StringComparison.Ordinal,
        MatchHandler? onMatch = null)
    {
        var length = GetPatternLength(pattern);

        return this.AddLiteral(
            pattern,
            CreateRule(MatchAction.Replace, priority, length, length, null, null, replacementFactory, null, null, onMatch, null),
            comparison,
            null,
            null);
    }

    /// <summary>
    /// Registers a literal pattern that replaces the matched text via a context-aware factory with an async callback.
    /// </summary>
    /// <param name="pattern">Text to match.</param>
    /// <param name="replacementFactory">Delegate that produces replacement text with metrics.</param>
    /// <param name="priority">Lower values win when priorities are compared.</param>
    /// <param name="comparison">Ordinal or OrdinalIgnoreCase comparison.</param>
    /// <param name="onMatchAsync">Optional async callback invoked when the pattern is found.</param>
    /// <returns>The current builder instance.</returns>
    public TextRewritePlanBuilder ReplaceLiteral(
        string pattern,
        ReplacementFactoryWithContext replacementFactory,
        MatchHandlerAsync onMatchAsync,
        int priority = 0,
        StringComparison comparison = StringComparison.Ordinal)
    {
        var length = GetPatternLength(pattern);

        return this.AddLiteral(
            pattern,
            CreateRule(MatchAction.Replace, priority, length, length, null, null, replacementFactory, null, null, null, onMatchAsync),
            comparison,
            null,
            null);
    }

    /// <summary>
    /// Registers a literal pattern that replaces the matched text via an async factory.
    /// </summary>
    /// <param name="pattern">Text to match.</param>
    /// <param name="replacementFactory">Delegate that produces replacement text asynchronously.</param>
    /// <param name="priority">Lower values win when priorities are compared.</param>
    /// <param name="comparison">Ordinal or OrdinalIgnoreCase comparison.</param>
    /// <param name="onMatch">Optional callback invoked when the pattern is found.</param>
    /// <returns>The current builder instance.</returns>
    public TextRewritePlanBuilder ReplaceLiteral(
        string pattern,
        ReplacementFactoryAsync replacementFactory,
        int priority = 0,
        StringComparison comparison = StringComparison.Ordinal,
        MatchHandler? onMatch = null)
    {
        var length = GetPatternLength(pattern);

        return this.AddLiteral(
            pattern,
            CreateRule(MatchAction.Replace, priority, length, length, null, null, null, replacementFactory, null, onMatch, null),
            comparison,
            null,
            null);
    }

    /// <summary>
    /// Registers a literal pattern that replaces the matched text via an async factory and async callback.
    /// </summary>
    /// <param name="pattern">Text to match.</param>
    /// <param name="replacementFactory">Delegate that produces replacement text asynchronously.</param>
    /// <param name="priority">Lower values win when priorities are compared.</param>
    /// <param name="comparison">Ordinal or OrdinalIgnoreCase comparison.</param>
    /// <param name="onMatchAsync">Optional async callback invoked when the pattern is found.</param>
    /// <returns>The current builder instance.</returns>
    public TextRewritePlanBuilder ReplaceLiteral(
        string pattern,
        ReplacementFactoryAsync replacementFactory,
        MatchHandlerAsync onMatchAsync,
        int priority = 0,
        StringComparison comparison = StringComparison.Ordinal)
    {
        var length = GetPatternLength(pattern);

        return this.AddLiteral(
            pattern,
            CreateRule(MatchAction.Replace, priority, length, length, null, null, null, replacementFactory, null, null, onMatchAsync),
            comparison,
            null,
            null);
    }

    /// <summary>
    /// Registers a literal pattern that replaces the matched text via a context-aware async factory.
    /// </summary>
    /// <param name="pattern">Text to match.</param>
    /// <param name="replacementFactory">Delegate that produces replacement text asynchronously with metrics.</param>
    /// <param name="priority">Lower values win when priorities are compared.</param>
    /// <param name="comparison">Ordinal or OrdinalIgnoreCase comparison.</param>
    /// <param name="onMatch">Optional callback invoked when the pattern is found.</param>
    /// <returns>The current builder instance.</returns>
    public TextRewritePlanBuilder ReplaceLiteral(
        string pattern,
        ReplacementFactoryWithContextAsync replacementFactory,
        int priority = 0,
        StringComparison comparison = StringComparison.Ordinal,
        MatchHandler? onMatch = null)
    {
        var length = GetPatternLength(pattern);

        return this.AddLiteral(
            pattern,
            CreateRule(MatchAction.Replace, priority, length, length, null, null, null, null, replacementFactory, onMatch, null),
            comparison,
            null,
            null);
    }

    /// <summary>
    /// Registers a literal pattern that replaces the matched text via a context-aware async factory and async callback.
    /// </summary>
    /// <param name="pattern">Text to match.</param>
    /// <param name="replacementFactory">Delegate that produces replacement text asynchronously with metrics.</param>
    /// <param name="priority">Lower values win when priorities are compared.</param>
    /// <param name="comparison">Ordinal or OrdinalIgnoreCase comparison.</param>
    /// <param name="onMatchAsync">Optional async callback invoked when the pattern is found.</param>
    /// <returns>The current builder instance.</returns>
    public TextRewritePlanBuilder ReplaceLiteral(
        string pattern,
        ReplacementFactoryWithContextAsync replacementFactory,
        MatchHandlerAsync onMatchAsync,
        int priority = 0,
        StringComparison comparison = StringComparison.Ordinal)
    {
        var length = GetPatternLength(pattern);

        return this.AddLiteral(
            pattern,
            CreateRule(MatchAction.Replace, priority, length, length, null, null, null, null, replacementFactory, null, onMatchAsync),
            comparison,
            null,
            null);
    }

    /// <summary>
    /// Registers a literal rule using a custom <see cref="TextRewriteRuleEntry" /> implementation.
    /// </summary>
    public TextRewritePlanBuilder AddLiteralRule(
        string pattern,
        TextRewriteRuleEntry textRewriteRule,
        StringComparison comparison = StringComparison.Ordinal,
        MatchHandler? onMatch = null,
        MatchHandlerAsync? onMatchAsync = null)
        => this.AddLiteral(pattern, textRewriteRule, comparison, onMatch, onMatchAsync);

    /// <summary>
    /// Registers a regex rule that triggers a callback without modifying the output.
    /// </summary>
    /// <param name="pattern">Regular expression pattern to match against the buffered tail.</param>
    /// <param name="maxMatchLength">Maximum expected match length (used to bound buffering).</param>
    /// <param name="onMatch">Callback invoked when the pattern is found.</param>
    /// <param name="priority">Lower values win when priorities are compared.</param>
    /// <returns>The current builder instance.</returns>
    public TextRewritePlanBuilder HookRegex(string pattern, int maxMatchLength, MatchHandler onMatch, int priority = 0)
        => this.HookRegex(BuildRegex(pattern), maxMatchLength, onMatch, priority);

    /// <summary>
    /// Registers a regex rule that triggers a callback without modifying the output.
    /// </summary>
    /// <param name="regex">Regular expression to match against the buffered tail.</param>
    /// <param name="maxMatchLength">Maximum expected match length (used to bound buffering).</param>
    /// <param name="onMatch">Callback invoked when the pattern is found.</param>
    /// <param name="priority">Lower values win when priorities are compared.</param>
    /// <returns>The current builder instance.</returns>
    public TextRewritePlanBuilder HookRegex(Regex regex, int maxMatchLength, MatchHandler onMatch, int priority = 0)
        => this.AddRegex(
            regex,
            maxMatchLength,
            CreateRule(MatchAction.None, priority, 0, maxMatchLength, null, null, null, null, null, onMatch, null),
            null,
            null);

    /// <summary>
    /// Registers a regex rule that triggers an async callback without modifying the output.
    /// </summary>
    /// <param name="pattern">Regular expression pattern to match against the buffered tail.</param>
    /// <param name="maxMatchLength">Maximum expected match length (used to bound buffering).</param>
    /// <param name="onMatchAsync">Callback invoked when the pattern is found.</param>
    /// <param name="priority">Lower values win when priorities are compared.</param>
    /// <returns>The current builder instance.</returns>
    public TextRewritePlanBuilder HookRegex(string pattern, int maxMatchLength, MatchHandlerAsync onMatchAsync, int priority = 0)
        => this.HookRegex(BuildRegex(pattern), maxMatchLength, onMatchAsync, priority);

    /// <summary>
    /// Registers a regex rule that triggers an async callback without modifying the output.
    /// </summary>
    public TextRewritePlanBuilder HookRegex(Regex regex, int maxMatchLength, MatchHandlerAsync onMatchAsync, int priority = 0)
        => this.AddRegex(
            regex,
            maxMatchLength,
            CreateRule(MatchAction.None, priority, 0, maxMatchLength, null, null, null, null, null, null, onMatchAsync),
            null,
            null);

    /// <summary>
    /// Registers a regex rule that removes the matched text.
    /// </summary>
    /// <param name="pattern">Regular expression pattern to match against the buffered tail.</param>
    /// <param name="maxMatchLength">Maximum expected match length (used to bound buffering).</param>
    /// <param name="priority">Lower values win when priorities are compared.</param>
    /// <param name="onMatch">Optional callback invoked when the pattern is found.</param>
    /// <returns>The current builder instance.</returns>
    public TextRewritePlanBuilder RemoveRegex(
        string pattern,
        int maxMatchLength,
        int priority = 0,
        MatchHandler? onMatch = null)
        => this.RemoveRegex(BuildRegex(pattern), maxMatchLength, priority, onMatch);

    /// <summary>
    /// Registers a regex rule that removes the matched text.
    /// </summary>
    /// <param name="regex">Regular expression to match against the buffered tail.</param>
    /// <param name="maxMatchLength">Maximum expected match length (used to bound buffering).</param>
    /// <param name="priority">Lower values win when priorities are compared.</param>
    /// <param name="onMatch">Optional callback invoked when the pattern is found.</param>
    /// <returns>The current builder instance.</returns>
    public TextRewritePlanBuilder RemoveRegex(
        Regex regex,
        int maxMatchLength,
        int priority = 0,
        MatchHandler? onMatch = null)
        => this.AddRegex(
            regex,
            maxMatchLength,
            CreateRule(MatchAction.Remove, priority, 0, maxMatchLength, null, null, null, null, null, onMatch, null),
            null,
            null);

    public TextRewritePlanBuilder RemoveRegex(
        string pattern,
        int maxMatchLength,
        MatchHandlerAsync onMatchAsync,
        int priority = 0)
        => this.RemoveRegex(BuildRegex(pattern), maxMatchLength, onMatchAsync, priority);

    public TextRewritePlanBuilder RemoveRegex(
        Regex regex,
        int maxMatchLength,
        MatchHandlerAsync onMatchAsync,
        int priority = 0)
        => this.AddRegex(
            regex,
            maxMatchLength,
            CreateRule(MatchAction.Remove, priority, 0, maxMatchLength, null, null, null, null, null, null, onMatchAsync),
            null,
            null);

    /// <summary>
    /// Registers a regex rule that replaces the matched text with a fixed string.
    /// </summary>
    /// <param name="pattern">Regular expression pattern to match against the buffered tail.</param>
    /// <param name="maxMatchLength">Maximum expected match length (used to bound buffering).</param>
    /// <param name="replacement">Replacement applied to the output stream.</param>
    /// <param name="priority">Lower values win when priorities are compared.</param>
    /// <param name="onMatch">Optional callback invoked when the pattern is found.</param>
    /// <returns>The current builder instance.</returns>
    public TextRewritePlanBuilder ReplaceRegex(
        string pattern,
        int maxMatchLength,
        string replacement,
        int priority = 0,
        MatchHandler? onMatch = null)
        => this.ReplaceRegex(BuildRegex(pattern), maxMatchLength, replacement, priority, onMatch);

    /// <summary>
    /// Registers a regex rule that replaces the matched text with a fixed string.
    /// </summary>
    /// <param name="regex">Regular expression to match against the buffered tail.</param>
    /// <param name="maxMatchLength">Maximum expected match length (used to bound buffering).</param>
    /// <param name="replacement">Replacement applied to the output stream.</param>
    /// <param name="priority">Lower values win when priorities are compared.</param>
    /// <param name="onMatch">Optional callback invoked when the pattern is found.</param>
    /// <returns>The current builder instance.</returns>
    public TextRewritePlanBuilder ReplaceRegex(
        Regex regex,
        int maxMatchLength,
        string replacement,
        int priority = 0,
        MatchHandler? onMatch = null)
        => this.AddRegex(
            regex,
            maxMatchLength,
            CreateRule(MatchAction.Replace, priority, 0, maxMatchLength, replacement, null, null, null, null, onMatch, null),
            null,
            null);

    public TextRewritePlanBuilder ReplaceRegex(
        Regex regex,
        int maxMatchLength,
        string replacement,
        MatchHandlerAsync onMatchAsync,
        int priority = 0)
        => this.AddRegex(
            regex,
            maxMatchLength,
            CreateRule(MatchAction.Replace, priority, 0, maxMatchLength, replacement, null, null, null, null, null, onMatchAsync),
            null,
            null);

    public TextRewritePlanBuilder ReplaceRegex(
        string pattern,
        int maxMatchLength,
        string replacement,
        MatchHandlerAsync onMatchAsync,
        int priority = 0)
        => this.ReplaceRegex(BuildRegex(pattern), maxMatchLength, replacement, onMatchAsync, priority);

    /// <summary>
    /// Registers a regex rule that replaces the matched text via a factory.
    /// </summary>
    /// <param name="regex">Regular expression to match against the buffered tail.</param>
    /// <param name="maxMatchLength">Maximum expected match length (used to bound buffering).</param>
    /// <param name="replacementFactory">Delegate that produces replacement text.</param>
    /// <param name="priority">Lower values win when priorities are compared.</param>
    /// <param name="onMatch">Optional callback invoked when the pattern is found.</param>
    /// <returns>The current builder instance.</returns>
    public TextRewritePlanBuilder ReplaceRegex(
        Regex regex,
        int maxMatchLength,
        ReplacementFactory replacementFactory,
        int priority = 0,
        MatchHandler? onMatch = null)
        => this.AddRegex(
            regex,
            maxMatchLength,
            CreateRule(MatchAction.Replace, priority, 0, maxMatchLength, null, replacementFactory, null, null, null, onMatch, null),
            null,
            null);

    public TextRewritePlanBuilder ReplaceRegex(
        string pattern,
        int maxMatchLength,
        ReplacementFactory replacementFactory,
        int priority = 0,
        MatchHandler? onMatch = null)
        => this.ReplaceRegex(BuildRegex(pattern), maxMatchLength, replacementFactory, priority, onMatch);

    public TextRewritePlanBuilder ReplaceRegex(
        Regex regex,
        int maxMatchLength,
        ReplacementFactory replacementFactory,
        MatchHandlerAsync onMatchAsync,
        int priority = 0)
        => this.AddRegex(
            regex,
            maxMatchLength,
            CreateRule(MatchAction.Replace, priority, 0, maxMatchLength, null, replacementFactory, null, null, null, null, onMatchAsync),
            null,
            null);

    public TextRewritePlanBuilder ReplaceRegex(
        string pattern,
        int maxMatchLength,
        ReplacementFactory replacementFactory,
        MatchHandlerAsync onMatchAsync,
        int priority = 0)
        => this.ReplaceRegex(BuildRegex(pattern), maxMatchLength, replacementFactory, onMatchAsync, priority);

    /// <summary>
    /// Registers a regex rule that replaces the matched text via a context-aware factory.
    /// </summary>
    /// <param name="regex">Regular expression to match against the buffered tail.</param>
    /// <param name="maxMatchLength">Maximum expected match length (used to bound buffering).</param>
    /// <param name="replacementFactory">Delegate that produces replacement text with metrics.</param>
    /// <param name="priority">Lower values win when priorities are compared.</param>
    /// <param name="onMatch">Optional callback invoked when the pattern is found.</param>
    /// <returns>The current builder instance.</returns>
    public TextRewritePlanBuilder ReplaceRegex(
        Regex regex,
        int maxMatchLength,
        ReplacementFactoryWithContext replacementFactory,
        int priority = 0,
        MatchHandler? onMatch = null)
        => this.AddRegex(
            regex,
            maxMatchLength,
            CreateRule(MatchAction.Replace, priority, 0, maxMatchLength, null, null, replacementFactory, null, null, onMatch, null),
            null,
            null);

    public TextRewritePlanBuilder ReplaceRegex(
        string pattern,
        int maxMatchLength,
        ReplacementFactoryWithContext replacementFactory,
        int priority = 0,
        MatchHandler? onMatch = null)
        => this.ReplaceRegex(BuildRegex(pattern), maxMatchLength, replacementFactory, priority, onMatch);

    public TextRewritePlanBuilder ReplaceRegex(
        Regex regex,
        int maxMatchLength,
        ReplacementFactoryWithContext replacementFactory,
        MatchHandlerAsync onMatchAsync,
        int priority = 0)
        => this.AddRegex(
            regex,
            maxMatchLength,
            CreateRule(MatchAction.Replace, priority, 0, maxMatchLength, null, null, replacementFactory, null, null, null, onMatchAsync),
            null,
            null);

    public TextRewritePlanBuilder ReplaceRegex(
        string pattern,
        int maxMatchLength,
        ReplacementFactoryWithContext replacementFactory,
        MatchHandlerAsync onMatchAsync,
        int priority = 0)
        => this.ReplaceRegex(BuildRegex(pattern), maxMatchLength, replacementFactory, onMatchAsync, priority);

    /// <summary>
    /// Registers a regex rule that replaces the matched text via an async factory.
    /// </summary>
    /// <param name="regex">Regular expression to match against the buffered tail.</param>
    /// <param name="maxMatchLength">Maximum expected match length (used to bound buffering).</param>
    /// <param name="replacementFactory">Delegate that produces replacement text asynchronously.</param>
    /// <param name="priority">Lower values win when priorities are compared.</param>
    /// <param name="onMatch">Optional callback invoked when the pattern is found.</param>
    /// <returns>The current builder instance.</returns>
    public TextRewritePlanBuilder ReplaceRegex(
        Regex regex,
        int maxMatchLength,
        ReplacementFactoryAsync replacementFactory,
        int priority = 0,
        MatchHandler? onMatch = null)
        => this.AddRegex(
            regex,
            maxMatchLength,
            CreateRule(MatchAction.Replace, priority, 0, maxMatchLength, null, null, null, replacementFactory, null, onMatch, null),
            null,
            null);

    public TextRewritePlanBuilder ReplaceRegex(
        string pattern,
        int maxMatchLength,
        ReplacementFactoryAsync replacementFactory,
        int priority = 0,
        MatchHandler? onMatch = null)
        => this.ReplaceRegex(BuildRegex(pattern), maxMatchLength, replacementFactory, priority, onMatch);

    public TextRewritePlanBuilder ReplaceRegex(
        Regex regex,
        int maxMatchLength,
        ReplacementFactoryAsync replacementFactory,
        MatchHandlerAsync onMatchAsync,
        int priority = 0)
        => this.AddRegex(
            regex,
            maxMatchLength,
            CreateRule(MatchAction.Replace, priority, 0, maxMatchLength, null, null, null, replacementFactory, null, null, onMatchAsync),
            null,
            null);

    public TextRewritePlanBuilder ReplaceRegex(
        string pattern,
        int maxMatchLength,
        ReplacementFactoryAsync replacementFactory,
        MatchHandlerAsync onMatchAsync,
        int priority = 0)
        => this.ReplaceRegex(BuildRegex(pattern), maxMatchLength, replacementFactory, onMatchAsync, priority);

    /// <summary>
    /// Registers a regex rule that replaces the matched text via a context-aware async factory.
    /// </summary>
    /// <param name="regex">Regular expression to match against the buffered tail.</param>
    /// <param name="maxMatchLength">Maximum expected match length (used to bound buffering).</param>
    /// <param name="replacementFactory">Delegate that produces replacement text asynchronously with metrics.</param>
    /// <param name="priority">Lower values win when priorities are compared.</param>
    /// <param name="onMatch">Optional callback invoked when the pattern is found.</param>
    /// <returns>The current builder instance.</returns>
    public TextRewritePlanBuilder ReplaceRegex(
        Regex regex,
        int maxMatchLength,
        ReplacementFactoryWithContextAsync replacementFactory,
        int priority = 0,
        MatchHandler? onMatch = null)
        => this.AddRegex(
            regex,
            maxMatchLength,
            CreateRule(MatchAction.Replace, priority, 0, maxMatchLength, null, null, null, null, replacementFactory, onMatch, null),
            null,
            null);

    public TextRewritePlanBuilder ReplaceRegex(
        string pattern,
        int maxMatchLength,
        ReplacementFactoryWithContextAsync replacementFactory,
        int priority = 0,
        MatchHandler? onMatch = null)
        => this.ReplaceRegex(BuildRegex(pattern), maxMatchLength, replacementFactory, priority, onMatch);

    public TextRewritePlanBuilder ReplaceRegex(
        Regex regex,
        int maxMatchLength,
        ReplacementFactoryWithContextAsync replacementFactory,
        MatchHandlerAsync onMatchAsync,
        int priority = 0)
        => this.AddRegex(
            regex,
            maxMatchLength,
            CreateRule(MatchAction.Replace, priority, 0, maxMatchLength, null, null, null, null, replacementFactory, null, onMatchAsync),
            null,
            null);

    public TextRewritePlanBuilder ReplaceRegex(
        string pattern,
        int maxMatchLength,
        ReplacementFactoryWithContextAsync replacementFactory,
        MatchHandlerAsync onMatchAsync,
        int priority = 0)
        => this.ReplaceRegex(BuildRegex(pattern), maxMatchLength, replacementFactory, onMatchAsync, priority);

    /// <summary>
    /// Registers a regex rule using a custom <see cref="TextRewriteRuleEntry" /> implementation.
    /// </summary>
    public TextRewritePlanBuilder AddRegexRule(
        string pattern,
        int maxMatchLength,
        TextRewriteRuleEntry textRewriteRule,
        MatchHandler? onMatch = null,
        MatchHandlerAsync? onMatchAsync = null)
        => this.AddRegexRule(BuildRegex(pattern), maxMatchLength, textRewriteRule, onMatch, onMatchAsync);

    public TextRewritePlanBuilder AddRegexRule(
        Regex regex,
        int maxMatchLength,
        TextRewriteRuleEntry textRewriteRule,
        MatchHandler? onMatch = null,
        MatchHandlerAsync? onMatchAsync = null)
        => this.AddRegex(regex, maxMatchLength, textRewriteRule, onMatch, onMatchAsync);

    /// <summary>
    /// Registers a tail matcher that triggers a callback without modifying the output.
    /// </summary>
    /// <param name="maxMatchLength">Maximum number of tail characters to pass to the matcher.</param>
    /// <param name="matcher">Delegate that returns a match length ending at the buffer tail.</param>
    /// <param name="onMatch">Callback invoked when the matcher reports a match.</param>
    /// <param name="priority">Lower values win when priorities are compared.</param>
    /// <returns>The current builder instance.</returns>
    public TextRewritePlanBuilder HookTailMatcher(
        int maxMatchLength,
        TailMatcher matcher,
        MatchHandler onMatch,
        int priority = 0)
        => this.AddTailMatcher(
            maxMatchLength,
            matcher,
            CreateRule(MatchAction.None, priority, 0, maxMatchLength, null, null, null, null, null, onMatch, null),
            null,
            null);

    public TextRewritePlanBuilder HookTailMatcher(
        int maxMatchLength,
        TailMatcher matcher,
        MatchHandlerAsync onMatchAsync,
        int priority = 0)
        => this.AddTailMatcher(
            maxMatchLength,
            matcher,
            CreateRule(MatchAction.None, priority, 0, maxMatchLength, null, null, null, null, null, null, onMatchAsync),
            null,
            null);

    /// <summary>
    /// Registers a tail matcher that removes the matched text.
    /// </summary>
    /// <param name="maxMatchLength">Maximum number of tail characters to pass to the matcher.</param>
    /// <param name="matcher">Delegate that returns a match length ending at the buffer tail.</param>
    /// <param name="priority">Lower values win when priorities are compared.</param>
    /// <param name="onMatch">Optional callback invoked when the matcher reports a match.</param>
    /// <returns>The current builder instance.</returns>
    public TextRewritePlanBuilder RemoveTailMatcher(
        int maxMatchLength,
        TailMatcher matcher,
        int priority = 0,
        MatchHandler? onMatch = null)
        => this.AddTailMatcher(
            maxMatchLength,
            matcher,
            CreateRule(MatchAction.Remove, priority, 0, maxMatchLength, null, null, null, null, null, onMatch, null),
            null,
            null);

    public TextRewritePlanBuilder RemoveTailMatcher(
        int maxMatchLength,
        TailMatcher matcher,
        MatchHandlerAsync onMatchAsync,
        int priority = 0)
        => this.AddTailMatcher(
            maxMatchLength,
            matcher,
            CreateRule(MatchAction.Remove, priority, 0, maxMatchLength, null, null, null, null, null, null, onMatchAsync),
            null,
            null);

    /// <summary>
    /// Registers a tail matcher that replaces the matched text with a fixed string.
    /// </summary>
    /// <param name="maxMatchLength">Maximum number of tail characters to pass to the matcher.</param>
    /// <param name="matcher">Delegate that returns a match length ending at the buffer tail.</param>
    /// <param name="replacement">Replacement applied to the output stream.</param>
    /// <param name="priority">Lower values win when priorities are compared.</param>
    /// <param name="onMatch">Optional callback invoked when the matcher reports a match.</param>
    /// <returns>The current builder instance.</returns>
    public TextRewritePlanBuilder ReplaceTailMatcher(
        int maxMatchLength,
        TailMatcher matcher,
        string replacement,
        int priority = 0,
        MatchHandler? onMatch = null)
        => this.AddTailMatcher(
            maxMatchLength,
            matcher,
            CreateRule(MatchAction.Replace, priority, 0, maxMatchLength, replacement, null, null, null, null, onMatch, null),
            null,
            null);

    public TextRewritePlanBuilder ReplaceTailMatcher(
        int maxMatchLength,
        TailMatcher matcher,
        string replacement,
        MatchHandlerAsync onMatchAsync,
        int priority = 0)
        => this.AddTailMatcher(
            maxMatchLength,
            matcher,
            CreateRule(MatchAction.Replace, priority, 0, maxMatchLength, replacement, null, null, null, null, null, onMatchAsync),
            null,
            null);

    /// <summary>
    /// Registers a tail matcher that replaces the matched text via a factory.
    /// </summary>
    /// <param name="maxMatchLength">Maximum number of tail characters to pass to the matcher.</param>
    /// <param name="matcher">Delegate that returns a match length ending at the buffer tail.</param>
    /// <param name="replacementFactory">Delegate that produces replacement text.</param>
    /// <param name="priority">Lower values win when priorities are compared.</param>
    /// <param name="onMatch">Optional callback invoked when the matcher reports a match.</param>
    /// <returns>The current builder instance.</returns>
    public TextRewritePlanBuilder ReplaceTailMatcher(
        int maxMatchLength,
        TailMatcher matcher,
        ReplacementFactory replacementFactory,
        int priority = 0,
        MatchHandler? onMatch = null)
        => this.AddTailMatcher(
            maxMatchLength,
            matcher,
            CreateRule(MatchAction.Replace, priority, 0, maxMatchLength, null, replacementFactory, null, null, null, onMatch, null),
            null,
            null);

    public TextRewritePlanBuilder ReplaceTailMatcher(
        int maxMatchLength,
        TailMatcher matcher,
        ReplacementFactory replacementFactory,
        MatchHandlerAsync onMatchAsync,
        int priority = 0)
        => this.AddTailMatcher(
            maxMatchLength,
            matcher,
            CreateRule(MatchAction.Replace, priority, 0, maxMatchLength, null, replacementFactory, null, null, null, null, onMatchAsync),
            null,
            null);

    /// <summary>
    /// Registers a tail matcher that replaces the matched text via a context-aware factory.
    /// </summary>
    /// <param name="maxMatchLength">Maximum number of tail characters to pass to the matcher.</param>
    /// <param name="matcher">Delegate that returns a match length ending at the buffer tail.</param>
    /// <param name="replacementFactory">Delegate that produces replacement text with metrics.</param>
    /// <param name="priority">Lower values win when priorities are compared.</param>
    /// <param name="onMatch">Optional callback invoked when the matcher reports a match.</param>
    /// <returns>The current builder instance.</returns>
    public TextRewritePlanBuilder ReplaceTailMatcher(
        int maxMatchLength,
        TailMatcher matcher,
        ReplacementFactoryWithContext replacementFactory,
        int priority = 0,
        MatchHandler? onMatch = null)
        => this.AddTailMatcher(
            maxMatchLength,
            matcher,
            CreateRule(MatchAction.Replace, priority, 0, maxMatchLength, null, null, replacementFactory, null, null, onMatch, null),
            null,
            null);

    public TextRewritePlanBuilder ReplaceTailMatcher(
        int maxMatchLength,
        TailMatcher matcher,
        ReplacementFactoryWithContext replacementFactory,
        MatchHandlerAsync onMatchAsync,
        int priority = 0)
        => this.AddTailMatcher(
            maxMatchLength,
            matcher,
            CreateRule(MatchAction.Replace, priority, 0, maxMatchLength, null, null, replacementFactory, null, null, null, onMatchAsync),
            null,
            null);

    /// <summary>
    /// Registers a tail matcher that replaces the matched text via an async factory.
    /// </summary>
    /// <param name="maxMatchLength">Maximum number of tail characters to pass to the matcher.</param>
    /// <param name="matcher">Delegate that returns a match length ending at the buffer tail.</param>
    /// <param name="replacementFactory">Delegate that produces replacement text asynchronously.</param>
    /// <param name="priority">Lower values win when priorities are compared.</param>
    /// <param name="onMatch">Optional callback invoked when the pattern is found.</param>
    /// <returns>The current builder instance.</returns>
    public TextRewritePlanBuilder ReplaceTailMatcher(
        int maxMatchLength,
        TailMatcher matcher,
        ReplacementFactoryAsync replacementFactory,
        int priority = 0,
        MatchHandler? onMatch = null)
        => this.AddTailMatcher(
            maxMatchLength,
            matcher,
            CreateRule(MatchAction.Replace, priority, 0, maxMatchLength, null, null, null, replacementFactory, null, onMatch, null),
            null,
            null);

    public TextRewritePlanBuilder ReplaceTailMatcher(
        int maxMatchLength,
        TailMatcher matcher,
        ReplacementFactoryAsync replacementFactory,
        MatchHandlerAsync onMatchAsync,
        int priority = 0)
        => this.AddTailMatcher(
            maxMatchLength,
            matcher,
            CreateRule(MatchAction.Replace, priority, 0, maxMatchLength, null, null, null, replacementFactory, null, null, onMatchAsync),
            null,
            null);

    /// <summary>
    /// Registers a tail matcher that replaces the matched text via a context-aware async factory.
    /// </summary>
    /// <param name="maxMatchLength">Maximum number of tail characters to pass to the matcher.</param>
    /// <param name="matcher">Delegate that returns a match length ending at the buffer tail.</param>
    /// <param name="replacementFactory">Delegate that produces replacement text asynchronously with metrics.</param>
    /// <param name="priority">Lower values win when priorities are compared.</param>
    /// <param name="onMatch">Optional callback invoked when the pattern is found.</param>
    /// <returns>The current builder instance.</returns>
    public TextRewritePlanBuilder ReplaceTailMatcher(
        int maxMatchLength,
        TailMatcher matcher,
        ReplacementFactoryWithContextAsync replacementFactory,
        int priority = 0,
        MatchHandler? onMatch = null)
        => this.AddTailMatcher(
            maxMatchLength,
            matcher,
            CreateRule(MatchAction.Replace, priority, 0, maxMatchLength, null, null, null, null, replacementFactory, onMatch, null),
            null,
            null);

    public TextRewritePlanBuilder ReplaceTailMatcher(
        int maxMatchLength,
        TailMatcher matcher,
        ReplacementFactoryWithContextAsync replacementFactory,
        MatchHandlerAsync onMatchAsync,
        int priority = 0)
        => this.AddTailMatcher(
            maxMatchLength,
            matcher,
            CreateRule(MatchAction.Replace, priority, 0, maxMatchLength, null, null, null, null, replacementFactory, null, onMatchAsync),
            null,
            null);

    /// <summary>
    /// Registers a tail matcher rule using a custom <see cref="TextRewriteRuleEntry" /> implementation.
    /// </summary>
    public TextRewritePlanBuilder AddTailMatcherRule(
        int maxMatchLength,
        TailMatcher matcher,
        TextRewriteRuleEntry textRewriteRule,
        MatchHandler? onMatch = null,
        MatchHandlerAsync? onMatchAsync = null)
        => this.AddTailMatcher(maxMatchLength, matcher, textRewriteRule, onMatch, onMatchAsync);

    /// <summary>
    /// Compiles the configured rules into an immutable plan.
    /// </summary>
    /// <param name="options">Optional plan settings.</param>
    /// <returns>Compiled <see cref="TextRewritePlan" />.</returns>
    public TextRewritePlan Build(TextCompileOptions? options = null)
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
                ordinalIgnoreCase.Add(new(FoldOrdinalIgnoreCase(p.pattern), p.ruleId, p.comparison));
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
            maxMatchLength = Math.Max(maxMatchLength, rulesArray[i].MaxMatchLength);

        var hasAsyncRules = false;
        for (var i = 0; i < rulesArray.Length; i++)
        {
            if (!rulesArray[i].HasAsyncCallbacks)
                continue;

            hasAsyncRules = true;

            break;
        }

        var maxPending = maxMatchLength > 0 ? maxMatchLength - 1 : 0;

        var kinds = new string?[rulesArray.Length];
        var targets = new string?[rulesArray.Length];

        foreach (var p in this.literalPatterns)
        {
            kinds[p.ruleId] = "Literal";
            targets[p.ruleId] = p.pattern;
        }

        foreach (var r in this.regexRules)
        {
            kinds[r.ruleId] = "Regex";
            targets[r.ruleId] = r.regex.ToString();
        }

        foreach (var c in this.customRules)
        {
            kinds[c.ruleId] ??= "Tail";
            targets[c.ruleId] ??= null;
        }

        return new(
            rulesArray,
            ordinalAutomaton,
            ordinalIgnoreCaseAutomaton,
            regexRulesArray,
            customRulesArray,
            maxMatchLength,
            maxPending,
            selection,
            hasAsyncRules,
            kinds,
            targets);
    }

    public override TextRewritePlan Build() => this.Build(null);

    private static int GetPatternLength(string pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);

        if (pattern.Length == 0)
            throw new ArgumentException("Pattern must be non-empty.", nameof(pattern));

        return pattern.Length;
    }

    private static TextRewriteRuleEntry CreateRule(
        MatchAction action,
        int priority,
        int fixedLength,
        int maxMatchLength,
        string? replacement,
        ReplacementFactory? replacementFactory,
        ReplacementFactoryWithContext? replacementFactoryWithContext,
        ReplacementFactoryAsync? replacementFactoryAsync,
        ReplacementFactoryWithContextAsync? replacementFactoryWithContextAsync,
        MatchHandler? onMatch,
        MatchHandlerAsync? onMatchAsync)
        => new StandardRuleEntry(
            action,
            priority,
            fixedLength,
            maxMatchLength,
            replacement,
            replacementFactory,
            replacementFactoryWithContext,
            replacementFactoryAsync,
            replacementFactoryWithContextAsync,
            onMatch,
            onMatchAsync);

    private static TextRewriteRuleEntry DecorateRule(
        TextRewriteRuleEntry textRewriteRule,
        MatchHandler? onMatch,
        MatchHandlerAsync? onMatchAsync)
    {
        if (onMatch is null && onMatchAsync is null)
            return textRewriteRule;

        return new DecoratedTextRewriteRuleEntry(textRewriteRule, onMatch, onMatchAsync);
    }

    private TextRewritePlanBuilder AddLiteral(
        string pattern,
        TextRewriteRuleEntry textRewriteRule,
        StringComparison comparison,
        MatchHandler? onMatch,
        MatchHandlerAsync? onMatchAsync)
    {
        ArgumentNullException.ThrowIfNull(pattern);

        if (pattern.Length == 0)
            throw new ArgumentException("Pattern must be non-empty.", nameof(pattern));
        ArgumentNullException.ThrowIfNull(textRewriteRule);

        if (comparison != StringComparison.Ordinal && comparison != StringComparison.OrdinalIgnoreCase)
            throw new NotSupportedException($"Only Ordinal and OrdinalIgnoreCase are supported. Got {comparison}.");

        if (textRewriteRule.FixedLength != pattern.Length)
            throw new ArgumentException("Rule.FixedLength must match literal length.", nameof(textRewriteRule));
        if (textRewriteRule.MaxMatchLength < pattern.Length)
            throw new ArgumentOutOfRangeException(nameof(textRewriteRule), "Rule.MaxMatchLength must be >= literal length.");

        var ruleId = this.rules.Count;
        var effectiveRule = DecorateRule(textRewriteRule, onMatch, onMatchAsync);

        effectiveRule.AssignOrder(ruleId);
        this.rules.Add(effectiveRule);

        this.literalPatterns.Add(new(pattern, ruleId, comparison));

        return this;
    }

    private TextRewritePlanBuilder AddRegex(
        Regex regex,
        int maxMatchLength,
        TextRewriteRuleEntry textRewriteRule,
        MatchHandler? onMatch,
        MatchHandlerAsync? onMatchAsync)
    {
        ArgumentNullException.ThrowIfNull(regex);

        if (maxMatchLength <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxMatchLength), "maxMatchLength must be > 0.");
        ArgumentNullException.ThrowIfNull(textRewriteRule);

        if (textRewriteRule.MaxMatchLength < maxMatchLength)
            throw new ArgumentOutOfRangeException(nameof(textRewriteRule), "Rule.MaxMatchLength must be >= maxMatchLength.");

        var ruleId = this.rules.Count;
        var effectiveRule = DecorateRule(textRewriteRule, onMatch, onMatchAsync);
        effectiveRule.AssignOrder(ruleId);
        this.rules.Add(effectiveRule);

        this.regexRules.Add(new(ruleId, regex, maxMatchLength));

        return this;
    }

    private static Regex BuildRegex(string pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);

        if (pattern.Length == 0)
            throw new ArgumentException("Pattern must be non-empty.", nameof(pattern));

        return new(pattern, RegexOptions.CultureInvariant | RegexOptions.Compiled);
    }

    private TextRewritePlanBuilder AddTailMatcher(
        int maxMatchLength,
        TailMatcher matcher,
        TextRewriteRuleEntry textRewriteRule,
        MatchHandler? onMatch,
        MatchHandlerAsync? onMatchAsync)
    {
        ArgumentNullException.ThrowIfNull(matcher);

        if (maxMatchLength <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxMatchLength), "maxMatchLength must be > 0.");
        ArgumentNullException.ThrowIfNull(textRewriteRule);

        if (textRewriteRule.MaxMatchLength < maxMatchLength)
            throw new ArgumentOutOfRangeException(nameof(textRewriteRule), "Rule.MaxMatchLength must be >= maxMatchLength.");

        var ruleId = this.rules.Count;
        var effectiveRule = DecorateRule(textRewriteRule, onMatch, onMatchAsync);
        effectiveRule.AssignOrder(ruleId);
        this.rules.Add(effectiveRule);

        this.customRules.Add(new(ruleId, matcher, maxMatchLength));

        return this;
    }

    private static string FoldOrdinalIgnoreCase(string s)
        => string.Create(
            s.Length,
            s,
            static (dst, src) =>
            {
                for (var i = 0; i < dst.Length; i++)
                    dst[i] = FoldCharOrdinalIgnoreCase(src[i]);
            });

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static char FoldCharOrdinalIgnoreCase(char c)
    {
        if ((uint)(c - 'a') <= (uint)('z' - 'a'))
            return (char)(c - 32);

        return char.ToUpperInvariant(c);
    }

    internal readonly record struct LiteralPattern(string pattern, int ruleId, StringComparison comparison);

    private sealed class DecoratedTextRewriteRuleEntry(
        TextRewriteRuleEntry inner,
        MatchHandler? extraHandler,
        MatchHandlerAsync? extraHandlerAsync)
        : TextRewriteRuleEntry(inner.Action, inner.Priority, inner.FixedLength, inner.MaxMatchLength)
    {
        public override string? Replacement => inner.Replacement;
        public override ReplacementFactory? ReplacementFactory => inner.ReplacementFactory;
        public override ReplacementFactoryWithContext? ReplacementFactoryWithContext => inner.ReplacementFactoryWithContext;
        public override ReplacementFactoryAsync? ReplacementFactoryAsync => inner.ReplacementFactoryAsync;
        public override ReplacementFactoryWithContextAsync? ReplacementFactoryWithContextAsync => inner.ReplacementFactoryWithContextAsync;

        public override MatchHandler? OnMatch
        {
            get
            {
                if (inner.OnMatch is null && extraHandler is null)
                    return null;

                return (id, span) =>
                {
                    inner.OnMatch?.Invoke(id, span);
                    extraHandler?.Invoke(id, span);
                };
            }
        }

        public override MatchHandlerAsync? OnMatchAsync
        {
            get
            {
                if (inner.OnMatchAsync is null && extraHandlerAsync is null && this.OnMatch is null)
                    return null;

                return async (id, memory) =>
                {
                    if (inner.OnMatchAsync is not null)
                        await inner.OnMatchAsync(id, memory).ConfigureAwait(false);
                    else
                        inner.OnMatch?.Invoke(id, memory.Span);

                    if (extraHandlerAsync is not null)
                        await extraHandlerAsync(id, memory).ConfigureAwait(false);
                    else
                        extraHandler?.Invoke(id, memory.Span);
                };
            }
        }

        public override bool HasAsyncCallbacks
            => base.HasAsyncCallbacks
               || inner.HasAsyncCallbacks
               || extraHandlerAsync is not null;
    }
}