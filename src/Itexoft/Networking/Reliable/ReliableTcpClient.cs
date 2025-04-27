// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Networking.P2P;
using Itexoft.Networking.P2P.Adapters;

namespace Itexoft.Networking.Reliable;

/// <summary>
/// High-level client that maintains a reliable TCP connection with automatic reconnection,
/// heartbeat supervision and make-before-break switching.
/// </summary>
public sealed class ReliableTcpClient : IAsyncDisposable
{
    private readonly P2PConnectionManager _manager;

    /// <summary>
    /// Creates a client that uses the built-in TCP transport adapter.
    /// </summary>
    /// <param name="options">Client configuration.</param>
    public ReliableTcpClient(ReliableTcpClientOptions options)
        : this(options, new TcpP2PTransportAdapter()) { }

    internal ReliableTcpClient(ReliableTcpClientOptions options, IP2PTransportAdapter adapter)
    {
        this.Options = options ?? throw new ArgumentNullException(nameof(options));
        this._manager = new(options.ToP2POptions(), adapter);
        this._manager.ConnectionStateChanged += this.OnStateChanged;
    }

    /// <summary>
    /// Effective client configuration.
    /// </summary>
    public ReliableTcpClientOptions Options { get; }

    /// <summary>
    /// Current connection state.
    /// </summary>
    public ReliableConnectionState State => MapState(this._manager.State);

    /// <summary>
    /// Handle for the active connection, if any.
    /// </summary>
    public ReliableTcpConnectionHandle? CurrentConnection => this._manager.CurrentConnection is null
        ? null
        : new ReliableTcpConnectionHandle(this._manager.CurrentConnection);

    /// <summary>
    /// Disposes the client and releases all resources.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        this._manager.ConnectionStateChanged -= this.OnStateChanged;
        await this._manager.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Raised whenever the connection state changes.
    /// </summary>
    public event EventHandler<ReliableConnectionStateChangedEventArgs>? ConnectionStateChanged;

    /// <summary>
    /// Establishes the connection using the configured endpoints.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Active connection handle.</returns>
    public async Task<ReliableTcpConnectionHandle> ConnectAsync(CancellationToken cancellationToken = default)
    {
        var handle = await this._manager.ConnectAsync(cancellationToken).ConfigureAwait(false);

        return new(handle);
    }

    /// <summary>
    /// Forces a make-before-break reconnection.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ReliableTcpConnectionHandle> ReconnectAsync(CancellationToken cancellationToken = default)
    {
        var handle = await this._manager.ReconnectAsync(cancellationToken).ConfigureAwait(false);

        return new(handle);
    }

    /// <summary>
    /// Terminates the connection for the specified reason.
    /// </summary>
    public Task DisconnectAsync(
        ReliableDisconnectReason reason = ReliableDisconnectReason.Manual,
        CancellationToken cancellationToken = default) => this._manager.DisconnectAsync(MapReason(reason), cancellationToken);

    /// <summary>
    /// Returns live metrics captured by the underlying connection manager.
    /// </summary>
    public ReliableConnectionMetrics GetMetrics() => new(this._manager.GetMetrics());

    private void OnStateChanged(object? sender, P2PConnectionStateChangedEventArgs args)
    {
        var mapped = new ReliableConnectionStateChangedEventArgs(
            MapState(args.PreviousState),
            MapState(args.CurrentState),
            args.Cause.ToString(),
            args.Evidence);
        this.ConnectionStateChanged?.Invoke(this, mapped);
    }

    private static ReliableConnectionState MapState(P2PConnectionState state) => state switch
    {
        P2PConnectionState.Disconnected => ReliableConnectionState.Disconnected,
        P2PConnectionState.Connecting => ReliableConnectionState.Connecting,
        P2PConnectionState.Handshake => ReliableConnectionState.Handshake,
        P2PConnectionState.Established => ReliableConnectionState.Established,
        P2PConnectionState.Degraded => ReliableConnectionState.Degraded,
        P2PConnectionState.Reconnecting => ReliableConnectionState.Reconnecting,
        P2PConnectionState.Switching => ReliableConnectionState.Switching,
        P2PConnectionState.Failed => ReliableConnectionState.Failed,
        P2PConnectionState.Disposed => ReliableConnectionState.Disposed,
        _ => ReliableConnectionState.Disconnected
    };

    private static DisconnectReason MapReason(ReliableDisconnectReason reason) => reason switch
    {
        ReliableDisconnectReason.Manual => DisconnectReason.Manual,
        ReliableDisconnectReason.Shutdown => DisconnectReason.Shutdown,
        ReliableDisconnectReason.NetworkError => DisconnectReason.FatalError,
        _ => DisconnectReason.Manual
    };
}