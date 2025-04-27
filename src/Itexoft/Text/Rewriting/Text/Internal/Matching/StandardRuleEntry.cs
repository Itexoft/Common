// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Text.Rewriting.Primitives;

namespace Itexoft.Text.Rewriting.Text.Internal.Matching;

internal sealed class StandardRuleEntry(
    MatchAction action,
    int priority,
    int fixedLength,
    int maxMatchLength,
    string? replacement,
    ReplacementFactory? replacementFactory,
    ReplacementFactoryWithContext? replacementFactoryWithContext,
    ReplacementFactoryAsync? replacementFactoryAsync,
    ReplacementFactoryWithContextAsync? replacementFactoryWithContextAsync,
    MatchHandler? onMatch,
    MatchHandlerAsync? onMatchAsync) : TextRewriteRuleEntry(action, priority, fixedLength, maxMatchLength)
{
    public override string? Replacement => replacement;
    public override ReplacementFactory? ReplacementFactory => replacementFactory;
    public override ReplacementFactoryWithContext? ReplacementFactoryWithContext => replacementFactoryWithContext;
    public override ReplacementFactoryAsync? ReplacementFactoryAsync => replacementFactoryAsync;
    public override ReplacementFactoryWithContextAsync? ReplacementFactoryWithContextAsync => replacementFactoryWithContextAsync;
    public override MatchHandler? OnMatch => onMatch;
    public override MatchHandlerAsync? OnMatchAsync => onMatchAsync;
}
