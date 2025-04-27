// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Net;
using System.Runtime.InteropServices.JavaScript;
using Itexoft.Networking.P2P;

namespace Itexoft.Networking.Reliable;

/// <summary>
/// Options controlling the TCP server used by <see cref="ReliableTcpServer" />.
/// </summary>
public sealed class ReliableTcpServerOptions
{
    /// <summary>
    /// Identifier announced to clients during the handshake.
    /// </summary>
    public string ServerId { get; init; } = Environment.MachineName;

    /// <summary>
    /// IP address the listener should bind to; defaults to <see cref="JSType.Any" />.
    /// </summary>
    public IPAddress Address { get; init; } = IPAddress.Any;

    /// <summary>
    /// Port for incoming connections; when set to 0 the system assigns a free port.
    /// </summary>
    public int Port { get; init; } = 0;

    /// <summary>
    /// Optional listen backlog.
    /// </summary>
    public int? Backlog { get; init; }

    /// <summary>
    /// Maximum interval between data/control channel pairing attempts.
    /// </summary>
    public TimeSpan SessionPairTimeout { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Optional metadata propagated to connecting clients.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }

    internal P2PConnectionHostOptions ToHostOptions() => new()
    {
        NodeId = this.ServerId,
        Metadata = this.Metadata,
        TransportListenerOptions = new()
        {
            Address = this.Address,
            Port = this.Port,
            Backlog = this.Backlog,
            SessionPairTimeout = this.SessionPairTimeout
        }
    };
}