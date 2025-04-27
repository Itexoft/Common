// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Networking.P2P;

namespace Itexoft.Networking.Reliable;

/// <summary>
/// Represents the active TCP connection managed by <see cref="ReliableTcpClient" />.
/// </summary>
public sealed class ReliableTcpConnectionHandle
{
    private readonly P2PConnectionHandle _inner;

    /// <summary>
    /// Initializes a new wrapper over the underlying P2P connection handle.
    /// </summary>
    internal ReliableTcpConnectionHandle(P2PConnectionHandle inner)
    {
        this._inner = inner ?? throw new ArgumentNullException(nameof(inner));
        this.Endpoint = ReliableTcpEndpoint.FromP2P(inner.Endpoint);
    }

    /// <summary>
    /// Stream used for application payload.
    /// </summary>
    public Stream DataStream => this._inner.DataStream;

    /// <summary>
    /// Identifier provided by the remote node during handshake.
    /// </summary>
    public string RemoteNodeId => this._inner.RemoteNodeId;

    /// <summary>
    /// Remote session identifier assigned by the server.
    /// </summary>
    public string RemoteSessionId => this._inner.RemoteSessionId;

    /// <summary>
    /// Local session identifier.
    /// </summary>
    public string LocalSessionId => this._inner.LocalSessionId;

    /// <summary>
    /// Session epoch reflecting how many make-before-break switches occurred.
    /// </summary>
    public long SessionEpoch => this._inner.SessionEpoch;

    /// <summary>
    /// Endpoint currently in use.
    /// </summary>
    public ReliableTcpEndpoint Endpoint { get; }
}