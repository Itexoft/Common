// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.TerminalKit.ObjectExplorer;

/// <summary>
/// Describes an action button rendered at the bottom of the explorer UI.
/// </summary>
public sealed class TerminalExplorerButton
{
    private readonly Func<TerminalObjectExplorer, TerminalExplorerSession, TerminalExplorerButtonResult> _handler;
    private readonly Func<TerminalObjectExplorer, TerminalExplorerSession, bool>? _isVisible;

    internal TerminalExplorerButton(
        string label,
        ConsoleKey key,
        Func<TerminalObjectExplorer, TerminalExplorerSession, TerminalExplorerButtonResult> handler,
        Func<TerminalObjectExplorer, TerminalExplorerSession, bool>? isVisible = null)
    {
        this.Label = label ?? throw new ArgumentNullException(nameof(label));
        this.Key = key;
        this._handler = handler ?? throw new ArgumentNullException(nameof(handler));
        this._isVisible = isVisible;
    }

    /// <summary>
    /// Gets the button label shown to the user.
    /// </summary>
    public string Label { get; }

    /// <summary>
    /// Gets the shortcut key that triggers the button.
    /// </summary>
    public ConsoleKey Key { get; }

    internal TerminalExplorerButtonResult Invoke(TerminalObjectExplorer explorer, TerminalExplorerSession session) =>
        this._handler(explorer, session);

    internal bool IsVisible(TerminalObjectExplorer explorer, TerminalExplorerSession session) =>
        this._isVisible?.Invoke(explorer, session) ?? true;
}