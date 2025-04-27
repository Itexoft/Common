// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Net;
using System.Net.Sockets;

namespace Itexoft.Net;

public readonly struct NetIpEndpoint(NetIpAddress ipAddress, NetPort port)
{
    public NetIpAddress IpAddress { get; } = ipAddress;
    public NetPort Port { get; } = port;

    public AddressFamily AddressFamily => this.IpAddress.AddressFamily;

    public static implicit operator IPEndPoint(NetIpEndpoint endpoint) => new(endpoint.IpAddress, endpoint.Port);
    public static implicit operator NetIpEndpoint((NetIpAddress IpAddress, NetPort Port) endpoint) => new(endpoint.IpAddress, endpoint.Port);

    public static bool TryParse(string value, out NetIpEndpoint ipEndpoint)
    {
        if (!IPEndPoint.TryParse(value, out var point))
        {
            ipEndpoint = default;

            return false;
        }
        else
        {
            ipEndpoint = new(point.Address, point.Port);

            return true;
        }
    }
}
