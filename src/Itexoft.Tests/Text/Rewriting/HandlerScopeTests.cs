// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Text.Rewriting.Internal.Runtime;

namespace Itexoft.Tests.Text.Rewriting;

public sealed class HandlerScopeTests
{
    [Test]
    public void PushRestoresPreviousValueOnDispose()
    {
        var scope = new HandlerScope<string>();
        Assert.That(scope.Current, Is.Null);

        using (scope.Push("first"))
        {
            Assert.That(scope.Current, Is.EqualTo("first"));

            using (scope.Push("second"))
                Assert.That(scope.Current, Is.EqualTo("second"));

            Assert.That(scope.Current, Is.EqualTo("first"));
        }

        Assert.That(scope.Current, Is.Null);
    }

    [Test]
    public async Task PushIsAsyncLocalIsolatedAcrossTasks()
    {
        var scope = new HandlerScope<string>();

        using var _ = scope.Push("root");

        var task1 = Task.Run(async () =>
        {
            using var inner = scope.Push("task1");
            await Task.Yield();

            return scope.Current;
        });

        var task2 = Task.Run(async () =>
        {
            await Task.Yield();

            return scope.Current;
        });

        var results = await Task.WhenAll(task1, task2).ConfigureAwait(false);

        Assert.That(results[0], Is.EqualTo("task1"));
        Assert.That(results[1], Is.EqualTo("root"));
        Assert.That(scope.Current, Is.EqualTo("root"));
    }
}
