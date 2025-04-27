// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Text;
using Itexoft.Net.Core;
using Itexoft.Net.Proxies;
using Itexoft.Threading;

namespace Itexoft.Tests.Net.Proxies;

public class NetAutoProxyTests
{
    [Test]
    public async Task ConnectAsync_SendsConnectWithAuth()
    {
        var server = await ProxyTestServers.StartHttpAsync().ConfigureAwait(false);
        await using var server1 = server.ConfigureAwait(false);
        var proxy = new NetAutoProxy("127.0.0.1", server.EndPoint.Port, new("user", "pass"));
        await new NetTcpConnector(proxy).ConnectAsync(("example.com", 443), CancelToken.None).ConfigureAwait(false);

        var request = server.LastRequest ?? string.Empty;
        var token = Convert.ToBase64String(Encoding.ASCII.GetBytes("user:pass"));
        StringAssert.StartsWith("CONNECT example.com:443 HTTP/1.1\r\nHost: example.com\r\n", request);
        StringAssert.Contains($"Proxy-Authorization: Basic {token}", request);
        StringAssert.EndsWith("\r\n\r\n", request);
    }
}
