// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Text.Rewriting.Primitives;

/// <summary>
/// Base class for rewrite plan builders.
/// </summary>
/// <typeparam name="TBuilder">Builder type for fluent chaining.</typeparam>
/// <typeparam name="TPlan">Plan type being built.</typeparam>
public abstract class RewritePlanBuilder<TBuilder, TPlan> where TBuilder : RewritePlanBuilder<TBuilder, TPlan>
{
    /// <summary>
    /// Builds a plan with default options.
    /// </summary>
    /// <returns>Compiled plan.</returns>
    public abstract TPlan Build();
}

/// <summary>
/// Base class for immutable rewrite plans.
/// </summary>
public abstract class RewritePlan(bool hasAsyncRules = false)
{
    /// <summary>
    /// Gets the total number of compiled rules.
    /// </summary>
    public abstract int RuleCount { get; }

    internal bool HasAsyncRules => hasAsyncRules;
}

/// <summary>
/// Generic base class for immutable rewrite plans with typed rule entries.
/// </summary>
/// <typeparam name="TRuleEntry">Rule entry type.</typeparam>
public abstract class RewritePlan<TRuleEntry>(TRuleEntry[] rules, bool hasAsyncRules = false) : RewritePlan(hasAsyncRules)
{
    public override int RuleCount => this.Rules.Length;

    internal TRuleEntry[] Rules { get; } = rules ?? throw new ArgumentNullException(nameof(rules));

    internal ReadOnlySpan<TRuleEntry> RulesSpan => this.Rules;
}
