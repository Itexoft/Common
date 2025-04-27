// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Text.RegularExpressions;
using Itexoft.Text.Rewriting.Internal.Runtime;
using Itexoft.Text.Rewriting.Primitives;
using Itexoft.Text.Rewriting.Text.Dsl.Internal;
using Itexoft.Text.Rewriting.Text.Internal;

namespace Itexoft.Text.Rewriting.Text.Dsl;

/// <summary>
/// Provides fluent configuration for a single text rule.
/// </summary>
public sealed class TextRuleBuilder<THandlers>
{
    private readonly TextRewritePlanBuilder builder;
    private readonly StringComparison comparison;
    private readonly string? group;
    private readonly TextRuleKind kind;
    private readonly TailMatcher? matcher;
    private readonly int maxMatchLength;
    private readonly string? name;
    private readonly string? pattern;
    private readonly Regex? regex;
    private readonly List<string?> ruleGroups;
    private readonly List<string?> ruleNames;
    private readonly HandlerScope<THandlers> scope;

    private bool built;
    private int priority;

    internal TextRuleBuilder(
        TextRewritePlanBuilder builder,
        HandlerScope<THandlers> scope,
        List<string?> ruleNames,
        List<string?> ruleGroups,
        TextRuleKind kind,
        string pattern,
        StringComparison comparison,
        string? name,
        string? group)
    {
        this.builder = builder;
        this.scope = scope;
        this.ruleNames = ruleNames;
        this.ruleGroups = ruleGroups;
        this.kind = kind;
        this.pattern = pattern;
        this.maxMatchLength = 0;
        this.name = name;
        this.group = group;
        this.comparison = comparison;
        this.regex = null;
    }

    internal TextRuleBuilder(
        TextRewritePlanBuilder builder,
        HandlerScope<THandlers> scope,
        List<string?> ruleNames,
        List<string?> ruleGroups,
        TextRuleKind kind,
        string pattern,
        RegexOptions options,
        int maxMatchLength,
        string? name,
        string? group)
    {
        this.builder = builder;
        this.scope = scope;
        this.ruleNames = ruleNames;
        this.ruleGroups = ruleGroups;
        this.kind = kind;
        this.pattern = pattern;
        this.maxMatchLength = maxMatchLength;
        this.name = name;
        this.group = group;
        this.comparison = StringComparison.Ordinal;
        this.regex = new(pattern, options);
    }

    internal TextRuleBuilder(
        TextRewritePlanBuilder builder,
        HandlerScope<THandlers> scope,
        List<string?> ruleNames,
        List<string?> ruleGroups,
        TextRuleKind kind,
        Regex regex,
        int maxMatchLength,
        string? name,
        string? group)
    {
        this.builder = builder;
        this.scope = scope;
        this.ruleNames = ruleNames;
        this.ruleGroups = ruleGroups;
        this.kind = kind;
        this.pattern = regex.ToString();
        this.maxMatchLength = maxMatchLength;
        this.name = name;
        this.group = group;
        this.comparison = StringComparison.Ordinal;
        this.regex = regex ?? throw new ArgumentNullException(nameof(regex));
    }

    internal TextRuleBuilder(
        TextRewritePlanBuilder builder,
        HandlerScope<THandlers> scope,
        List<string?> ruleNames,
        List<string?> ruleGroups,
        TextRuleKind kind,
        TailMatcher matcher,
        int maxMatchLength,
        string? name,
        string? group)
    {
        this.builder = builder;
        this.scope = scope;
        this.ruleNames = ruleNames;
        this.ruleGroups = ruleGroups;
        this.kind = kind;
        this.matcher = matcher;
        this.maxMatchLength = maxMatchLength;
        this.name = name;
        this.group = group;
        this.comparison = StringComparison.Ordinal;
    }

    /// <summary>
    /// Sets rule priority; lower values win when conflicts are resolved.
    /// </summary>
    public TextRuleBuilder<THandlers> Priority(int value)
    {
        this.priority = value;

        return this;
    }

    /// <summary>
    /// Hooks the match without mutating output using a synchronous callback.
    /// </summary>
    public TextRuleBuilder<THandlers> Hook(Action<THandlers, int, ReadOnlySpan<char>> onMatch)
    {
        this.EnsureNotBuilt();

        var handler = this.Wrap(onMatch);
        this.AddRule(MatchAction.None, new() { OnMatch = handler });

        return this;
    }

    /// <summary>
    /// Hooks the match without mutating output using an asynchronous callback.
    /// </summary>
    public TextRuleBuilder<THandlers> Hook(Func<THandlers, int, ReadOnlyMemory<char>, ValueTask> onMatchAsync)
    {
        this.EnsureNotBuilt();

        var handlerAsync = this.Wrap(onMatchAsync);
        this.AddRule(MatchAction.None, new() { OnMatchAsync = handlerAsync });

        return this;
    }

