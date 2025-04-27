// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.TerminalKit;

/// <summary>
/// Maps specific column values to cell styles.
/// </summary>
public sealed class TerminalCellStyleRule
{
    /// <summary>
    /// Gets the column key this rule applies to.
    /// </summary>
    public DataBindingKey Column { get; init; } = DataBindingKey.Empty;

    /// <summary>
    /// Gets the map of values to the styles that should be applied.
    /// </summary>
    public IReadOnlyDictionary<string, TerminalCellStyle> ValueStyles { get; init; } =
        new Dictionary<string, TerminalCellStyle>(StringComparer.OrdinalIgnoreCase);
}