// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Text.Rewriting.Primitives;

namespace Itexoft.Text.Rewriting.Text.Dsl;

/// <summary>
/// Options that control how a text rewrite plan is compiled.
/// </summary>
public sealed record TextCompileOptions
{
    /// <summary>
    /// Strategy used to resolve overlapping matches.
    /// </summary>
    public MatchSelection MatchSelection { get; init; } = MatchSelection.LongestThenPriority;
}
