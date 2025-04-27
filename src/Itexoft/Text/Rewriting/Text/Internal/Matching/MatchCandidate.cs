// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Text.Rewriting.Text.Internal.Matching;

internal readonly struct MatchCandidate(int ruleId, int matchLength, int priority, int order)
{
    public static MatchCandidate None => new(-1, 0, 0, 0);

    public readonly int RuleId = ruleId;
    public readonly int MatchLength = matchLength;
    public readonly int Priority = priority;
    public readonly int Order = order;

    public bool HasValue => this.RuleId >= 0;
}
