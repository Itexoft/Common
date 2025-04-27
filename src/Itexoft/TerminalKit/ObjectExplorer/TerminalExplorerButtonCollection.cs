// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Collections.ObjectModel;

namespace Itexoft.TerminalKit.ObjectExplorer;

/// <summary>
/// Mutable collection of buttons displayed by the explorer command bar.
/// </summary>
public sealed class TerminalExplorerButtonCollection : Collection<TerminalExplorerButton>
{
    private readonly TerminalObjectExplorer _owner;

    internal TerminalExplorerButtonCollection(TerminalObjectExplorer owner) =>
        this._owner = owner ?? throw new ArgumentNullException(nameof(owner));

    /// <summary>
    /// Adds a button backed by a simple synchronous action.
    /// </summary>
    /// <param name="label">Text displayed in the command bar.</param>
    /// <param name="key">Shortcut key used to trigger the action.</param>
    /// <param name="handler">Callback executed when the button is pressed.</param>
    /// <param name="isVisible">Optional predicate controlling whether the button is rendered.</param>
    public TerminalExplorerButton Add(
        string label,
        ConsoleKey key,
        Action<TerminalObjectExplorer> handler,
        Func<TerminalObjectExplorer, bool>? isVisible = null)
    {
        ArgumentNullException.ThrowIfNull(handler);

        return this.AddInternal(
            label,
            key,
            (explorer, _) =>
            {
                handler(explorer);

                return TerminalExplorerButtonResult.Continue;
            },
            isVisible != null ? (explorer, _) => isVisible(explorer) : null);
    }

    /// <summary>
    /// Adds a button backed by a delegate that returns a continuation instruction.
    /// </summary>
    /// <param name="label">Text displayed in the command bar.</param>
    /// <param name="key">Shortcut key used to trigger the action.</param>
    /// <param name="handler">Callback that returns the continuation directive.</param>
    /// <param name="isVisible">Optional predicate controlling whether the button is rendered.</param>
    public TerminalExplorerButton Add(
        string label,
        ConsoleKey key,
        Func<TerminalObjectExplorer, TerminalExplorerButtonResult> handler,
        Func<TerminalObjectExplorer, bool>? isVisible = null)
    {
        ArgumentNullException.ThrowIfNull(handler);

        return this.AddInternal(
            label,
            key,
            (explorer, _) => handler(explorer),
            isVisible != null ? (explorer, _) => isVisible(explorer) : null);
    }

    internal TerminalExplorerButton Add(
        string label,
        ConsoleKey key,
        Func<TerminalObjectExplorer, TerminalExplorerSession, TerminalExplorerButtonResult> handler,
        Func<TerminalObjectExplorer, TerminalExplorerSession, bool>? isVisible = null)
    {
        ArgumentNullException.ThrowIfNull(handler);

        return this.AddInternal(label, key, handler, isVisible);
    }

    /// <summary>
    /// Adds a standard quit button that disposes the explorer.
    /// </summary>
    /// <param name="label">Custom label, defaults to <c>Quit</c>.</param>
    /// <param name="key">Shortcut key (defaults to Q).</param>
    public TerminalExplorerButton AddQuit(string? label = null, ConsoleKey key = ConsoleKey.Q)
    {
        var button = new TerminalExplorerButton(
            label ?? "Quit",
            key,
            (explorer, _) =>
            {
                explorer.Dispose();

                return TerminalExplorerButtonResult.Exit;
            });
        this.Add(button);

        return button;
    }

    private TerminalExplorerButton AddInternal(
        string label,
        ConsoleKey key,
        Func<TerminalObjectExplorer, TerminalExplorerSession, TerminalExplorerButtonResult> handler,
        Func<TerminalObjectExplorer, TerminalExplorerSession, bool>? isVisible)
    {
        var button = new TerminalExplorerButton(label, key, handler, isVisible);
        this.Add(button);

        return button;
    }
}