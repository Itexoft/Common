// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Threading;

namespace Itexoft.Tests.Threading.Core;

public sealed class CancelTokenTests
{
    [Test]
    public void Cancel_CancelsToken()
    {
        var token = new CancelToken(new object());

        token.Cancel();

        Assert.That(token.IsRequested, Is.True);
        //Assert.That(token.TryGetSource(out _), Is.False);
    }

    [Test]
    public void ThrowIf_ThrowsWhenCancelled()
    {
        var token = new CancelToken(new object());
        token.Cancel();

        Assert.That(() => token.ThrowIf(), Throws.InstanceOf<OperationCanceledException>());
    }

    [Test]
    public void LinkTimeout_Triggers()
    {
        var token = new CancelToken(new object()).Branch(TimeSpan.FromMilliseconds(50));

        Assert.That(SpinWait.SpinUntil(() => token.IsRequested, TimeSpan.FromSeconds(1)), Is.True);
    }

    [Test]
    public void ImplicitCancellationToken_ReflectsTimeout()
    {
        var cancelToken = new CancelToken(new object()).Branch(TimeSpan.FromMilliseconds(50));
        using (cancelToken.Bridge(out var token))
            Assert.That(SpinWait.SpinUntil(() => token.IsCancellationRequested, TimeSpan.FromSeconds(1)), Is.True);
    }
}
