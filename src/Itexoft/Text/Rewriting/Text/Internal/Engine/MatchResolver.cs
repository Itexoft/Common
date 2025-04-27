// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.CompilerServices;
using Itexoft.Text.Rewriting.Primitives;
using Itexoft.Text.Rewriting.Text.Internal.Gates;
using Itexoft.Text.Rewriting.Text.Internal.Matching;
using Itexoft.Threading;

namespace Itexoft.Text.Rewriting.Text.Internal.Engine;

/// <summary>
/// Isolates match selection logic from the streaming engine to keep buffering and IO concerns separate.
/// </summary>
internal sealed class MatchResolver
{
    private readonly TextRewritePlan plan;
    private int ordinalIgnoreCaseState;
    private int ordinalState;

    internal MatchResolver(TextRewritePlan plan)
    {
        this.plan = plan ?? throw new ArgumentNullException(nameof(plan));
        this.ordinalState = 0;
        this.ordinalIgnoreCaseState = 0;
    }

    internal MatchCandidate FindBestCandidate(
        char currentChar,
        char[] pendingBuffer,
        int pendingStart,
        int pendingLength,
        SyncAsyncLazy<RuleGateSnapshot> gate)
    {
        var best = MatchCandidate.None;

        if (this.plan.ordinalAutomaton is not null)
        {
            this.ordinalState = this.plan.ordinalAutomaton.Step(this.ordinalState, currentChar);
            var ruleId = this.plan.ordinalAutomaton.GetBestRuleId(this.ordinalState);

            if (ruleId >= 0 && gate.GetOrCreate().Allows(ruleId))
                best = this.ChooseBetter(best, ruleId, this.plan.Rules[ruleId].FixedLength);
        }

        if (this.plan.ordinalIgnoreCaseAutomaton is not null)
        {
            var folded = FoldCharOrdinalIgnoreCase(currentChar);
            this.ordinalIgnoreCaseState = this.plan.ordinalIgnoreCaseAutomaton.Step(this.ordinalIgnoreCaseState, folded);
            var ruleId = this.plan.ordinalIgnoreCaseAutomaton.GetBestRuleId(this.ordinalIgnoreCaseState);

            if (ruleId >= 0 && gate.GetOrCreate().Allows(ruleId))
                best = this.ChooseBetter(best, ruleId, this.plan.Rules[ruleId].FixedLength);
        }

        if (pendingLength != 0 && (this.plan.regexRules.Length != 0 || this.plan.customRules.Length != 0))
            best = this.EvaluateTailRules(pendingBuffer, pendingStart, pendingLength, () => gate.GetOrCreate(), best);

        return best;
    }

    internal async ValueTask<MatchCandidate> FindBestCandidateAsync(
        char currentChar,
        char[] pendingBuffer,
        int pendingStart,
        int pendingLength,
        SyncAsyncLazy<RuleGateSnapshot> gate,
        CancelToken cancelToken)
    {
        cancelToken.ThrowIf();

        var best = MatchCandidate.None;
        var gateCreated = false;
        RuleGateSnapshot gateSnapshot = default;

        async ValueTask<RuleGateSnapshot> getGateAsync()
        {
            if (gateCreated)
                return gateSnapshot;

            gateSnapshot = await gate.GetOrCreateAsync(cancelToken).ConfigureAwait(false);
            gateCreated = true;

            return gateSnapshot;
        }

        if (this.plan.ordinalAutomaton is not null)
        {
            this.ordinalState = this.plan.ordinalAutomaton.Step(this.ordinalState, currentChar);
            var ruleId = this.plan.ordinalAutomaton.GetBestRuleId(this.ordinalState);

            if (ruleId >= 0 && (await getGateAsync().ConfigureAwait(false)).Allows(ruleId))
                best = this.ChooseBetter(best, ruleId, this.plan.Rules[ruleId].FixedLength);
        }

        if (this.plan.ordinalIgnoreCaseAutomaton is not null)
        {
            var folded = FoldCharOrdinalIgnoreCase(currentChar);
            this.ordinalIgnoreCaseState = this.plan.ordinalIgnoreCaseAutomaton.Step(this.ordinalIgnoreCaseState, folded);
            var ruleId = this.plan.ordinalIgnoreCaseAutomaton.GetBestRuleId(this.ordinalIgnoreCaseState);

            if (ruleId >= 0 && (await getGateAsync().ConfigureAwait(false)).Allows(ruleId))
                best = this.ChooseBetter(best, ruleId, this.plan.Rules[ruleId].FixedLength);
        }

        if (pendingLength != 0 && (this.plan.regexRules.Length != 0 || this.plan.customRules.Length != 0))
            best = await this.EvaluateTailRulesAsync(pendingBuffer, pendingStart, pendingLength, getGateAsync, best).ConfigureAwait(false);

        return best;
    }

