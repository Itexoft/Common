// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Collections.Concurrent;
using Itexoft.Extensions;
using Itexoft.Threading;

namespace Itexoft.Tests.Threading.ControlFlow;

/// <summary>
/// Utilities for multi-threaded stress testing of control flow primitives.
/// </summary>
internal static class ConcurrencyStress
{
    public static void Run(int workers, int iterationsPerWorker, Action body, TimeSpan? timeout = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(workers);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(iterationsPerWorker);
        body.Required();

        using var cts = timeout is { } t ? new CancellationTokenSource(t) : null;
        var barrier = new Barrier(workers);
        var errors = new ConcurrentQueue<Exception>();

        var tasks = Enumerable.Range(0, workers).Select(_ => Task.Run(
            () =>
            {
                barrier.SignalAndWait(cts?.Token ?? CancellationToken.None);

                for (var i = 0; i < iterationsPerWorker && cts?.IsCancellationRequested != true; i++)
                {
                    try
                    {
                        body();
                    }
                    catch (Exception ex)
                    {
                        errors.Enqueue(ex);

                        break;
                    }
                }
            },
            cts?.Token ?? CancellationToken.None)).ToArray();

        try
        {
            Task.WaitAll(tasks);
        }
        catch (AggregateException ex)
        {
            foreach (var inner in ex.InnerExceptions)
                errors.Enqueue(inner);
        }

        if (!errors.IsEmpty)
            throw new AggregateException(errors);
    }

    public static async Task RunAsync(int workers, Func<CancelToken, Task> worker, TimeSpan duration, CancellationToken cancelToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(workers);
        worker.Required();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancelToken);
        cts.CancelAfter(duration);

        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var errors = new ConcurrentQueue<Exception>();

        var tasks = Enumerable.Range(0, workers).Select(_ => Task.Run(
            async () =>
            {
                await start.Task.ConfigureAwait(false);

                while (!cts.IsCancellationRequested)
                {
                    try
                    {
                        await worker(cts.Token).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        errors.Enqueue(ex);
                        await cts.CancelAsync().ConfigureAwait(false);

                        break;
                    }
                }
            },
            CancellationToken.None)).ToArray();

        start.SetResult();
        await Task.WhenAll(tasks).ConfigureAwait(false);

        if (!errors.IsEmpty)
            throw new AggregateException(errors);
    }
}
