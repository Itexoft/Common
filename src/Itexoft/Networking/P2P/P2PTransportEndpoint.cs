// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Net;

namespace Itexoft.Networking.P2P;

/// <summary>
/// Describes a single transport candidate that can be used to establish a P2P session.
/// </summary>
public sealed class P2PTransportEndpoint
{
    public P2PTransportEndpoint(
        string host,
        int port,
        bool useTls = false,
        string? label = null,
        string transport = P2PTransportNames.Tcp,
        IReadOnlyDictionary<string, string>? parameters = null,
        int? controlPort = null,
        int priority = 0)
    {
        this.Host = string.IsNullOrWhiteSpace(host)
            ? throw new ArgumentException("Host is required", nameof(host))
            : host;

        if (port is < IPEndPoint.MinPort or > IPEndPoint.MaxPort)
            throw new ArgumentOutOfRangeException(nameof(port), port, "Port is out of range.");

        this.Port = port;
        this.UseTls = useTls;
        this.Label = label;
        this.Transport = string.IsNullOrWhiteSpace(transport) ? P2PTransportNames.Tcp : transport;
        this.Parameters = parameters;
        this.ControlPort = controlPort;
        this.Priority = priority;
    }

    /// <summary>
    /// Host name or address of the remote peer.
    /// </summary>
    public string Host { get; }

    /// <summary>
    /// Port that should be used when dialing this endpoint.
    /// </summary>
    public int Port { get; }

    /// <summary>
    /// Optional friendly label (for diagnostics).
    /// </summary>
    public string? Label { get; }

    /// <summary>
    /// Transport identifier (for example tcp, pipe, memory).
    /// </summary>
    public string Transport { get; }

    /// <summary>
    /// Optional adapter-specific parameters.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Parameters { get; }

    /// <summary>
    /// Optional control-channel port when the adapter employs a separate connection.
    /// </summary>
    public int? ControlPort { get; }

    /// <summary>
    /// Indicates whether the transport should negotiate TLS.
    /// </summary>
    public bool UseTls { get; }

    /// <summary>
    /// Happy-Eyeballs priority; lower values are dialed first.
    /// </summary>
    public int Priority { get; }

    public override string ToString()
    {
        var scheme = string.Equals(this.Transport, P2PTransportNames.Tcp, StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : $"{this.Transport}:";

        var control = this.ControlPort.HasValue && this.ControlPort.Value != this.Port
            ? $"/ctrl:{this.ControlPort.Value}"
            : string.Empty;

        var tls = this.UseTls ? "/tls" : string.Empty;
        var core = $"{scheme}{this.Host}:{this.Port}{control}{tls}";

        return this.Label is { Length: > 0 } ? $"{this.Label} ({core})" : core;
    }
}

public static class P2PTransportNames
{
    public const string Tcp = "tcp";
    public const string NamedPipe = "pipe";
    public const string InMemory = "memory";
}