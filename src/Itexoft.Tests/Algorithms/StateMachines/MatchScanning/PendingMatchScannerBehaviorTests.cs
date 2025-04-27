// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Algorithms.StateMachines.MatchScanning;

namespace Itexoft.Tests.Algorithms.StateMachines.MatchScanning;

public sealed class PendingMatchScannerBehaviorTests
{
    [Test]
    public void GateRequestThenAcceptsMatch()
    {
        var patterns = new[] { new Pattern("ab", 0) };
        var buffer = "ab".ToCharArray();

        var result = RunScanner(buffer, patterns, gateRuleId => true);

        Assert.That(result.Updates, Has.Count.EqualTo(1));
        Assert.That(result.Updates[0], Is.EqualTo((0, 2, 0)));
        Assert.That(result.FinalBest, Is.EqualTo((0, 2, 0)));
    }

    [Test]
    public void SkipsMatchWhenGateDenies()
    {
        var patterns = new[] { new Pattern("ab", 0) };
        var buffer = "ab".ToCharArray();

        var result = RunScanner(buffer, patterns, gateRuleId => false);

        Assert.That(result.Updates, Is.Empty);
        Assert.That(result.FinalBest.ruleId, Is.EqualTo(-1));
    }

    [Test]
    public void PrefersLongerOverlappingMatch()
    {
        var patterns = new[] { new Pattern("ab", 0), new Pattern("abc", 1) };
        var buffer = "abc".ToCharArray();

        var result = RunScanner(buffer, patterns, _ => true);

        Assert.That(result.Updates.Any(u => u.ruleId == 0), Is.True);
        Assert.That(result.Updates.Last(), Is.EqualTo((1, 3, 0)));
        Assert.That(result.FinalBest, Is.EqualTo((1, 3, 0)));
    }

    [Test]
    public void RespectsMinimumOffset()
    {
        var patterns = new[] { new Pattern("aa", 0) };
        var buffer = "aaaa".ToCharArray();

        var result = RunScanner(buffer, patterns, _ => true, 2);

        Assert.That(result.Updates, Has.Count.EqualTo(1));
        Assert.That(result.Updates[0].offset, Is.EqualTo(2));
        Assert.That(result.FinalBest.offset, Is.EqualTo(2));
    }

    [Test]
    public void LaterBetterCandidateUpdatesBest()
    {
        var patterns = new[] { new Pattern("ab", 0), new Pattern("bca", 1) };
        var buffer = "abca".ToCharArray();

        var result = RunScanner(buffer, patterns, _ => true);

        Assert.That(result.FinalBest, Is.EqualTo((1, 3, 1)));
        Assert.That(result.Updates.Any(u => u.ruleId == 0), Is.True);
        Assert.That(result.Updates.Any(u => u.ruleId == 1), Is.True);
    }

    private static (List<(int ruleId, int matchLength, int offset)> Updates, (int ruleId, int matchLength, int offset) FinalBest) RunScanner(
        char[] buffer,
        Pattern[] patterns,
        Func<int, bool> gateDecider,
        int minOffset = 0)
    {
        var scanner = new PendingMatchScanner<TestAutomataState>(new TestAutomata());
        var state = scanner.Create(buffer, 0, buffer.Length);
        state.automataState = new() { patterns = patterns, minOffset = minOffset };

        var gateHasValue = false;
        var gateResult = false;
        var updates = new List<(int ruleId, int matchLength, int offset)>();
        var finalBest = (-1, 0, 0);

        while (true)
        {
            scanner.Step(ref state, gateHasValue, gateResult, out var action, out var ruleId, out var matchLength, out var offset);

            if (action == PendingMatchScanner<TestAutomataState>.PendingScanAction.Completed)
            {
                finalBest = (ruleId, matchLength, offset);

                break;
            }

            if (action == PendingMatchScanner<TestAutomataState>.PendingScanAction.GateRequest)
            {
                gateResult = gateDecider(ruleId);
                gateHasValue = true;

                continue;
            }

            updates.Add((ruleId, matchLength, offset));
            gateHasValue = false;
        }

        return (updates, finalBest);
    }

    private sealed record Pattern(string Text, int RuleId);

    private struct TestAutomataState
    {
        internal Pattern[] patterns;
        internal int minOffset;
    }

    private sealed class TestAutomata : IPendingMatchAutomata<TestAutomataState>
    {
        public void Init(ref PendingMatchScanner<TestAutomataState>.ScannerState state) => state.automataState = default;

        public void OnPendingCandidateSelected(
            ref PendingMatchScanner<TestAutomataState>.ScannerState state,
            int candidateRuleId,
            int candidateMatchLength,
            int candidateOffset) { }

        public bool TryAcceptPendingCandidate(ref PendingMatchScanner<TestAutomataState>.ScannerState state)
        {
            if (state.pendingRuleId < 0 || state.pendingOffset < state.automataState.minOffset)
                return false;

            if (state.bestRuleId < 0
                || state.pendingMatchLength > state.bestMatchLength
                || (state.pendingMatchLength == state.bestMatchLength && state.pendingOffset < state.bestOffset))
            {
                state.bestRuleId = state.pendingRuleId;
                state.bestMatchLength = state.pendingMatchLength;
                state.bestOffset = state.pendingOffset;

                return true;
            }

            return false;
        }

        public void StepAutomata(ref PendingMatchScanner<TestAutomataState>.ScannerState state, char ch, ref int ruleId, ref int matchLength)
        {
            var processed = state.index;

            foreach (var pattern in state.automataState.patterns)
            {
                var len = pattern.Text.Length;

                if (len == 0 || processed < len)
                    continue;

                var offset = processed - len;
                var span = state.pendingBuffer.AsSpan(state.pendingStart + offset, len);

                if (!span.SequenceEqual(pattern.Text))
                    continue;

                if (len > matchLength)
                {
                    ruleId = pattern.RuleId;
                    matchLength = len;
                }
            }
        }
    }
}
