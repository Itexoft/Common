// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Text.Rewriting.Primitives;

/// <summary>
/// Defines how to react when a framed event exceeds configured limits.
/// </summary>
public enum OverflowBehavior
{
    /// <summary>
    /// Throws a <see cref="FormatException" />.
    /// </summary>
    Error = 0,

    /// <summary>
    /// Drops the offending frame/event.
    /// </summary>
    Drop = 1,
}
