// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.TerminalKit;

/// <summary>
/// Scrolling list component that supports basic CRUD gestures.
/// </summary>
[TerminalComponent(nameof(TerminalListView))]
public sealed class TerminalListView : TerminalCollectionComponent
{
    /// <summary>
    /// Gets the interpolation template used to format each item.
    /// </summary>
    public string? ItemTemplate { get; init; }

    /// <summary>
    /// Gets the text displayed when the list is empty.
    /// </summary>
    public string? EmptyStateText { get; init; }
}
