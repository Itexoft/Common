// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.TerminalKit;

/// <summary>
/// Controls how collection components handle overflow.
/// </summary>
public sealed class ViewportScrollPolicy
{
    /// <summary>
    /// Gets a value indicating whether horizontal scrolling is allowed.
    /// </summary>
    public bool EnableHorizontal { get; init; }

    /// <summary>
    /// Gets a value indicating whether vertical scrolling is allowed.
    /// </summary>
    public bool EnableVertical { get; init; } = true;

    /// <summary>
    /// Gets the default policy (vertical scrolling enabled).
    /// </summary>
    public static ViewportScrollPolicy Default { get; } = new();
}
