// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Net;
using System.Net.Sockets;

namespace Itexoft.Networking.P2P;

/// <summary>
/// Represents the transport connection created by an <see cref="IP2PTransportAdapter" />.
/// </summary>
public sealed class P2PTransportConnection : IAsyncDisposable
{
    private readonly Func<ValueTask>? _onDispose;

    public P2PTransportConnection(
        Stream dataStream,
        IP2PControlChannel controlChannel,
        EndPoint? remoteEndPoint,
        Socket? underlyingSocket,
        Func<ValueTask>? onDispose = null)
    {
        this.DataStream = dataStream ?? throw new ArgumentNullException(nameof(dataStream));
        this.ControlChannel = controlChannel ?? throw new ArgumentNullException(nameof(controlChannel));
        this.RemoteEndPoint = remoteEndPoint;
        this.UnderlyingSocket = underlyingSocket;
        this._onDispose = onDispose;
    }

    public Stream DataStream { get; }

    public IP2PControlChannel ControlChannel { get; }

    public EndPoint? RemoteEndPoint { get; }

    public Socket? UnderlyingSocket { get; }

    public CancellationToken TransportCancellationToken { get; init; }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (this._onDispose is not null)
            {
                await this._onDispose().ConfigureAwait(false);
            }
        }
        finally
        {
            await this.DataStream.DisposeAsync().ConfigureAwait(false);
            if (this.UnderlyingSocket is { } socket)
            {
                try
                {
                    socket.Dispose();
                }
                catch
                {
                    // ignore errors raised during finalisation
                }
            }
        }
    }
}