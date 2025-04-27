// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Text.Rewriting.Primitives;

namespace Itexoft.Text.Rewriting.Text.Internal;

/// <summary>
/// Collects all callbacks related to a rule so builders can route through a single path.
/// </summary>
internal readonly record struct RuleCallbacks
{
    internal string? Replacement { get; init; }
    internal ReplacementFactory? ReplacementFactory { get; init; }
    internal ReplacementFactoryWithContext? ReplacementFactoryWithContext { get; init; }
    internal ReplacementFactoryAsync? ReplacementFactoryAsync { get; init; }
    internal ReplacementFactoryWithContextAsync? ReplacementFactoryWithContextAsync { get; init; }
    internal MatchHandler? OnMatch { get; init; }
    internal MatchHandlerAsync? OnMatchAsync { get; init; }
}
