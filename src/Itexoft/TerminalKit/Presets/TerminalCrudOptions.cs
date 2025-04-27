// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.TerminalKit.Presets;

/// <summary>
/// Configures identifiers and text resources for the CRUD preset.
/// </summary>
public sealed class TerminalCrudOptions
{
    /// <summary>
    /// Gets or sets the component identifier assigned to the screen.
    /// </summary>
    public string ScreenId { get; set; } = "console.crud.screen";

    /// <summary>
    /// Gets or sets the title shown in the header.
    /// </summary>
    public string Title { get; set; } = "CRUD Screen";

    /// <summary>
    /// Gets or sets the theme identifier applied to the screen.
    /// </summary>
    public string Theme { get; set; } = "default.dark";

    /// <summary>
    /// Gets or sets the state slice name that stores the data items.
    /// </summary>
    public string ItemsStateName { get; set; } = "Items";

    /// <summary>
    /// Gets or sets the state slice name holding selection metadata.
    /// </summary>
    public string SelectionStateName { get; set; } = "Selection";

    /// <summary>
    /// Gets or sets options that drive scrollable window behavior.
    /// </summary>
    public TerminalScrollableOptions ScrollableWindow { get; set; } = new();

    /// <summary>
    /// Gets or sets the placeholder text rendered when no items are present.
    /// </summary>
    public string EmptyStateText { get; set; } = "No items yet";

    /// <summary>
    /// Gets or sets the list item template identifier.
    /// </summary>
    public string ListItemTemplate { get; set; } = "templates/list-row";
}
