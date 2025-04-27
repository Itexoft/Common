// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Threading.ControlFlow;

namespace Itexoft.Tests.Threading.ControlFlow;

public sealed class DeferredTests
{
    [Test]
    public void DeferredUtility_CachesNullResult()
    {
        var calls = 0;

        var func = DeferredUtility.CreateDelegate<string?>(() =>
        {
            Interlocked.Increment(ref calls);

            return null;
        });

        for (var i = 0; i < 20; i++)
            Assert.That(func(), Is.Null);

        Assert.That(calls, Is.EqualTo(1));
    }

    [Test]
    public void TryGetValueIfCreated_DoesNotCreate()
    {
        var calls = 0;

        var deferred = new Deferred<int>(() =>
        {
            Interlocked.Increment(ref calls);

            return 123;
        });

        Assert.That(deferred.TryGetValueIfCreated(out _), Is.False);
        Assert.That(calls, Is.EqualTo(0));
    }

    [Test]
    public void Dispose_WaitsForCreationAndCallsHandler()
    {
        var created = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var disposeCount = 0;

        var deferred = new Deferred<DisposableSpy>(
            () =>
            {
                created.Task.Wait();

                return new(() => Interlocked.Increment(ref disposeCount));
            },
            d => d.Dispose());

        var valueTask = Task.Run(() => deferred.Value);

        var disposeTask = Task.Run(() =>
        {
            SpinWait.SpinUntil(() => created.Task.IsCompleted, TimeSpan.FromMilliseconds(100));
            deferred.Dispose(out var captured);
            captured?.Dispose();
        });

        created.SetResult();
        var value = valueTask.Result;
        disposeTask.Wait();

        Assert.That(disposeCount, Is.EqualTo(1));
        Assert.That(value, Is.Not.Null);
    }

    private sealed class DisposableSpy(Action onDispose) : IDisposable
    {
        public void Dispose() => onDispose();
    }
}
