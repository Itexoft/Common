// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.TerminalKit.Rendering;

/// <summary>
/// Hosts a console UI snapshot with automatic resize detection and key dispatch.
/// </summary>
internal sealed class TerminalHost : IDisposable
{
    private readonly Func<TerminalKeyBindingMap> _bindingsFactory;
    private readonly TerminalResizeWatcher _resizeWatcher;
    private readonly Func<TerminalSnapshot> _snapshotFactory;
    private bool _alternateBufferActive;
    private TerminalKeyBindingMap? _bindings;
    private bool _disposed;
    private volatile bool _pendingRender = true;

    public TerminalHost(
        Func<TerminalSnapshot> snapshotFactory,
        Func<TerminalKeyBindingMap> bindingsFactory,
        TimeSpan? resizePollingInterval = null)
    {
        this._snapshotFactory = snapshotFactory ?? throw new ArgumentNullException(nameof(snapshotFactory));
        this._bindingsFactory = bindingsFactory ?? throw new ArgumentNullException(nameof(bindingsFactory));
        this._resizeWatcher = new(resizePollingInterval);
        this._resizeWatcher.Resized += (_, _) =>
        {
            this._pendingRender = true;
            this.SyncBufferSize();
        };
        this.EnterAlternateBuffer();
    }

    public void Dispose()
    {
        if (this._disposed)
            return;

        this._resizeWatcher.Dispose();
        this.LeaveAlternateBuffer();
        this._disposed = true;
    }

    public void Run(CancellationToken cancellationToken = default)
    {
        var dispatcher = TerminalDispatcher.Install(this.Invalidate, this.RunExternalScope);
        try
        {
            this._bindings = this._bindingsFactory();
            this._bindings.SetAfterInvoke(this.Invalidate);

            while (!cancellationToken.IsCancellationRequested)
            {
                if (this._pendingRender)
                {
                    this._pendingRender = false;
                    this.RenderFrame();

                    continue;
                }

                if (!Console.KeyAvailable)
                {
                    Thread.Sleep(32);

                    continue;
                }

                var keyInfo = Console.ReadKey(intercept: true);

                if (this._bindings.TryHandle(keyInfo))
                    break;
            }
        }
        finally
        {
            TerminalDispatcher.Remove(dispatcher);
        }
    }

    private void Invalidate()
    {
        this._pendingRender = true;
    }

    private void RenderFrame()
    {
        this.SyncBufferSize();
        var snapshot = this._snapshotFactory();
        new TerminalSnapshotRenderer(snapshot).Render();
    }

    private void EnterAlternateBuffer()
    {
        if (this._alternateBufferActive)
            return;

        try
        {
            Console.Write("\u001b[?1049h\u001b[?25l");
            this._alternateBufferActive = true;
            this.SyncBufferSize();
        }
        catch
        {
            this._alternateBufferActive = false;
        }
    }

    private void LeaveAlternateBuffer()
    {
        if (!this._alternateBufferActive)
            return;

        try
        {
            Console.Write("\u001b[?1049l\u001b[?25h");
            Console.ResetColor();
        }
        catch { }
        finally
        {
            this._alternateBufferActive = false;
        }
    }

    private object? RunExternalScope(Func<object?> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        this.LeaveAlternateBuffer();
        try
        {
            Console.ResetColor();
            Console.Clear();
            Console.CursorVisible = true;

            return operation();
        }
        finally
        {
            this.EnterAlternateBuffer();
            this.SyncBufferSize();
            this.Invalidate();
        }
    }

    private void SyncBufferSize()
    {
        if (!this._alternateBufferActive)
            return;

        try
        {
#pragma warning disable CA1416
            var width = TerminalDimensions.GetWindowWidthOrDefault(0);
            if (width > 0 && Console.BufferWidth != width)
                Console.BufferWidth = width;

            var height = TerminalDimensions.GetWindowHeightOrDefault(0);
            if (height > 0 && Console.BufferHeight != height)
                Console.BufferHeight = height;
#pragma warning restore CA1416
        }
        catch { }
    }
}