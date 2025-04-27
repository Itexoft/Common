// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.TerminalKit;

/// <summary>
/// Base descriptor for list or table-like components.
/// </summary>
public abstract class TerminalCollectionComponent : TerminalContainerComponent
{
    /// <summary>
    /// Gets the binding path that resolves to the enumerable backing the view.
    /// </summary>
    public string? DataSource { get; init; }

    /// <summary>
    /// Gets the binding path for viewport metadata (offset, window size).
    /// </summary>
    public string? ViewportState { get; init; }

    /// <summary>
    /// Gets the binding path that contains the currently selected index.
    /// </summary>
    public string? SelectionState { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether the navigation summary footer should be rendered.
    /// </summary>
    public bool ShowNavigationSummary { get; init; } = true;

    /// <summary>
    /// Gets custom navigation hint text displayed under the component.
    /// </summary>
    public string? NavigationHint { get; init; }

    /// <summary>
    /// Gets the status message displayed below the collection.
    /// </summary>
    public string? StatusMessage { get; init; }
}
