// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Text.RegularExpressions;
using Itexoft.Extensions;
using Itexoft.Text.Rewriting.Internal.Runtime;
using Itexoft.Text.Rewriting.Primitives;
using Itexoft.Text.Rewriting.Text.Dsl.Internal;

namespace Itexoft.Text.Rewriting.Text.Dsl;

/// <summary>
/// Fluent DSL used to declare streaming text rewrite rules.
/// </summary>
public sealed class TextDsl<THandlers> where THandlers : class
{
    private readonly TextRewritePlanBuilder builder;
    private readonly string? currentGroup;
    private readonly List<string?> ruleGroups;
    private readonly List<string?> ruleNames;

    internal TextDsl(
        TextRewritePlanBuilder builder,
        HandlerScope<THandlers> scope,
        List<string?> ruleNames,
        List<string?> ruleGroups,
        string? currentGroup = null)
    {
        this.builder = builder;
        this.Scope = scope;
        this.ruleNames = ruleNames;
        this.ruleGroups = ruleGroups;
        this.currentGroup = currentGroup;
    }

    internal HandlerScope<THandlers> Scope { get; }

    /// <summary>
    /// Registers a literal rule against the provided pattern.
    /// </summary>
    public TextRuleBuilder<THandlers> Literal(string pattern, StringComparison comparison = StringComparison.Ordinal, string? name = null) => new(
        this.builder,
        this.Scope,
        this.ruleNames,
        this.ruleGroups,
        TextRuleKind.Literal,
        pattern,
        comparison,
        name,
        this.currentGroup);

    /// <summary>
    /// Registers a regex rule that evaluates matches ending at the buffer tail.
    /// </summary>
    public TextRuleBuilder<THandlers> Regex(
        string pattern,
        int maxMatchLength,
        RegexOptions options = RegexOptions.CultureInvariant | RegexOptions.Compiled,
        string? name = null) => new(
        this.builder,
        this.Scope,
        this.ruleNames,
        this.ruleGroups,
        TextRuleKind.Regex,
        pattern,
        options,
        maxMatchLength,
        name,
        this.currentGroup);

    /// <summary>
    /// Registers a regex rule using a preconfigured regex instance.
    /// </summary>
    public TextRuleBuilder<THandlers> Regex(Regex regex, int maxMatchLength, string? name = null) => new(
        this.builder,
        this.Scope,
        this.ruleNames,
        this.ruleGroups,
        TextRuleKind.Regex,
        regex,
        maxMatchLength,
        name,
        this.currentGroup);

    /// <summary>
    /// Registers a custom tail matcher that returns a match length for the buffered tail.
    /// </summary>
    public TextRuleBuilder<THandlers> Tail(int maxMatchLength, TailMatcher matcher, string? name = null) => new(
        this.builder,
        this.Scope,
        this.ruleNames,
        this.ruleGroups,
        TextRuleKind.Tail,
        matcher,
        maxMatchLength,
        name,
        this.currentGroup);

    /// <summary>
    /// Groups nested rules under the provided group name.
    /// </summary>
    public void Group(string group, Action<TextDsl<THandlers>> configure)
    {
        configure.Required();

        configure(new(this.builder, this.Scope, this.ruleNames, this.ruleGroups, group));
    }
}
