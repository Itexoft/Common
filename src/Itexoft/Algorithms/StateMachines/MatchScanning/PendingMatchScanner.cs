// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.CompilerServices;

namespace Itexoft.Algorithms.StateMachines.MatchScanning;

/// <summary>
/// Lightweight scanning state machine with a single hot loop and an external gate check.
/// </summary>
public sealed class PendingMatchScanner<TAutomataState>
{
    public enum PendingScanAction
    {
        GateRequest,
        MatchUpdated,
        Completed,
    }

    private readonly IPendingMatchAutomata<TAutomataState> automata;

    internal PendingMatchScanner(IPendingMatchAutomata<TAutomataState> automata) =>
        this.automata = automata ?? throw new ArgumentNullException(nameof(automata));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ScannerState Create(char[] pendingBuffer, int pendingStart, int pendingLength)
    {
        var state = new ScannerState
        {
            pendingBuffer = pendingBuffer,
            pendingStart = pendingStart,
            pendingLength = pendingLength,
        };

        this.Init(ref state);

        return state;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void Init(ref ScannerState state)
    {
        state.index = 0;
        state.bestRuleId = -1;
        state.bestMatchLength = 0;
        state.bestOffset = 0;

        state.pendingRuleId = -1;
        state.pendingMatchLength = 0;
        state.pendingOffset = 0;

        state.hasPendingGate = false;
        state.completed = state.pendingLength == 0;

        state.hasCachedGate = false;
        state.cachedRuleId = -1;
        state.cachedMatchLength = 0;
        state.cachedOffset = 0;
        state.cachedGateResult = false;

        this.automata.Init(ref state);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void Step(
        ref ScannerState state,
        bool gateHasValue,
        bool gateResult,
        out PendingScanAction action,
        out int ruleId,
        out int matchLength,
        out int offset)
    {
        if (state.completed)
        {
            action = PendingScanAction.Completed;
            ruleId = state.bestRuleId;
            matchLength = state.bestMatchLength;
            offset = state.bestOffset;

            return;
        }

        if (state.hasPendingGate)
        {
            if (!gateHasValue)
            {
                action = PendingScanAction.GateRequest;
                ruleId = state.pendingRuleId;
                matchLength = state.pendingMatchLength;
                offset = state.pendingOffset;

                return;
            }

            if (gateResult && this.automata.TryAcceptPendingCandidate(ref state))
            {
                state.hasPendingGate = false;
                state.hasCachedGate = true;
                state.cachedRuleId = state.pendingRuleId;
                state.cachedMatchLength = state.pendingMatchLength;
                state.cachedOffset = state.pendingOffset;
                state.cachedGateResult = true;

                action = PendingScanAction.MatchUpdated;
                ruleId = state.bestRuleId;
                matchLength = state.bestMatchLength;
                offset = state.bestOffset;

                return;
            }

            if (gateHasValue)
            {
                state.hasCachedGate = true;
                state.cachedRuleId = state.pendingRuleId;
                state.cachedMatchLength = state.pendingMatchLength;
                state.cachedOffset = state.pendingOffset;
                state.cachedGateResult = gateResult;
            }

            state.hasPendingGate = false;
        }

        while (state.index < state.pendingLength)
        {
            var i = state.index++;
            var ch = state.pendingBuffer[state.pendingStart + i];

            var candidateRuleId = -1;
            var candidateMatchLength = 0;

            this.automata.StepAutomata(ref state, ch, ref candidateRuleId, ref candidateMatchLength);

            if (candidateRuleId >= 0)
            {
                var candidateOffset = i + 1 - candidateMatchLength;

                if (state.hasCachedGate
                    && state.cachedRuleId == candidateRuleId
                    && state.cachedMatchLength == candidateMatchLength
                    && state.cachedOffset == candidateOffset)
                {
                    if (state.cachedGateResult && this.automata.TryAcceptPendingCandidate(ref state))
                    {
                        action = PendingScanAction.MatchUpdated;
                        ruleId = state.bestRuleId;
                        matchLength = state.bestMatchLength;
                        offset = state.bestOffset;

                        return;
                    }
                }
                else
                {
                    state.pendingRuleId = candidateRuleId;
                    state.pendingMatchLength = candidateMatchLength;
                    state.pendingOffset = candidateOffset;
                    state.hasPendingGate = true;

                    this.automata.OnPendingCandidateSelected(ref state, candidateRuleId, candidateMatchLength, candidateOffset);

                    action = PendingScanAction.GateRequest;
                    ruleId = candidateRuleId;
                    matchLength = candidateMatchLength;
                    offset = candidateOffset;

                    return;
                }
            }
        }

        state.completed = true;

        action = PendingScanAction.Completed;
        ruleId = state.bestRuleId;
        matchLength = state.bestMatchLength;
        offset = state.bestOffset;
    }

    public struct ScannerState
    {
        internal char[] pendingBuffer;
        internal int pendingStart;
        internal int pendingLength;

        internal int index;

        internal int bestRuleId;
        internal int bestMatchLength;
        internal int bestOffset;

        internal int pendingRuleId;
        internal int pendingMatchLength;
        internal int pendingOffset;

        internal bool hasPendingGate;
        internal bool completed;

        internal bool hasCachedGate;
        internal int cachedRuleId;
        internal int cachedMatchLength;
        internal int cachedOffset;
        internal bool cachedGateResult;

        internal TAutomataState automataState;
    }
}