    /// <summary>
    /// Removes the matched text and optionally invokes a callback.
    /// </summary>
    public TextRuleBuilder<THandlers> Remove(Action<THandlers, int, ReadOnlySpan<char>>? onMatch = null)
    {
        this.EnsureNotBuilt();

        this.AddRule(MatchAction.Remove, new() { OnMatch = this.Wrap(onMatch) });

        return this;
    }

    /// <summary>
    /// Removes the matched text and invokes an asynchronous callback.
    /// </summary>
    public TextRuleBuilder<THandlers> Remove(Func<THandlers, int, ReadOnlyMemory<char>, ValueTask> onMatchAsync)
    {
        this.EnsureNotBuilt();

        this.AddRule(MatchAction.Remove, new() { OnMatchAsync = this.Wrap(onMatchAsync) });

        return this;
    }

    /// <summary>
    /// Replaces the matched text with a fixed string and optionally invokes a callback.
    /// </summary>
    public TextRuleBuilder<THandlers> Replace(string replacement, Action<THandlers, int, ReadOnlySpan<char>>? onMatch = null)
    {
        this.EnsureNotBuilt();

        this.AddRule(MatchAction.Replace, new() { Replacement = replacement, OnMatch = this.Wrap(onMatch) });

        return this;
    }

    /// <summary>
    /// Replaces the matched text using a synchronous factory.
    /// </summary>
    public TextRuleBuilder<THandlers> Replace(Func<THandlers, int, ReadOnlySpan<char>, string?> replacementFactory)
    {
        this.EnsureNotBuilt();

        this.AddRule(MatchAction.Replace, new() { ReplacementFactory = this.Wrap(replacementFactory) });

        return this;
    }

    /// <summary>
    /// Replaces the matched text using an asynchronous factory.
    /// </summary>
    public TextRuleBuilder<THandlers> Replace(Func<THandlers, int, ReadOnlyMemory<char>, ValueTask<string?>> replacementFactoryAsync)
    {
        this.EnsureNotBuilt();

        this.AddRule(MatchAction.Replace, new() { ReplacementFactoryAsync = this.Wrap(replacementFactoryAsync) });

        return this;
    }

    /// <summary>
    /// Replaces the matched text using a factory that receives current metrics.
    /// </summary>
    public TextRuleBuilder<THandlers> Replace(Func<THandlers, int, ReadOnlySpan<char>, RewriteMetrics, string?> replacementFactoryWithContext)
    {
        this.EnsureNotBuilt();

        this.AddRule(MatchAction.Replace, new() { ReplacementFactoryWithContext = this.Wrap(replacementFactoryWithContext) });

        return this;
    }

    private void AddRule(MatchAction action, RuleCallbacks callbacks)
    {
        this.ruleNames.Add(this.name);
        this.ruleGroups.Add(this.group);

        switch (this.kind)
        {
            case TextRuleKind.Literal:
                this.builder.RegisterLiteral(this.pattern!, this.comparison, this.priority, action, callbacks);

                break;
            case TextRuleKind.Regex:
                this.builder.RegisterRegex(this.regex!, this.maxMatchLength, this.priority, action, callbacks);

                break;
            case TextRuleKind.Tail:
                this.builder.RegisterTailMatcher(this.maxMatchLength, this.matcher!, this.priority, action, callbacks);

                break;
        }

        this.built = true;
    }

    private void EnsureNotBuilt()
    {
        if (this.built)
            throw new InvalidOperationException("Rule already configured.");
    }

    private MatchHandler? Wrap(Action<THandlers, int, ReadOnlySpan<char>>? handler)
    {
        if (handler is null)
            return null;

        return (id, span) => handler(this.GetHandler(), id, span);
    }

    private MatchHandlerAsync? Wrap(Func<THandlers, int, ReadOnlyMemory<char>, ValueTask>? handler)
    {
        if (handler is null)
            return null;

        return (id, memory) => handler(this.GetHandler(), id, memory);
    }

    private ReplacementFactory Wrap(Func<THandlers, int, ReadOnlySpan<char>, string?> factory) => (id, span) => factory(this.GetHandler(), id, span);

    private ReplacementFactoryWithContext Wrap(Func<THandlers, int, ReadOnlySpan<char>, RewriteMetrics, string?> factory) =>
        (id, span, metrics) => factory(this.GetHandler(), id, span, metrics);

    private ReplacementFactoryAsync Wrap(Func<THandlers, int, ReadOnlyMemory<char>, ValueTask<string?>> factory) =>
        (id, memory) => factory(this.GetHandler(), id, memory);

    private THandlers GetHandler() => this.scope.Current ?? throw new InvalidOperationException("Handler scope is not set.");
}
