// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.CompilerServices;
using Itexoft.Algorithms.StateMachines.MatchScanning;
using Itexoft.Text.Rewriting.Primitives;

namespace Itexoft.Text.Rewriting.Text.Internal.Matching;

internal struct TextAutomataState
{
    internal TextRewritePlan plan;
    internal int minOffset;
    internal int ordinalState;
    internal int ordinalIgnoreCaseState;
}

internal sealed class TextPendingMatchAutomata : IPendingMatchAutomata<TextAutomataState>
{
    public void Init(ref PendingMatchScanner<TextAutomataState>.ScannerState state)
    {
        state.automataState = default;
        state.automataState.minOffset = 0;
        state.automataState.ordinalState = 0;
        state.automataState.ordinalIgnoreCaseState = 0;
    }

    public void OnPendingCandidateSelected(
        ref PendingMatchScanner<TextAutomataState>.ScannerState state,
        int candidateRuleId,
        int candidateMatchLength,
        int candidateOffset) { }

    public bool TryAcceptPendingCandidate(ref PendingMatchScanner<TextAutomataState>.ScannerState state)
    {
        var custom = state.automataState;
        var candidateRuleId = state.pendingRuleId;

        if (candidateRuleId < 0 || state.pendingOffset < custom.minOffset)
            return false;

        var rules = custom.plan.Rules;
        var selection = custom.plan.selection;

        if (state.bestRuleId < 0
            || ShouldPrefer(rules, selection, state.bestRuleId, state.bestMatchLength, candidateRuleId, state.pendingMatchLength))
        {
            state.bestRuleId = candidateRuleId;
            state.bestMatchLength = state.pendingMatchLength;
            state.bestOffset = state.pendingOffset;

            return true;
        }

        return false;
    }

    public void StepAutomata(ref PendingMatchScanner<TextAutomataState>.ScannerState state, char ch, ref int ruleId, ref int matchLength)
    {
        var custom = state.automataState;
        var plan = custom.plan;
        var rules = plan.Rules;
        var selection = plan.selection;
        var bestRuleId = ruleId;
        var bestMatchLength = matchLength;
        var processed = state.index;

        var ordinalAutomaton = plan.ordinalAutomaton;

        if (ordinalAutomaton is not null)
        {
            custom.ordinalState = ordinalAutomaton.Step(custom.ordinalState, ch);
            var candidateRuleId = ordinalAutomaton.GetBestRuleId(custom.ordinalState);

            if (candidateRuleId >= 0)
            {
                var candidateLength = rules[candidateRuleId].FixedLength;

                if (ShouldPrefer(rules, selection, bestRuleId, bestMatchLength, candidateRuleId, candidateLength))
                {
                    bestRuleId = candidateRuleId;
                    bestMatchLength = candidateLength;
                }
            }
        }

        var ordinalIgnoreCaseAutomaton = plan.ordinalIgnoreCaseAutomaton;

        if (ordinalIgnoreCaseAutomaton is not null)
        {
            var folded = FoldCharOrdinalIgnoreCase(ch);
            custom.ordinalIgnoreCaseState = ordinalIgnoreCaseAutomaton.Step(custom.ordinalIgnoreCaseState, folded);
            var candidateRuleId = ordinalIgnoreCaseAutomaton.GetBestRuleId(custom.ordinalIgnoreCaseState);

            if (candidateRuleId >= 0)
            {
                var candidateLength = rules[candidateRuleId].FixedLength;

                if (ShouldPrefer(rules, selection, bestRuleId, bestMatchLength, candidateRuleId, candidateLength))
                {
                    bestRuleId = candidateRuleId;
                    bestMatchLength = candidateLength;
                }
            }
        }

        if (processed != 0 && (plan.regexRules.Length != 0 || plan.customRules.Length != 0))
        {
            var buffer = state.pendingBuffer;
            var start = state.pendingStart;

            foreach (var rr in plan.regexRules)
            {
                var tailLen = processed < rr.MaxMatchLength ? processed : rr.MaxMatchLength;

                if (tailLen == 0)
                    continue;

                var tailSpan = buffer.AsSpan(start + processed - tailLen, tailLen);

                var bestLen = 0;

                foreach (var m in rr.Regex.EnumerateMatches(tailSpan))
                {
                    if (m.Length != 0 && m.Index + m.Length == tailLen && m.Length > bestLen)
                        bestLen = m.Length;
                }

                if (bestLen != 0 && ShouldPrefer(rules, selection, bestRuleId, bestMatchLength, rr.RuleId, bestLen))
                {
                    bestRuleId = rr.RuleId;
                    bestMatchLength = bestLen;
                }
            }

            foreach (var cr in plan.customRules)
            {
                var tailLen = processed < cr.MaxMatchLength ? processed : cr.MaxMatchLength;

                if (tailLen == 0)
                    continue;

                var tailSpan = buffer.AsSpan(start + processed - tailLen, tailLen);
                var candidateLength = cr.Matcher(tailSpan);

                if (candidateLength > 0
                    && candidateLength <= tailLen
                    && ShouldPrefer(rules, selection, bestRuleId, bestMatchLength, cr.RuleId, candidateLength))
                {
                    bestRuleId = cr.RuleId;
                    bestMatchLength = candidateLength;
                }
            }
        }

        state.automataState = custom;
        ruleId = bestRuleId;
        matchLength = bestMatchLength;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool ShouldPrefer(
        TextRewriteRuleEntry[] rules,
        MatchSelection selection,
        int bestRuleId,
        int bestLength,
        int candidateRuleId,
        int candidateLength)
    {
        if (candidateRuleId < 0)
            return false;

        if (bestRuleId < 0)
            return true;

        var candidateRule = rules[candidateRuleId];
        var bestRule = rules[bestRuleId];

        if (selection == MatchSelection.LongestThenPriority)
        {
            if (candidateLength > bestLength)
                return true;

            if (candidateLength < bestLength)
                return false;
        }
        else
        {
            if (candidateRule.Priority < bestRule.Priority)
                return true;

            if (candidateRule.Priority > bestRule.Priority)
                return false;
        }

        if (candidateRule.Priority < bestRule.Priority)
            return true;

        if (candidateRule.Priority > bestRule.Priority)
            return false;

        if (candidateRule.Order < bestRule.Order)
            return true;

        if (candidateRule.Order > bestRule.Order)
            return false;

        return candidateLength > bestLength;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static char FoldCharOrdinalIgnoreCase(char c)
    {
        if ((uint)(c - 'a') <= (uint)('z' - 'a'))
            return (char)(c - 32);

        return char.ToUpperInvariant(c);
    }
}
