// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.TerminalKit.Rendering;

/// <summary>
/// Polls console window size and notifies listeners when it changes.
/// </summary>
internal sealed class TerminalResizeWatcher : IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _watcher;

    public TerminalResizeWatcher(TimeSpan? pollInterval = null)
    {
        var interval = pollInterval ?? TimeSpan.FromMilliseconds(120);
        this._watcher = Task.Run(
            async () =>
            {
                var width = TerminalDimensions.GetWindowWidthOrDefault(0);
                var height = TerminalDimensions.GetWindowHeightOrDefault(0);
                while (!this._cts.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(interval, this._cts.Token);
                    }
                    catch (TaskCanceledException)
                    {
                        break;
                    }

                    var currentWidth = TerminalDimensions.GetWindowWidthOrDefault(width);
                    var currentHeight = TerminalDimensions.GetWindowHeightOrDefault(height);
                    if (width != currentWidth || height != currentHeight)
                    {
                        width = currentWidth;
                        height = currentHeight;
                        this.Resized?.Invoke(this, EventArgs.Empty);
                    }
                }
            },
            this._cts.Token);
    }

    public void Dispose()
    {
        this._cts.Cancel();
        try
        {
            this._watcher.Wait(TimeSpan.FromMilliseconds(200));
        }
        catch (AggregateException)
        {
            // Swallow cancellation exceptions.
        }

        this._cts.Dispose();
    }

    public event EventHandler? Resized;
}