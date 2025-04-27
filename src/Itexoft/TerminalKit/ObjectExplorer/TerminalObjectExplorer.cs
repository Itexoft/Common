// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.TerminalKit.Rendering;

namespace Itexoft.TerminalKit.ObjectExplorer;

/// <summary>
/// Hosts the reflective explorer; dispose to restore the console state.
/// </summary>
public class TerminalObjectExplorer : IDisposable
{
    private readonly string _title;
    private CancellationTokenSource? _cts;
    private bool _disposed;
    private TerminalHost? _host;
    private TerminalExplorerSession? _session;

    /// <summary>
    /// Initializes the explorer for the specified object graph.
    /// </summary>
    /// <param name="value">Root object to inspect/edit.</param>
    /// <param name="title">Optional title displayed in the header.</param>
    public TerminalObjectExplorer(object? value, string? title = null)
    {
        this.Value = value;
        this._title = string.IsNullOrWhiteSpace(title) ? value?.GetType().Name ?? "Object" : title!;
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
        if (this._disposed)
            return;

        this.DisposeHost();
        Console.ResetColor();
        Console.Clear();
        Console.CursorVisible = true;
        this._disposed = true;
    }

    /// <summary>
    /// Starts the explorer loop and blocks until the user exits or cancellation is requested.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token propagated to the render loop.</param>
    public void Show(CancellationToken cancellationToken = default)
    {
        this.ThrowIfDisposed();
        this.DisposeHost();

        this._session = new(
            this.Value,
            this._title,
            this,
            this.Buttons.ToArray());
        this._host = new(this._session.BuildSnapshot, this._session.BuildBindings);
        this._cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        this._host.Run(this._cts.Token);
    }

    private void DisposeHost()
    {
        if (this._cts != null)
        {
            this._cts.Cancel();
            this._cts.Dispose();
            this._cts = null;
        }

        if (this._host != null)
        {
            this._host.Dispose();
            this._host = null;
        }

        this._session = null;
    }

    private void ThrowIfDisposed()
    {
        if (this._disposed)
            throw new ObjectDisposedException(nameof(TerminalObjectExplorer));
    }

    /// <summary>
    /// Convenience helper that creates and shows an explorer for the specified object.
    /// </summary>
    /// <param name="target">Object to inspect.</param>
    /// <param name="title">Optional window title.</param>
    /// <param name="cancellationToken">Cancellation token propagated to <see cref="Show" />.</param>
    public static void ShowObject(object? target, string? title = null, CancellationToken cancellationToken = default)
    {
        using var explorer = new TerminalObjectExplorer(target, title);
        explorer.Show(cancellationToken);
    }
}