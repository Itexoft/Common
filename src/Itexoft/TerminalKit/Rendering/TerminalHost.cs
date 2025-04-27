// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Core;
using Itexoft.Extensions;
using Itexoft.Threading;

namespace Itexoft.TerminalKit.Rendering;

/// <summary>
/// Hosts a console UI snapshot with automatic resize detection and key dispatch.
/// </summary>
internal sealed class TerminalHost : IDisposable
{
    private readonly Func<TerminalKeyBindingMap> bindingsFactory;
    private readonly TerminalResizeWatcher resizeWatcher;
    private readonly Func<TerminalSnapshot> snapshotFactory;
    private bool alternateBufferActive;
    private TerminalKeyBindingMap? bindings;
    private Disposed disposed;
    private volatile bool pendingRender = true;

    public TerminalHost(Func<TerminalSnapshot> snapshotFactory, Func<TerminalKeyBindingMap> bindingsFactory, TimeSpan? resizePollingInterval = null)
    {
        this.snapshotFactory = snapshotFactory ?? throw new ArgumentNullException(nameof(snapshotFactory));
        this.bindingsFactory = bindingsFactory ?? throw new ArgumentNullException(nameof(bindingsFactory));
        this.resizeWatcher = new(resizePollingInterval);

        this.resizeWatcher.Resized += (_, _) =>
        {
            this.pendingRender = true;
            this.SyncBufferSize();
        };

        this.EnterAlternateBuffer();
    }

    public void Dispose()
    {
        if (this.disposed.Enter())
            return;

        this.resizeWatcher.Dispose();
        this.LeaveAlternateBuffer();
    }

    public void Run(CancelToken cancelToken = default)
    {
        var dispatcher = TerminalDispatcher.Install(this.Invalidate, this.RunExternalScope);

        try
        {
            this.bindings = this.bindingsFactory();
            this.bindings.SetAfterInvoke(this.Invalidate);

            while (!cancelToken.IsRequested)
            {
                if (this.pendingRender)
                {
                    this.pendingRender = false;
                    this.RenderFrame();

                    continue;
                }

                if (!Console.KeyAvailable)
                {
                    Thread.Sleep(32);

                    continue;
                }

                var keyInfo = Console.ReadKey(intercept: true);

                if (this.bindings.TryHandle(keyInfo))
                    break;
            }
        }
        finally
        {
            TerminalDispatcher.Remove(dispatcher);
        }
    }

    private void Invalidate() => this.pendingRender = true;

    private void RenderFrame()
    {
        this.SyncBufferSize();
        var snapshot = this.snapshotFactory();
        new TerminalSnapshotRenderer(snapshot).Render();
    }

    private void EnterAlternateBuffer()
    {
        if (this.alternateBufferActive)
            return;

        try
        {
            Console.Write("\u001b[?1049h\u001b[?25l");
            this.alternateBufferActive = true;
            this.SyncBufferSize();
        }
        catch
        {
            this.alternateBufferActive = false;
        }
    }

    private void LeaveAlternateBuffer()
    {
        if (!this.alternateBufferActive)
            return;

        try
        {
            Console.Write("\u001b[?1049l\u001b[?25h");
            Console.ResetColor();
        }
        catch { }
        finally
        {
            this.alternateBufferActive = false;
        }
    }

    private object? RunExternalScope(Func<object?> operation)
    {
        operation.Required();
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
        if (!this.alternateBufferActive)
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
