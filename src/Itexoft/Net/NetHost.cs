// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Globalization;
using System.Net;
using System.Net.Sockets;
using Itexoft.Extensions;

namespace Itexoft.Net;

public readonly struct NetHost(string host, AddressFamily addressFamily = AddressFamily.Unspecified)
{
    public NetHost(NetIpAddress ipAddress) : this(ipAddress.ToString(), ipAddress.AddressFamily) { }

    private static readonly IdnMapping idnMapping = new();
    public string Host { get; } = idnMapping.GetAscii(host.RequiredNotWhiteSpace());
    public AddressFamily AddressFamily { get; } = addressFamily;

    public override string ToString() => this.Host;

    public override int GetHashCode() => HashCode.Combine(this.Host, this.AddressFamily);

    public bool TryParseIp(out NetIpAddress ip) => NetIpAddress.TryParse(this.Host, out ip);

    public static implicit operator NetHost(string host) => new(host);
    public static implicit operator string(NetHost host) => host.Host;
    public static implicit operator NetHost(DnsEndPoint host) => new(host.Host, host.AddressFamily);
    public static implicit operator NetHost(NetIpAddress ip) => new(ip);

    public static implicit operator NetHost(Uri uri) => new(
        uri.DnsSafeHost,
        uri.HostNameType switch
        {
            UriHostNameType.IPv4 => AddressFamily.InterNetwork,
            UriHostNameType.IPv6 => AddressFamily.InterNetworkV6,
            UriHostNameType.Basic => AddressFamily.Unspecified,
            UriHostNameType.Dns => AddressFamily.Unspecified,
            UriHostNameType.Unknown => AddressFamily.Unspecified,
            _ => AddressFamily.Unspecified,
        });
}
