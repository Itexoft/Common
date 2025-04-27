// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.TerminalKit.Rendering;

/// <summary>
/// Polls console window size and notifies listeners when it changes.
/// </summary>
internal sealed class TerminalResizeWatcher : IDisposable
{
    private readonly CancellationTokenSource cts = new();
    private readonly Task watcher;

    public TerminalResizeWatcher(TimeSpan? pollInterval = null)
    {
        var interval = pollInterval ?? TimeSpan.FromMilliseconds(120);

        this.watcher = Task.Run(
            async () =>
            {
                var width = TerminalDimensions.GetWindowWidthOrDefault(0);
                var height = TerminalDimensions.GetWindowHeightOrDefault(0);

                while (!this.cts.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(interval, this.cts.Token);
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
            this.cts.Token);
    }

    public void Dispose()
    {
        this.cts.Cancel();

        try
        {
            this.watcher.Wait(TimeSpan.FromMilliseconds(200));
        }
        catch (AggregateException)
        {
            // Swallow cancellation exceptions.
        }

        this.cts.Dispose();
    }

    public event EventHandler? Resized;
}
