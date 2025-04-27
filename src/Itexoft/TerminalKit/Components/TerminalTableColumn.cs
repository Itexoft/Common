// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.TerminalKit;

/// <summary>
/// Describes a single column inside a table view.
/// </summary>
public sealed class TerminalTableColumn
{
    /// <summary>
    /// Gets the key bound to this column.
    /// </summary>
    public DataBindingKey Key { get; init; } = DataBindingKey.Empty;

    /// <summary>
    /// Gets the header text displayed to the user.
    /// </summary>
    public string Header { get; init; } = string.Empty;

    /// <summary>
    /// Gets the column width in characters.
    /// </summary>
    public int Width { get; init; } = 12;

    /// <summary>
    /// Gets an optional style override for this column.
    /// </summary>
    public TerminalCellStyle? Style { get; init; }
}
