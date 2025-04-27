// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Extensions;
using Itexoft.Net.Dns;
using Itexoft.Threading;

namespace Itexoft.Net;

public readonly struct NetDnsHost(NetHost host, INetDnsResolver? resolver)
{
    public string Host => host;

    public ValueTask<NetIpAddress> ResolveIpAsync(CancelToken cancelToken)
    {
        if (host.TryParseIp(out var ipAddress))
            return ValueTask.FromResult(ipAddress);

        return resolver.Required().ResolveAsync(this.Host, cancelToken);
    }

    public static implicit operator NetDnsHost(string host) => new(host, null);
    public static implicit operator string(NetDnsHost host) => host.Host;
    public static implicit operator NetDnsHost(NetHost host) => new(host, null);

    public override string ToString() => host.ToString();
}
