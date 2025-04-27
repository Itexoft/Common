// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.TerminalKit.Dsl;

/// <summary>
/// Fluent builder for command bars that sit below lists or tables.
/// </summary>
public sealed class TerminalCommandBarComposer
{
    private readonly TerminalComponentBuilder<TerminalPanel> _builder;

    internal TerminalCommandBarComposer(TerminalComponentBuilder<TerminalPanel> builder) => this._builder = builder;

    /// <summary>
    /// Gets the underlying panel handle for advanced access.
    /// </summary>
    public TerminalComponentHandle<TerminalPanel> Handle => this._builder.Handle;

    /// <summary>
    /// Adds a counter label that displays paging or selection info.
    /// </summary>
    public TerminalCommandBarComposer Counter(Func<string> textFactory)
    {
        ArgumentNullException.ThrowIfNull(textFactory);
        this._builder.AddChild<TerminalLabel>(label =>
            label.Set(l => l.Text, textFactory()));

        return this;
    }

    /// <summary>
    /// Adds a shortcut hint line with custom text.
    /// </summary>
    public TerminalCommandBarComposer Shortcut(string text)
    {
        this._builder.AddChild<TerminalShortcutHint>(hint => hint.Set(h => h.Text, text));

        return this;
    }
}