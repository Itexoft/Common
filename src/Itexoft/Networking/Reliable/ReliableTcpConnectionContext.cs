// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Networking.P2P;

namespace Itexoft.Networking.Reliable;

/// <summary>
/// Context supplied when a client connection is accepted by <see cref="ReliableTcpServer" />.
/// </summary>
public sealed class ReliableTcpConnectionContext
{
    internal ReliableTcpConnectionContext(P2PConnectionAcceptedContext inner)
    {
        this.Connection = inner.Connection;
        this.RemoteNodeId = inner.RemoteNodeId;
        this.Metadata = inner.Metadata;
        this.RemoteSessionId = inner.RemoteSessionId;
        this.LocalSessionId = inner.LocalSessionId;
    }

    /// <summary>
    /// Identifier announced by the client.
    /// </summary>
    public string RemoteNodeId { get; }

    /// <summary>
    /// Optional metadata provided by the client.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; }

    /// <summary>
    /// Session identifier supplied by the client, if any.
    /// </summary>
    public string? RemoteSessionId { get; }

    /// <summary>
    /// Session identifier assigned by the server.
    /// </summary>
    public string? LocalSessionId { get; }

    /// <summary>
    /// Stream used for application data exchange.
    /// </summary>
    public Stream DataStream => this.Connection.DataStream;

    /// <summary>
    /// Low-level transport connection for advanced scenarios.
    /// </summary>
    internal P2PTransportConnection Connection { get; }
}