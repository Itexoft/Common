// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Itexoft.Threading;
using Itexoft.Threading.ControlFlow;

namespace Itexoft.Net.Core;

public static class NetStatus
{
    private const int refreshIntervalMilliseconds = 1000;
    private static readonly NetIpAddress ipv4Probe = NetIpAddress.Parse("192.0.2.1"); // TEST-NET-1
    private static readonly NetIpAddress ipv6Probe = NetIpAddress.Parse("2001:db8::1"); // Documentation prefix

    private static InvokeGateLatchAsync<AvailabilitySnapshot> availabilitySnapshot = InvokeGate.LatchAsync(
        static () => GetAvailabilitySnapshotAsync(CancelToken.None),
        TimeSpan.FromMilliseconds(refreshIntervalMilliseconds));

    public static bool Ipv4Available => availabilitySnapshot.Invoke().Ipv4Available;

    public static bool Ipv6Available => availabilitySnapshot.Invoke().Ipv6Available;

    public static ValueTask<bool> Ipv4AvailableAsync => availabilitySnapshot.InvokeAsync(x => x.Ipv4Available);

    public static ValueTask<bool> Ipv6AvailableAsync => availabilitySnapshot.InvokeAsync(x => x.Ipv6Available);

    private static async ValueTask<AvailabilitySnapshot> GetAvailabilitySnapshotAsync(CancelToken cancelToken)
    {
        cancelToken.ThrowIf();

        var ipv4Usable = NetSocket.OsSupportsIPv4 && await HasUsableInterfaceAsync(AddressFamily.InterNetwork, cancelToken).ConfigureAwait(false);

        var ipv6Usable = NetSocket.OsSupportsIPv6 && await HasUsableInterfaceAsync(AddressFamily.InterNetworkV6, cancelToken).ConfigureAwait(false);

        if (ipv4Usable)
            ipv4Usable = await ProbeRouteAsync(AddressFamily.InterNetwork, ipv4Probe, cancelToken).ConfigureAwait(false);

        if (ipv6Usable)
            ipv6Usable = await ProbeRouteAsync(AddressFamily.InterNetworkV6, ipv6Probe, cancelToken).ConfigureAwait(false);

        return new(ipv4Usable, ipv6Usable);
    }

    private static async ValueTask<bool> HasUsableInterfaceAsync(AddressFamily addressFamily, CancelToken cancelToken)
    {
        using (cancelToken.Bridge(out var token))
        {
            return await Task.Run(
                () =>
                {
                    try
                    {
                        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                        {
                            cancelToken.ThrowIf();

                            if (nic.OperationalStatus != OperationalStatus.Up)
                                continue;

                            if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                                continue;

                            var props = nic.GetIPProperties();
                            var hasGateway = props.GatewayAddresses.Any(g => g.Address.AddressFamily == addressFamily && IsGlobalUnicast(g.Address));

                            if (!hasGateway)
                                continue;

                            if (props.UnicastAddresses.Any(ua => ua.Address.AddressFamily == addressFamily && IsGlobalUnicast(ua.Address)))
                                return true;
                        }
                    }
                    catch
                    {
                        // Ignore enumeration errors; fallback handled by caller.
                    }

                    return false;
                },
                token).ConfigureAwait(false);
        }
    }

    private static async ValueTask<bool> ProbeRouteAsync(AddressFamily addressFamily, IPAddress target, CancelToken cancelToken)
    {
        try
        {
            await using var socket = new NetSocket(addressFamily, SocketType.Dgram, ProtocolType.Udp);
            var endpoint = new NetIpEndpoint(new(target.GetAddressBytes(), (uint)target.ScopeId), 9);
            await socket.ConnectAsync(endpoint, cancelToken).ConfigureAwait(false);

            return true;
        }
        catch (SocketException ex) when (ex.SocketErrorCode is SocketError.NetworkUnreachable
                                             or SocketError.HostUnreachable
                                             or SocketError.AddressFamilyNotSupported
                                             or SocketError.AddressNotAvailable)
        {
            return false;
        }
        catch
        {
            // On unexpected errors treat as unavailable to stay conservative.
            return false;
        }
    }

    private static bool IsGlobalUnicast(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
            return false;

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
            return !(address.IsIPv6LinkLocal || address.IsIPv6Multicast);

        return true;
    }

    private readonly struct AvailabilitySnapshot(bool ipv4Available, bool ipv6Available)
    {
        public bool Ipv4Available { get; } = ipv4Available;

        public bool Ipv6Available { get; } = ipv6Available;
    }
}
