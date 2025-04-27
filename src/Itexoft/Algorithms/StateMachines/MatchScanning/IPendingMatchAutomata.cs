// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Algorithms.StateMachines.MatchScanning;

public interface IPendingMatchAutomata<TAutomataState>
{
    void Init(ref PendingMatchScanner<TAutomataState>.ScannerState state);

    void OnPendingCandidateSelected(
        ref PendingMatchScanner<TAutomataState>.ScannerState state,
        int candidateRuleId,
        int candidateMatchLength,
        int candidateOffset);

    bool TryAcceptPendingCandidate(ref PendingMatchScanner<TAutomataState>.ScannerState state);

    void StepAutomata(ref PendingMatchScanner<TAutomataState>.ScannerState state, char ch, ref int ruleId, ref int matchLength);
}
