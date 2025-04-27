// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Text.Rewriting.Primitives;

/// <summary>
/// Snapshot of streaming rewrite metrics available to callbacks.
/// </summary>
public readonly record struct RewriteMetrics(
    long ProcessedChars,
    long MatchesApplied,
    long Replacements,
    long Removals,
    long Flushes,
    int BufferedChars);
