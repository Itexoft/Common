// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.TerminalKit.Presets;

/// <summary>
/// Selection metadata shared across CRUD presets.
/// </summary>
public sealed class TerminalSelectionState
{
    /// <summary>
    /// Gets or sets the identifier of the currently selected record.
    /// </summary>
    public object? ActiveItemId { get; set; }

    /// <summary>
    /// Clears the selection metadata.
    /// </summary>
    public void Clear() => this.ActiveItemId = null;
}
