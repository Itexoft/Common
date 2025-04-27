// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.TerminalKit.Presets;

/// <summary>
/// Logical actions triggered by input bindings inside CRUD preset.
/// </summary>
public sealed class TerminalCrudActions
{
    /// <summary>
    /// Gets the action triggered when inserting a new record.
    /// </summary>
    public TerminalActionId CreateItem { get; init; } = TerminalActionId.From("workflow.createItem");

    /// <summary>
    /// Gets the action triggered when deleting the selected record.
    /// </summary>
    public TerminalActionId RemoveItem { get; init; } = TerminalActionId.From("workflow.removeItem");

    /// <summary>
    /// Gets the action triggered by numeric shortcuts.
    /// </summary>
    public TerminalActionId JumpToIndex { get; init; } = TerminalActionId.From("workflow.jumpToIndex");

    /// <summary>
    /// Gets the action that moves focus backward.
    /// </summary>
    public TerminalActionId FocusPrevious { get; init; } = TerminalActionId.From("workflow.focus.prev");

    /// <summary>
    /// Gets the action that moves focus forward.
    /// </summary>
    public TerminalActionId FocusNext { get; init; } = TerminalActionId.From("workflow.focus.next");
}
