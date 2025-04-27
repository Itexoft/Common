// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.TerminalKit;

/// <summary>
/// Dialog used for editing metadata for a selected item.
/// </summary>
[TerminalComponent(nameof(TerminalMetadataForm))]
public sealed class TerminalMetadataForm : TerminalFormComponent
{
    /// <summary>
    /// Gets the collection of form fields rendered inside the dialog.
    /// </summary>
    public IReadOnlyList<TerminalFormFieldDefinition> Fields { get; init; } = [];

    /// <summary>
    /// Gets an optional expression controlling whether the dialog is visible.
    /// </summary>
    public string? VisibilityExpression { get; init; }
}