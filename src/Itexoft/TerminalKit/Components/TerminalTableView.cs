// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.TerminalKit;

/// <summary>
/// Tabular view for metadata-rich rows.
/// </summary>
[TerminalComponent(nameof(TerminalTableView))]
public sealed class TerminalTableView : TerminalCollectionComponent
{
    /// <summary>
    /// Gets the set of columns rendered by the table.
    /// </summary>
    public IReadOnlyList<TerminalTableColumn> Columns { get; init; } = [];

    /// <summary>
    /// Gets the scroll policy applied to the table.
    /// </summary>
    public ViewportScrollPolicy ViewportScrollPolicy { get; init; } = ViewportScrollPolicy.Default;

    /// <summary>
    /// Gets the base style applied to all cells.
    /// </summary>
    public TerminalCellStyle? TableStyle { get; init; }

    /// <summary>
    /// Gets column-specific style overrides keyed by binding path.
    /// </summary>
    public IReadOnlyDictionary<string, TerminalCellStyle> ColumnStyles { get; init; } =
        new Dictionary<string, TerminalCellStyle>(StringComparer.Ordinal);

    /// <summary>
    /// Gets conditional formatting rules applied per value.
    /// </summary>
    public IReadOnlyList<TerminalCellStyleRule> CellStyleRules { get; init; } = [];
}
