// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Text.Rewriting.Text;
using Itexoft.Text.Rewriting.Text.Internal.Matching;
using TextPendingScanner =
    Itexoft.Algorithms.StateMachines.MatchScanning.PendingMatchScanner<Itexoft.Text.Rewriting.Text.Internal.Matching.TextAutomataState>;

namespace Itexoft.Tests.Algorithms.StateMachines.MatchScanning;

public sealed class PendingMatchScannerTests
{
    [Test]
    public void PicksLongestLiteralWhenGateAllows()
    {
        var plan = new TextRewritePlanBuilder().ReplaceLiteral("ab", "X").ReplaceLiteral("abc", "Y").Build();

        var scanner = new TextPendingScanner(new TextPendingMatchAutomata());
        var buffer = "abc".ToCharArray();
        var state = scanner.Create(buffer, 0, buffer.Length);
        state.automataState.plan = plan;
        state.automataState.minOffset = 0;
        state.automataState.ordinalState = 0;
        state.automataState.ordinalIgnoreCaseState = 0;

        var gateHasValue = false;
        var gateResult = false;

        (int ruleId, int matchLength, int offset)? update = null;

        while (true)
        {
            scanner.Step(ref state, gateHasValue, gateResult, out var action, out var ruleId, out var matchLength, out var offset);

            if (action == TextPendingScanner.PendingScanAction.Completed)
                break;

            if (action == TextPendingScanner.PendingScanAction.GateRequest)
            {
                gateResult = true;
                gateHasValue = true;

                continue;
            }

            update = (ruleId, matchLength, offset);
            gateHasValue = false;
        }

        Assert.That(update, Is.Not.Null);
        Assert.That(update?.ruleId, Is.EqualTo(1));
        Assert.That(update?.matchLength, Is.EqualTo(3));
        Assert.That(update?.offset, Is.EqualTo(0));
    }

    [Test]
    public void SkipsMatchWhenGateDenies()
    {
        var plan = new TextRewritePlanBuilder().ReplaceLiteral("ab", "X").Build();

        var scanner = new TextPendingScanner(new TextPendingMatchAutomata());
        var buffer = "ab".ToCharArray();
        var state = scanner.Create(buffer, 0, buffer.Length);
        state.automataState.plan = plan;
        state.automataState.minOffset = 0;
        state.automataState.ordinalState = 0;
        state.automataState.ordinalIgnoreCaseState = 0;

        var gateHasValue = false;
        var gateResult = false;
        var matchFound = false;

        while (true)
        {
            scanner.Step(ref state, gateHasValue, gateResult, out var action, out var ruleId, out var matchLength, out var offset);

            if (action == TextPendingScanner.PendingScanAction.Completed)
                break;

            if (action == TextPendingScanner.PendingScanAction.GateRequest)
            {
                gateResult = false;
                gateHasValue = true;

                continue;
            }

            if (action == TextPendingScanner.PendingScanAction.MatchUpdated)
                matchFound = true;
        }

        Assert.That(matchFound, Is.False);
    }

    [Test]
    public void RespectsMinOffsetWhenPickingMatch()
    {
        var plan = new TextRewritePlanBuilder().ReplaceLiteral("aa", "X").Build();

        var scanner = new TextPendingScanner(new TextPendingMatchAutomata());
        var buffer = "aaaa".ToCharArray();
        var state = scanner.Create(buffer, 0, buffer.Length);
        state.automataState.plan = plan;
        state.automataState.minOffset = 2;
        state.automataState.ordinalState = 0;
        state.automataState.ordinalIgnoreCaseState = 0;

        var gateHasValue = false;
        var gateResult = false;

        (int ruleId, int matchLength, int offset)? update = null;

        while (true)
        {
            scanner.Step(ref state, gateHasValue, gateResult, out var action, out var ruleId, out var matchLength, out var offset);

            if (action == TextPendingScanner.PendingScanAction.Completed)
                break;

            if (action == TextPendingScanner.PendingScanAction.GateRequest)
            {
                gateResult = true;
                gateHasValue = true;

                continue;
            }

            update = (ruleId, matchLength, offset);
            gateHasValue = false;
        }

        Assert.That(update, Is.Not.Null);
        Assert.That(update?.offset, Is.EqualTo(2));
    }
}
