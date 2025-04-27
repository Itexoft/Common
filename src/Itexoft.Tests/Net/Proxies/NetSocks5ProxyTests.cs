// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Text;
using Itexoft.Net;
using Itexoft.Net.Core;
using Itexoft.Net.Proxies;
using Itexoft.Threading;

namespace Itexoft.Tests.Net.Proxies;

public sealed class NetSocks5ProxyTests
{
    [Test]
    public async Task ConnectAsync_NoAuthDomainSuccess()
    {
        var server = await ProxyTestServers.StartSocks5Async(method: 0x00).ConfigureAwait(false);
        await using var server1 = server.ConfigureAwait(false);
        var proxy = new NetSocks5Proxy("127.0.0.1", server.EndPoint.Port);
        await new NetTcpConnector(proxy).ConnectAsync(("example.com", 80), CancelToken.None).ConfigureAwait(false);

        var request = server.ConnectRequest ?? [];
        Assert.That(request[0], Is.EqualTo(0x05));
        Assert.That(request[1], Is.EqualTo(0x01)); // CONNECT
        Assert.That(request[3], Is.EqualTo(0x03)); // domain
        var hostLen = request[4];
        var host = Encoding.ASCII.GetString(request, 5, hostLen);
        Assert.That(host, Is.EqualTo("example.com"));
    }

    [Test]
    public async Task ConnectAsync_WithAuthIpv4Success()
    {
        var server = await ProxyTestServers.StartSocks5Async(method: 0x02).ConfigureAwait(false);
        await using var server1 = server.ConfigureAwait(false);
        var proxy = new NetSocks5Proxy("127.0.0.1", server.EndPoint.Port, new("user", "pwd"));
        var destination = new NetEndpoint("192.0.2.10", 8080);

        await new NetTcpConnector(proxy).ConnectAsync(destination, CancelToken.None).ConfigureAwait(false);

        var auth = server.AuthRequest ?? [];
        Assert.That(auth[0], Is.EqualTo(0x01));
        Assert.That(auth[1], Is.EqualTo(4));
        Assert.That(Encoding.ASCII.GetString(auth, 2, 4), Is.EqualTo("user"));
        Assert.That(auth[6], Is.EqualTo(3));
        Assert.That(Encoding.ASCII.GetString(auth, 7, 3), Is.EqualTo("pwd"));

        var request = server.ConnectRequest ?? [];
        Assert.That(request[3], Is.EqualTo(0x01)); // IPv4
        CollectionAssert.AreEqual((await destination.ResolveAsync().ConfigureAwait(false)).IpAddress.GetBytes(), request.Skip(4).Take(4).ToArray());
    }

    [Test]
    public void ConnectAsync_ThrowsWhenAuthRequiredButMissing() => Assert.That(
        async () =>
        {
            var server = await ProxyTestServers.StartSocks5Async(method: 0x02).ConfigureAwait(false);
            await using var server1 = server.ConfigureAwait(false);
            var proxy = new NetSocks5Proxy("127.0.0.1", server.EndPoint.Port);
            await new NetTcpConnector(proxy).ConnectAsync(("example.com", 80), CancelToken.None).ConfigureAwait(false);
        },
        Throws.InstanceOf<IOException>());

    [Test]
    public void ConnectAsync_ThrowsOnConnectFailure() => Assert.That(
        async () =>
        {
            var server = await ProxyTestServers.StartSocks5Async(0x00, 0x05).ConfigureAwait(false);
            await using var server1 = server.ConfigureAwait(false);
            var proxy = new NetSocks5Proxy("127.0.0.1", server.EndPoint.Port);
            await new NetTcpConnector(proxy).ConnectAsync((NetIpAddress.Loopback, 80), CancelToken.None).ConfigureAwait(false);
        },
        Throws.InstanceOf<IOException>());
}
