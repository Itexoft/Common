// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Diagnostics;
using Itexoft.Text.Rewriting.Primitives;

namespace Itexoft.Text.Rewriting.Text.Internal.Engine;

/// <summary>
/// Encapsulates collection and publication of per-rule metrics so the streaming engine
/// focuses only on matching and buffering.
/// </summary>
internal sealed class RuleMetricsTracker
{
    private readonly long[]? elapsedTicks;
    private readonly bool enabled;
    private readonly long[]? hits;

    internal RuleMetricsTracker(int ruleCount, bool enabled)
    {
        this.enabled = enabled;

        if (!enabled)
            return;

        this.hits = new long[ruleCount];
        this.elapsedTicks = new long[ruleCount];
    }

    internal long Start(int ruleId)
    {
        if (!this.enabled || this.hits is null || this.elapsedTicks is null)
            return 0;

        this.hits[ruleId]++;

        return Stopwatch.GetTimestamp();
    }

    internal void Stop(int ruleId, long startTicks)
    {
        if (!this.enabled || this.elapsedTicks is null)
            return;

        if (startTicks == 0)
            return;

        this.elapsedTicks[ruleId] += Stopwatch.GetTimestamp() - startTicks;
    }

    internal IReadOnlyList<RuleStat> BuildStats(TextRewritePlan plan, TextRewriteOptions options)
    {
        if (!this.enabled || this.hits is null || this.elapsedTicks is null)
            return [];

        if (options.OnRuleMetrics is null && options.OnRuleMetricsAsync is null)
            return [];

        var frequency = Stopwatch.Frequency;
        var list = new List<RuleStat>(plan.RuleCount);

        for (var i = 0; i < plan.RuleCount; i++)
        {
            var hit = this.hits[i];
            var elapsed = this.elapsedTicks[i];

            if (hit == 0 && elapsed == 0)
                continue;

            var name = i < options.RuleNames.Length ? options.RuleNames[i] : null;
            var group = i < options.RuleGroups.Length ? options.RuleGroups[i] : null;
            list.Add(new(i, name, group, hit, TimeSpan.FromSeconds(elapsed / (double)frequency)));
        }

        return list;
    }
}
