// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.TerminalKit;

/// <summary>
/// Navigation strategies supported by the console UI runtime.
/// </summary>
public enum TerminalNavigationMode
{
    /// <summary>
    /// Standard arrow keys (Up/Down/Left/Right, PageUp/PageDown, Home/End).
    /// </summary>
    Arrow,

    /// <summary>
    /// Number keys mapped to menu slots.
    /// </summary>
    Numeric,

    /// <summary>
    /// Accelerator gestures such as CTRL+S.
    /// </summary>
    Accelerator,

    /// <summary>
    /// Any custom key handling implemented by the host.
    /// </summary>
    Custom
}