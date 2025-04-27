// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Net;
using Itexoft.Extensions;
using Itexoft.Threading;

namespace Itexoft.Net;

public readonly struct NetEndpoint
{
    public NetPort Port { get; }
    public NetDnsHost Host { get; }

    public NetEndpoint((NetDnsHost host, NetPort port) endpoint) : this(endpoint.host, endpoint.port) { }

    public NetEndpoint(NetDnsHost host, NetPort port)
    {
        this.Port = port;
        this.Host = host;
    }

    public NetEndpoint(NetIpEndpoint endpoint) : this(endpoint.IpAddress, endpoint.Port) { }

    public NetEndpoint(NetIpAddress ipAddress, NetPort port)
    {
        this.Port = port;
        this.Host = ipAddress.ToString().Required();
    }

    public static implicit operator NetEndpoint(EndPoint endpoint) => endpoint switch
    {
        IPEndPoint ipEndpoint => new(new NetIpAddress(ipEndpoint.Address.GetAddressBytes()), ipEndpoint.Port),
        DnsEndPoint dnsEndpoint => new(dnsEndpoint.Host, dnsEndpoint.Port),
        _ => default,
    };

    public static implicit operator NetEndpoint(NetIpEndpoint endpoint) => new(endpoint);
    public static implicit operator NetEndpoint((NetHost Host, NetPort Port) endpoint) => new(endpoint.Host, endpoint.Port);
    public static implicit operator NetEndpoint((NetIpAddress IpAddress, NetPort Port) endpoint) => new(endpoint.IpAddress, endpoint.Port);

    public override string ToString() => $"{this.Host}:{this.Port}";

    public bool TryCreate(out NetIpEndpoint ipEndpoint) => NetIpEndpoint.TryParse(this.ToString(), out ipEndpoint);

    public static bool TryParse(string value, out NetEndpoint ipEndpoint)
    {
        ipEndpoint = default;

        if (string.IsNullOrWhiteSpace(value))
            return false;

        var span = value.AsSpan().Trim();

        ReadOnlySpan<char> hostSpan;
        ReadOnlySpan<char> portSpan;

        if (span[0] == '[')
        {
            var endBracketIndex = span.IndexOf(']');

            if (endBracketIndex <= 1)
                return false;

            hostSpan = span[1..endBracketIndex].Trim();

            if (hostSpan.Length == 0)
                return false;

            var rest = span[(endBracketIndex + 1)..].TrimStart();

            if (rest.Length < 2 || rest[0] != ':')
                return false;

            portSpan = rest[1..].Trim();

            if (portSpan.Length == 0)
                return false;
        }
        else
        {
            var colonIndex = span.LastIndexOf(':');

            if (colonIndex <= 0 || colonIndex == span.Length - 1)
                return false;

            if (span[..colonIndex].IndexOf(':') >= 0)
                return false;

            hostSpan = span[..colonIndex].Trim();
            portSpan = span[(colonIndex + 1)..].Trim();

            if (hostSpan.Length == 0 || portSpan.Length == 0)
                return false;
        }

        if (!tryParsePort(portSpan, out var portValue))
            return false;

        ipEndpoint = new(new string(hostSpan), new(portValue));

        return true;

        static bool tryParsePort(ReadOnlySpan<char> span, out ushort portValue)
        {
            portValue = 0;

            if (span.Length is 0 or > 5)
                return false;

            uint acc = 0;

            foreach (var c in span)
            {
                var d = (uint)(c - '0');

                if (d > 9)
                    return false;

                acc = acc * 10u + d;

                if (acc > 65535u)
                    return false;
            }

            portValue = (ushort)acc;

            return true;
        }
    }

    public async Task<NetIpEndpoint> ResolveAsync(CancelToken cancelToken = default)
    {
        var result = await this.Host.ResolveIpAsync(cancelToken);

        return new(result, this.Port);
    }
}
