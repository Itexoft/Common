// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.TerminalKit.Dsl;

/// <summary>
/// Fluent helper for configuring value-based table coloring.
/// </summary>
public sealed class TerminalCellStyleRuleBuilder
{
    private readonly DataBindingKey column;
    private readonly Dictionary<string, TerminalCellStyle> styles = new(StringComparer.OrdinalIgnoreCase);

    internal TerminalCellStyleRuleBuilder(DataBindingKey column) => this.column = column;

    /// <summary>
    /// Adds a style override for the specified cell value.
    /// </summary>
    public TerminalCellStyleRuleBuilder When(string value, ConsoleColor? foreground = null, ConsoleColor? background = null)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value must be provided.", nameof(value));

        this.styles[value] = new()
        {
            Foreground = foreground,
            Background = background,
        };

        return this;
    }

    internal TerminalCellStyleRule Build() => new()
    {
        Column = this.column,
        ValueStyles = this.styles,
    };
}
