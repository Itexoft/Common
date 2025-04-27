// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Net.Core;
using Itexoft.Net.Proxies;

namespace Itexoft.Tests.Net.Proxies;

public sealed class NetHttpsProxyTests
{
    [Test]
    public async Task ConnectAsync_HandlesTlsAndConnect()
    {
        var server = await ProxyTestServers.StartHttpsAsync().ConfigureAwait(false);
        await using var server1 = server.ConfigureAwait(false);
        var proxy = new NetHttpsProxy("127.0.0.1", server.EndPoint.Port, new("user", "pass"));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await new NetTcpConnector(proxy).ConnectAsync(("example.com", 443), cts.Token).ConfigureAwait(false);

        var request = server.LastRequest ?? string.Empty;
        StringAssert.StartsWith("CONNECT example.com:443 HTTP/1.1\r\nHost: example.com\r\n", request);
        StringAssert.Contains("Proxy-Authorization: Basic", request);
    }

    [Test]
    public void ConnectAsync_ThrowsOnProxyError()
    {
        var response = "HTTP/1.1 502 Bad Gateway\r\n\r\n";

        Assert.That(
            async () =>
            {
                var server = await ProxyTestServers.StartHttpsAsync(_ => response).ConfigureAwait(false);
                await using var server1 = server.ConfigureAwait(false);
                var proxy = new NetHttpsProxy("127.0.0.1", server.EndPoint.Port);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

                await new NetTcpConnector(proxy).ConnectAsync(("example.com", 443), cts.Token).ConfigureAwait(false);
            },
            Throws.InstanceOf<IOException>());
    }
}
