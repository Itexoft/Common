// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Net;
using Itexoft.Networking.P2P;

namespace Itexoft.Networking.Reliable;

/// <summary>
/// Describes a reachable TCP endpoint for the reliable connection client.
/// </summary>
public sealed class ReliableTcpEndpoint
{
    /// <summary>
    /// Creates a new endpoint definition.
    /// </summary>
    /// <param name="host">Host name or IP address of the server.</param>
    /// <param name="port">Port number (0-65535).</param>
    /// <param name="label">Optional label used in diagnostics and metrics.</param>
    /// <param name="priority">Happy-Eyeballs priority; lower values are dialed first.</param>
    /// <param name="useTls">Whether the transport should negotiate TLS.</param>
    /// <param name="controlPort">Optional control-channel port when the adapter requires a dedicated socket.</param>
    public ReliableTcpEndpoint(string host, int port, string? label = null, int priority = 0, bool useTls = false, int? controlPort = null)
    {
        if (string.IsNullOrWhiteSpace(host))
            throw new ArgumentException("Host must be specified.", nameof(host));

        if (port is < IPEndPoint.MinPort or > IPEndPoint.MaxPort)
            throw new ArgumentOutOfRangeException(nameof(port), port, "Port must be between 0 and 65535.");

        this.Host = host;
        this.Port = port;
        this.Label = label;
        this.Priority = priority;
        this.UseTls = useTls;
        this.ControlPort = controlPort;
    }

    /// <summary>
    /// Host name or IP address.
    /// </summary>
    public string Host { get; }

    /// <summary>
    /// Port that will be used for the data connection.
    /// </summary>
    public int Port { get; }

    /// <summary>
    /// Optional label helping to identify the endpoint in logs and metrics.
    /// </summary>
    public string? Label { get; init; }

    /// <summary>
    /// Lower numbers indicate higher priority when running hedged dials.
    /// </summary>
    public int Priority { get; init; }

    /// <summary>
    /// Indicates whether TLS should be negotiated when dialing this endpoint.
    /// </summary>
    public bool UseTls { get; init; }

    /// <summary>
    /// Optional control channel port when the adapter requires a separate socket.
    /// </summary>
    public int? ControlPort { get; init; }

    internal P2PTransportEndpoint ToP2PEndpoint() =>
        new(this.Host, this.Port, this.UseTls, this.Label, P2PTransportNames.Tcp, controlPort: this.ControlPort, priority: this.Priority);

    internal static ReliableTcpEndpoint FromP2P(P2PTransportEndpoint endpoint) =>
        new(endpoint.Host, endpoint.Port, endpoint.Label, endpoint.Priority, endpoint.UseTls, endpoint.ControlPort);
}