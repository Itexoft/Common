// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Text.Rewriting.Primitives;

/// <summary>
/// Controls how competing matches are prioritized when overlaps occur.
/// </summary>
public enum MatchSelection
{
    /// <summary>
    /// Prefer the longest match, then resolve ties by lowest priority value.
    /// </summary>
    LongestThenPriority = 0,

    /// <summary>
    /// Prefer the lowest priority value first, then the longest match.
    /// </summary>
    PriorityThenLongest = 1,
}
