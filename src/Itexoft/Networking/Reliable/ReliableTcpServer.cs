// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Net;
using Itexoft.Networking.P2P;

namespace Itexoft.Networking.Reliable;

/// <summary>
/// Lightweight wrapper over the P2P host that exposes a server-friendly API.
/// </summary>
public sealed class ReliableTcpServer : IAsyncDisposable
{
    private readonly P2PConnectionHost _host;

    /// <summary>
    /// Creates a new reliable TCP server using the provided options.
    /// </summary>
    /// <param name="options">Server configuration.</param>
    public ReliableTcpServer(ReliableTcpServerOptions options)
    {
        this.Options = options ?? throw new ArgumentNullException(nameof(options));
        this._host = new(options.ToHostOptions());
        this._host.ConnectionAccepted += this.OnConnectionAcceptedAsync;
    }

    /// <summary>
    /// Effective server configuration.
    /// </summary>
    public ReliableTcpServerOptions Options { get; }

    /// <summary>
    /// Local endpoint assigned to the listener.
    /// </summary>
    public EndPoint? LocalEndPoint => this._host.LocalEndPoint;

    /// <summary>
    /// Disposes the server and releases all resources.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        this._host.ConnectionAccepted -= this.OnConnectionAcceptedAsync;
        await this._host.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Raised whenever a client connection is accepted.
    /// </summary>
    public event Func<ReliableTcpConnectionContext, Task>? ConnectionAccepted;

    /// <summary>
    /// Starts the TCP listener.
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken = default) => this._host.StartAsync(cancellationToken);

    /// <summary>
    /// Stops the TCP listener and disposes active connections.
    /// </summary>
    public Task StopAsync() => this._host.StopAsync();

    private async Task OnConnectionAcceptedAsync(P2PConnectionAcceptedContext context)
    {
        var handler = this.ConnectionAccepted;

        if (handler is null)
            return;

        var reliableContext = new ReliableTcpConnectionContext(context);
        foreach (Func<ReliableTcpConnectionContext, Task> single in handler.GetInvocationList())
            await single(reliableContext).ConfigureAwait(false);
    }
}