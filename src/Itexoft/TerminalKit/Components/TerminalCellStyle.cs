// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.TerminalKit;

/// <summary>
/// Describes foreground/background colors for console renderers.
/// </summary>
public sealed class TerminalCellStyle
{
    /// <summary>
    /// Gets the optional foreground color override.
    /// </summary>
    public ConsoleColor? Foreground { get; init; }

    /// <summary>
    /// Gets the optional background color override.
    /// </summary>
    public ConsoleColor? Background { get; init; }
}