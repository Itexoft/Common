// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Text.Rewriting.Primitives;

/// <summary>
/// Determines how pending text is handled when flushing the writer.
/// </summary>
public enum FlushBehavior
{
    /// <summary>
    /// Flush only the safe prefix, keeping trailing characters that might complete a future match.
    /// </summary>
    PreserveMatchTail = 0,

    /// <summary>
    /// Commit all buffered content and reset matcher state.
    /// </summary>
    Commit = 1,
}
