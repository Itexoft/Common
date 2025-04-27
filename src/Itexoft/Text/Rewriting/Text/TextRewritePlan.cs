// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Text.Rewriting.Primitives;
using Itexoft.Text.Rewriting.Text.Internal.Matching;

namespace Itexoft.Text.Rewriting.Text;

/// <summary>
/// Immutable plan compiled from a <see cref="TextRewritePlanBuilder" />.
/// </summary>
public sealed class TextRewritePlan : RewritePlan<TextRewriteRuleEntry>
{
    internal readonly CustomRuleEntry[] customRules;
    internal readonly int maxMatchLength;
    internal readonly int maxPending;
    internal readonly AhoCorasickAutomaton? ordinalAutomaton;
    internal readonly AhoCorasickAutomaton? ordinalIgnoreCaseAutomaton;
    internal readonly RegexRuleEntry[] regexRules;
    internal readonly string?[] ruleKinds;
    internal readonly string?[] ruleTargets;
    internal readonly MatchSelection selection;

    internal TextRewritePlan(
        TextRewriteRuleEntry[] rules,
        AhoCorasickAutomaton? ordinalAutomaton,
        AhoCorasickAutomaton? ordinalIgnoreCaseAutomaton,
        RegexRuleEntry[] regexRules,
        CustomRuleEntry[] customRules,
        int maxMatchLength,
        int maxPending,
        MatchSelection selection,
        bool hasAsyncRules,
        string?[] ruleKinds,
        string?[] ruleTargets) : base(rules, hasAsyncRules)
    {
        this.ordinalAutomaton = ordinalAutomaton;
        this.ordinalIgnoreCaseAutomaton = ordinalIgnoreCaseAutomaton;
        this.regexRules = regexRules;
        this.customRules = customRules;
        this.maxMatchLength = maxMatchLength;
        this.maxPending = maxPending;
        this.selection = selection;
        this.ruleKinds = ruleKinds;
        this.ruleTargets = ruleTargets;
    }
}
