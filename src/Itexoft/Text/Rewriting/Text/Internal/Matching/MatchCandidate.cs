// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Text.Rewriting.Text.Internal.Matching;

internal readonly struct MatchCandidate(int ruleId, int matchLength, int priority, int order)
{
    public static MatchCandidate None => new(-1, 0, 0, 0);

    public readonly int ruleId = ruleId;
    public readonly int matchLength = matchLength;
    public readonly int priority = priority;
    public readonly int order = order;

    public bool HasValue => this.ruleId >= 0;
}