// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.TerminalKit;

/// <summary>
/// Well-known events emitted by table views.
/// </summary>
public static class TerminalTableViewEvents
{
    /// <summary>
    /// Raised whenever a cell is edited.
    /// </summary>
    public static readonly TerminalEventKey CellEdited = TerminalEventKey.Create<TerminalTableView>("CellEdited");

    /// <summary>
    /// Raised when the user activates the current row.
    /// </summary>
    public static readonly TerminalEventKey RowActivated = TerminalEventKey.Create<TerminalTableView>("RowActivated");
}
