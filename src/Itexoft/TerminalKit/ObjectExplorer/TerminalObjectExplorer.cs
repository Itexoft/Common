// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Core;
using Itexoft.TerminalKit.Rendering;
using Itexoft.Threading;

namespace Itexoft.TerminalKit.ObjectExplorer;

/// <summary>
/// Hosts the reflective explorer; dispose to restore the console state.
/// </summary>
public class TerminalObjectExplorer : IDisposable
{
    private readonly string title;
    private Disposed disposed;
    private TerminalHost? host;
    private TerminalExplorerSession? session;

    /// <summary>
    /// Initializes the explorer for the specified object graph.
    /// </summary>
    /// <param name="value">Root object to inspect/edit.</param>
    /// <param name="title">Optional title displayed in the header.</param>
    public TerminalObjectExplorer(object? value, string? title = null)
    {
        this.Value = value;
        this.title = string.IsNullOrWhiteSpace(title) ? value?.GetType().Name ?? "Object" : title!;
        this.Buttons = new(this);
    }

    /// <summary>
    /// Gets the collection of buttons rendered at the bottom of the explorer.
    /// </summary>
    public TerminalExplorerButtonCollection Buttons { get; }

    /// <summary>
    /// Gets the root object being explored.
    /// </summary>
    public object? Value { get; protected set; }

    /// <summary>
    /// Stops the explorer and restores the console buffer.
    /// </summary>
    public void Dispose()
    {
        if (this.disposed.Enter())
            return;

        this.DisposeHost();
        Console.ResetColor();
        Console.Clear();
        Console.CursorVisible = true;
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Starts the explorer loop and blocks until the user exits or cancellation is requested.
    /// </summary>
    /// <param name="cancelToken">Cancellation token propagated to the render loop.</param>
    public void Show(CancelToken cancelToken = default)
    {
        this.disposed.ThrowIf();
        this.DisposeHost();

        this.session = new(this.Value, this.title, this, this.Buttons.ToArray());
        this.host = new(this.session.BuildSnapshot, this.session.BuildBindings);
        this.host.Run(cancelToken.Branch());
    }

    private void DisposeHost()
    {
        if (this.host != null)
        {
            this.host.Dispose();
            this.host = null;
        }

        this.session = null;
    }

    /// <summary>
    /// Convenience helper that creates and shows an explorer for the specified object.
    /// </summary>
    /// <param name="target">Object to inspect.</param>
    /// <param name="title">Optional window title.</param>
    /// <param name="cancelToken">Cancellation token propagated to <see cref="Show" />.</param>
    public static void ShowObject(object? target, string? title = null, CancelToken cancelToken = default)
    {
        using var explorer = new TerminalObjectExplorer(target, title);
        explorer.Show(cancelToken);
    }
}
