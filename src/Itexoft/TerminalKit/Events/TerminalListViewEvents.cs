// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.TerminalKit;

/// <summary>
/// Well-known events emitted by list views.
/// </summary>
public static class TerminalListViewEvents
{
    /// <summary>
    /// Raised when the user activates the current list item.
    /// </summary>
    public static readonly TerminalEventKey ItemActivated = TerminalEventKey.Create<TerminalListView>("ItemActivated");

    /// <summary>
    /// Raised when the user requests deletion of the selected item.
    /// </summary>
    public static readonly TerminalEventKey ItemDeleted = TerminalEventKey.Create<TerminalListView>("ItemDeleted");
}