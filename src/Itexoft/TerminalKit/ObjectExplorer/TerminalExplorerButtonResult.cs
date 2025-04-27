// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.TerminalKit.ObjectExplorer;

/// <summary>
/// Defines how the explorer should proceed after a button handler runs.
/// </summary>
public enum TerminalExplorerButtonResult
{
    /// <summary>
    /// Keep the explorer open without forcing a redraw.
    /// </summary>
    Continue,

    /// <summary>
    /// Rebuild the current frame and keep running.
    /// </summary>
    Refresh,

    /// <summary>
    /// Terminate the explorer loop.
    /// </summary>
    Exit,
}
