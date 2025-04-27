// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Text.Rewriting.Json.Internal.Rules;
using Itexoft.Text.Rewriting.Primitives;

namespace Itexoft.Text.Rewriting.Json;

/// <summary>
/// Immutable plan of JSON rewriting rules.
/// </summary>
public sealed class JsonRewritePlan : RewritePlan<JsonRewriteRule>
{
    internal JsonRewritePlan(JsonRewriteRule[] rules, string?[] ruleNames, string?[] ruleGroups, string?[] ruleKinds, string?[] ruleTargets) : base(
        rules ?? throw new ArgumentNullException(nameof(rules)),
        HasAsync(rules))
    {
        this.RuleNames = ruleNames ?? [];
        this.RuleGroups = ruleGroups ?? [];
        this.RuleKinds = ruleKinds ?? [];
        this.RuleTargets = ruleTargets ?? [];

        var allRules = this.Rules;
        this.ReplaceValueRules = FilterByType<ReplaceValueRule>(allRules);
        this.RenamePropertyRules = FilterByType<RenamePropertyRule>(allRules);
        this.RequireRules = FilterByType<RequireRule>(allRules);
        this.CaptureRules = FilterByType<CaptureRule>(allRules);
        this.CaptureAsyncRules = FilterByType<CaptureAsyncRule>(allRules);
        this.ReplaceInStringRules = FilterByType<ReplaceInStringRule>(allRules);
        this.ProjectionCaptureRules = FilterByType<ProjectionCaptureRule>(allRules);
    }

    internal string?[] RuleNames { get; }

    internal string?[] RuleGroups { get; }

    internal string?[] RuleKinds { get; }

    internal string?[] RuleTargets { get; }

    internal ReplaceValueRule[] ReplaceValueRules { get; }

    internal RenamePropertyRule[] RenamePropertyRules { get; }

    internal RequireRule[] RequireRules { get; }

    internal CaptureRule[] CaptureRules { get; }

    internal CaptureAsyncRule[] CaptureAsyncRules { get; }

    internal ReplaceInStringRule[] ReplaceInStringRules { get; }

    internal ProjectionCaptureRule[] ProjectionCaptureRules { get; }

    private static bool HasAsync(JsonRewriteRule[] rules)
    {
        for (var i = 0; i < rules.Length; i++)
        {
            if (rules[i].HasAsync)
                return true;
        }

        return false;
    }

    private static TRule[] FilterByType<TRule>(JsonRewriteRule[] source) where TRule : JsonRewriteRule
    {
        if (source.Length == 0)
            return [];

        var list = new List<TRule>(source.Length);

        foreach (var t in source)
        {
            if (t is TRule typed)
                list.Add(typed);
        }

        return list.ToArray();
    }
}
