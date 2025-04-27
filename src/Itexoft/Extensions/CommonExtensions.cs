// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.ExceptionServices;
using Itexoft.Threading;

namespace Itexoft.Extensions;

public static class CommonExtensions
{
    public static async Task WriteAsync(this TextWriter writer, TextReader reader, int cacheSize = 1, CancelToken cancelToken = default)
    {
        reader.Required();

        for (var buffer = new char[cacheSize]; !cancelToken.IsRequested;)
        {
            var read = await reader.ReadAsync(buffer, 0, buffer.Length);

            if (read == 0)
            {
                if (reader.Peek() == -1)
                    break;

                await Task.Yield();

                continue;
            }

            for (var i = 0; i < read; i++)
                await writer.WriteAsync(buffer[i]);
        }
    }

    extension(TimeSpan timeSpan)
    {
        public bool IsInfinite => timeSpan == Timeout.InfiniteTimeSpan;
        public bool IsZero => timeSpan == TimeSpan.Zero;

        public int TimeoutMilliseconds => timeSpan == TimeSpan.Zero ? 0 :
            timeSpan == Timeout.InfiniteTimeSpan ? Timeout.Infinite : checked((int)Math.Ceiling(timeSpan.TotalMilliseconds));

        public CancellationTokenSource CreateCancellationTokenSource() => timeSpan == Timeout.InfiniteTimeSpan ? new() : new(timeSpan);
    }

    extension(IDisposable disposable)
    {
        public bool TryDispose()
        {
            if (disposable is null)
                return false;

            try
            {
                disposable.Dispose();

                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    extension(IAsyncDisposable disposable)
    {
        public async ValueTask<bool> TryDisposeAsync(bool continueOnCapturedContext = false)
        {
            if (disposable is null)
                return false;

            try
            {
                await disposable.DisposeAsync().ConfigureAwait(continueOnCapturedContext);

                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    extension(Exception? exception)
    {
        public bool IsCancel => exception is OperationCanceledException;
        public bool IsNotCancel => exception is not OperationCanceledException;

        /// <summary>
        /// Rethrows an exception while preserving the original stack trace.
        /// </summary>
        public Exception Rethrow()
        {
            if (exception is not null)
                ExceptionDispatchInfo.Capture(exception).Throw();

            return exception!;
        }
    }

    extension(Task? task)
    {
        public async Task SuppressExceptionsAsync(bool continueOnCapturedContext = false)
        {
            if (task is null || task.IsCompleted)
                return;

            try
            {
                await task.ConfigureAwait(continueOnCapturedContext);
            }
            catch
            {
                // suppress task cancellation exceptions when stopping background loops
            }
        }
    }
}