    internal void ResetAutomata()
    {
        this.ordinalState = 0;
        this.ordinalIgnoreCaseState = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal MatchCandidate ChooseBetter(MatchCandidate current, int ruleId, int matchLength)
    {
        if (ruleId < 0)
            return current;

        var rule = this.plan.Rules[ruleId];

        if (!current.HasValue)
            return new(ruleId, matchLength, rule.Priority, rule.Order);

        if (this.plan.selection == MatchSelection.LongestThenPriority)
        {
            if (matchLength > current.MatchLength)
                return new(ruleId, matchLength, rule.Priority, rule.Order);

            if (matchLength < current.MatchLength)
                return current;
        }
        else
        {
            if (rule.Priority < current.Priority)
                return new(ruleId, matchLength, rule.Priority, rule.Order);

            if (rule.Priority > current.Priority)
                return current;
        }

        if (rule.Priority < current.Priority)
            return new(ruleId, matchLength, rule.Priority, rule.Order);

        if (rule.Priority > current.Priority)
            return current;

        if (rule.Order < current.Order)
            return new(ruleId, matchLength, rule.Priority, rule.Order);

        if (rule.Order > current.Order)
            return current;

        if (matchLength > current.MatchLength)
            return new(ruleId, matchLength, rule.Priority, rule.Order);

        return current;
    }

    private MatchCandidate EvaluateTailRules(
        char[] pendingBuffer,
        int pendingStart,
        int processed,
        Func<RuleGateSnapshot> gate,
        MatchCandidate currentBest)
    {
        var best = currentBest;
        var spanTotal = processed;

        for (var i = 0; i < this.plan.regexRules.Length; i++)
        {
            var rr = this.plan.regexRules[i];
            var tailLen = spanTotal < rr.MaxMatchLength ? spanTotal : rr.MaxMatchLength;

            if (tailLen == 0)
                continue;

            var tailSpan = pendingBuffer.AsSpan(pendingStart + spanTotal - tailLen, tailLen);

            var bestLen = 0;

            foreach (var m in rr.Regex.EnumerateMatches(tailSpan))
            {
                if (m.Length != 0 && m.Index + m.Length == tailLen && m.Length > bestLen)
                    bestLen = m.Length;
            }

            if (bestLen != 0 && gate().Allows(rr.RuleId))
                best = this.ChooseBetter(best, rr.RuleId, bestLen);
        }

        for (var i = 0; i < this.plan.customRules.Length; i++)
        {
            var cr = this.plan.customRules[i];
            var tailLen = spanTotal < cr.MaxMatchLength ? spanTotal : cr.MaxMatchLength;

            if (tailLen == 0)
                continue;

            var tailSpan = pendingBuffer.AsSpan(pendingStart + spanTotal - tailLen, tailLen);
            var matchLen = cr.Matcher(tailSpan);

            if (matchLen > 0 && matchLen <= tailLen && gate().Allows(cr.RuleId))
                best = this.ChooseBetter(best, cr.RuleId, matchLen);
        }

        return best;
    }

    private async ValueTask<MatchCandidate> EvaluateTailRulesAsync(
        char[] pendingBuffer,
        int pendingStart,
        int processed,
        Func<ValueTask<RuleGateSnapshot>> gate,
        MatchCandidate currentBest)
    {
        var best = currentBest;
        var spanTotal = processed;

        for (var i = 0; i < this.plan.regexRules.Length; i++)
        {
            var rr = this.plan.regexRules[i];
            var tailLen = spanTotal < rr.MaxMatchLength ? spanTotal : rr.MaxMatchLength;

            if (tailLen == 0)
                continue;

            var tailSpan = pendingBuffer.AsSpan(pendingStart + spanTotal - tailLen, tailLen);

            var bestLen = 0;

            foreach (var m in rr.Regex.EnumerateMatches(tailSpan))
            {
                if (m.Length != 0 && m.Index + m.Length == tailLen && m.Length > bestLen)
                    bestLen = m.Length;
            }

            if (bestLen != 0 && (await gate().ConfigureAwait(false)).Allows(rr.RuleId))
                best = this.ChooseBetter(best, rr.RuleId, bestLen);
        }

        for (var i = 0; i < this.plan.customRules.Length; i++)
        {
            var cr = this.plan.customRules[i];
            var tailLen = spanTotal < cr.MaxMatchLength ? spanTotal : cr.MaxMatchLength;

            if (tailLen == 0)
                continue;

            var tailSpan = pendingBuffer.AsSpan(pendingStart + spanTotal - tailLen, tailLen);
            var matchLen = cr.Matcher(tailSpan);

            if (matchLen > 0 && matchLen <= tailLen && (await gate().ConfigureAwait(false)).Allows(cr.RuleId))
                best = this.ChooseBetter(best, cr.RuleId, matchLen);
        }

        return best;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static char FoldCharOrdinalIgnoreCase(char c)
    {
        if ((uint)(c - 'a') <= (uint)('z' - 'a'))
            return (char)(c - 32);

        return char.ToUpperInvariant(c);
    }
}
