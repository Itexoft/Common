// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Net;
using Itexoft.Net.Core;
using Itexoft.Net.Proxies;

namespace Itexoft.Tests.Net.Proxies;

public sealed class NetSocks4ProxyTests
{
    [Test]
    public async Task ConnectAsync_SendsIPv4Request()
    {
        var server = await ProxyTestServers.StartSocks4Async().ConfigureAwait(false);
        await using var server1 = server.ConfigureAwait(false);
        var proxy = new NetSocks4Proxy("127.0.0.1", server.EndPoint.Port, new("bob", string.Empty));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var destination = new NetIpEndpoint(new(10, 1, 2, 3), 1080);

        await new NetTcpConnector(proxy).ConnectAsync(destination, cts.Token).ConfigureAwait(false);

        var req = server.LastRequest ?? [];
        Assert.That(req[0], Is.EqualTo(0x04));
        Assert.That(req[1], Is.EqualTo(0x01));
        Assert.That(req[2], Is.EqualTo(0x04));
        Assert.That(req[3], Is.EqualTo(0x38)); // port 1080 big-endian
        CollectionAssert.AreEqual(destination.IpAddress.GetBytes(), req.Skip(4).Take(4).ToArray());
        var user = req.Skip(8).TakeWhile(b => b != 0).ToArray();
        Assert.That(user, Is.EqualTo("bob"u8.ToArray()));
    }

    [Test]
    public async Task ConnectAsync_SendsSocks4AForDomain()
    {
        var server = await ProxyTestServers.StartSocks4Async().ConfigureAwait(false);
        await using var server1 = server.ConfigureAwait(false);
        var proxy = new NetSocks4Proxy("127.0.0.1", server.EndPoint.Port);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        await new NetTcpConnector(proxy).ConnectAsync(("example.com", 80), cts.Token).ConfigureAwait(false);

        var req = server.LastRequest ?? [];
        Assert.That(req[4], Is.EqualTo(0x00));
        Assert.That(req[7], Is.EqualTo(0x01)); // SOCKS4a marker
        var hostBytes = req.SkipWhile((_, idx) => idx < 9).TakeWhile(b => b != 0).ToArray();
        Assert.That(hostBytes, Is.EqualTo("example.com"u8.ToArray()));
    }

    [Test]
    public void ConnectAsync_ThrowsOnServerFailure() => Assert.That(
        async () =>
        {
            var server = await ProxyTestServers.StartSocks4Async(replyCode: 0x5B).ConfigureAwait(false);
            await using var server1 = server.ConfigureAwait(false);
            var proxy = new NetSocks4Proxy("127.0.0.1", server.EndPoint.Port);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

            await new NetTcpConnector(proxy).ConnectAsync((NetIpAddress.Loopback, 80), cts.Token).ConfigureAwait(false);
        },
        Throws.InstanceOf<IOException>());
}
