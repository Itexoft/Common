// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.TerminalKit.Presets;

/// <summary>
/// Workflow handlers invoked by CRUD preset events.
/// </summary>
public sealed class TerminalCrudHandlers
{
    /// <summary>
    /// Gets the handler invoked to open the metadata editor dialog.
    /// </summary>
    public TerminalHandlerId OpenEditor { get; init; } = TerminalHandlerId.From("workflow.openMetadataDialog");

    /// <summary>
    /// Gets the handler invoked to delete the selected item.
    /// </summary>
    public TerminalHandlerId RemoveItem { get; init; } = TerminalHandlerId.From("workflow.removeItem");

    /// <summary>
    /// Gets the handler invoked when a metadata field changes inline.
    /// </summary>
    public TerminalHandlerId UpdateField { get; init; } = TerminalHandlerId.From("workflow.updateMetadataField");

    /// <summary>
    /// Gets the handler invoked to persist metadata changes.
    /// </summary>
    public TerminalHandlerId SaveMetadata { get; init; } = TerminalHandlerId.From("workflow.saveMetadata");

    /// <summary>
    /// Gets the handler invoked when closing dialogs.
    /// </summary>
    public TerminalHandlerId CloseDialog { get; init; } = TerminalHandlerId.From("workflow.closeDialog");
}