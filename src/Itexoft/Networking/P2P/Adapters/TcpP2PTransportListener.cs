// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;

namespace Itexoft.Networking.P2P.Adapters;

public sealed class TcpP2PTransportListener : IAsyncDisposable
{
    private readonly Channel<P2PTransportConnection> _accepted;
    private readonly TcpP2PTransportListenerOptions _options;
    private readonly ConcurrentDictionary<Guid, PendingSession> _pending = new();
    private Task? _acceptLoopTask;
    private CancellationTokenSource _cts = new();

    private Socket? _listenerSocket;

    public TcpP2PTransportListener(TcpP2PTransportListenerOptions? options = null)
    {
        this._options = options ?? new TcpP2PTransportListenerOptions();
        this._accepted = Channel.CreateUnbounded<P2PTransportConnection>(
            new()
            {
                AllowSynchronousContinuations = false,
                SingleReader = false,
                SingleWriter = false
            });
    }

    public EndPoint? LocalEndPoint => this._listenerSocket?.LocalEndPoint;

    public async ValueTask DisposeAsync()
    {
        await this.StopAsync().ConfigureAwait(false);
        this._listenerSocket?.Dispose();
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (this._listenerSocket is not null)
            throw new InvalidOperationException("Listener already started.");

        var endPoint = this._options.EndPoint ?? new IPEndPoint(this._options.Address, this._options.Port);
        var socket = new Socket(endPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
        {
            NoDelay = true
        };
        socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        socket.Bind(endPoint);
        if (this._options.Backlog.HasValue && this._options.Backlog.Value > 0)
            socket.Listen(this._options.Backlog.Value);
        else
            socket.Listen();

        this._listenerSocket = socket;

        if (this._cts.IsCancellationRequested)
        {
            this._cts.Dispose();
            this._cts = new();
        }

        this._acceptLoopTask = Task.Run(() => this.AcceptLoopAsync(this._cts.Token), CancellationToken.None);
        await Task.CompletedTask.ConfigureAwait(false);
    }

    public async Task<P2PTransportConnection> AcceptAsync(CancellationToken cancellationToken = default) =>
        await this._accepted.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);

    public async Task StopAsync()
    {
        this._cts.Cancel();
        try
        {
            this._listenerSocket?.CloseGracefully();
        }
        catch
        {
            // ignored
        }

        this._listenerSocket = null;

        if (this._acceptLoopTask is not null)
            await this._acceptLoopTask.ConfigureAwait(false);

        this._accepted.Writer.TryComplete();

        while (await this._accepted.Reader.WaitToReadAsync().ConfigureAwait(false))
        while (this._accepted.Reader.TryRead(out var connection))
            await connection.DisposeAsync().ConfigureAwait(false);

        foreach (var pending in this._pending.Values)
            pending.Dispose();

        this._pending.Clear();

        this._cts.Dispose();
        this._cts = new();
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        var socket = this._listenerSocket;

        if (socket is null)
            return;

        while (!cancellationToken.IsCancellationRequested)
        {
            Socket client;
            try
            {
                client = await socket.AcceptAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException ex) when (ex.SocketErrorCode is SocketError.Interrupted
                                                 or SocketError.OperationAborted
                                                 or SocketError.InvalidArgument
                                                 or SocketError.NotSocket)
            {
                break;
            }

            _ = this.ProcessClientAsync(client, cancellationToken);
        }
    }

    private async Task ProcessClientAsync(Socket socket, CancellationToken cancellationToken)
    {
        try
        {
            var (sessionId, channel) = await TcpP2PChannelHeader.ReceiveAsync(socket, cancellationToken).ConfigureAwait(false);
            var pending = this._pending.GetOrAdd(sessionId, _ => new(sessionId));
            pending.Notify(channel, socket);

            this.CleanupStaleSessions();

            if (pending.TryBuild(out var connection))
            {
                this._pending.TryRemove(sessionId, out _);
                await this._accepted.Writer.WriteAsync(connection, cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            socket.CloseGracefully();
        }
    }

    private void CleanupStaleSessions()
    {
        if (this._options.SessionPairTimeout <= TimeSpan.Zero)
            return;

        var now = DateTimeOffset.UtcNow;
        foreach (var pair in this._pending)
            if (pair.Value.IsExpired(now, this._options.SessionPairTimeout))
                if (this._pending.TryRemove(pair.Key, out var removed))
                    removed.Dispose();
    }

    private sealed class PendingSession
    {
        private readonly DateTimeOffset _createdAt = DateTimeOffset.UtcNow;
        private readonly object _sync = new();
        private Socket? _controlSocket;
        private Socket? _dataSocket;
        private DateTimeOffset _lastUpdated = DateTimeOffset.UtcNow;

        public PendingSession(Guid sessionId)
        {
            // sessionId reserved for future diagnostics
        }

        public void Notify(TcpP2PChannel channel, Socket socket)
        {
            lock (this._sync)
            {
                if (channel == TcpP2PChannel.Data)
                {
                    this._dataSocket?.CloseGracefully();
                    this._dataSocket = socket;
                }
                else
                {
                    this._controlSocket?.CloseGracefully();
                    this._controlSocket = socket;
                }

                this._lastUpdated = DateTimeOffset.UtcNow;
            }
        }

        public bool TryBuild(out P2PTransportConnection connection)
        {
            lock (this._sync)
            {
                if (this._dataSocket is null || this._controlSocket is null)
                {
                    connection = null!;

                    return false;
                }

                var dataStream = new NetworkStream(this._dataSocket, false);
                var controlStream = new NetworkStream(this._controlSocket, false);
                var controlChannel = new SimpleStreamControlChannel(controlStream);

                async ValueTask OnDisposeAsync()
                {
                    try
                    {
                        await controlStream.DisposeAsync().ConfigureAwait(false);
                    }
                    catch
                    {
                        controlStream.Dispose();
                    }

                    this._controlSocket.CloseGracefully();
                }

                connection = new(
                    dataStream,
                    controlChannel,
                    this._dataSocket.RemoteEndPoint,
                    this._dataSocket,
                    OnDisposeAsync);
                this._dataSocket = null;
                this._controlSocket = null;

                return true;
            }
        }

        public bool IsExpired(DateTimeOffset now, TimeSpan timeout)
        {
            if (timeout <= TimeSpan.Zero)
                return false;

            return now - this._lastUpdated > timeout;
        }

        public void Dispose()
        {
            lock (this._sync)
            {
                this._dataSocket?.CloseGracefully();
                this._controlSocket?.CloseGracefully();
                this._dataSocket = null;
                this._controlSocket = null;
            }
        }
    }
}

public sealed class TcpP2PTransportListenerOptions
{
    public IPAddress Address { get; set; } = IPAddress.Loopback;
    public int Port { get; set; } = 0;
    public EndPoint? EndPoint { get; set; }
    public int? Backlog { get; set; }
    public TimeSpan SessionPairTimeout { get; set; } = TimeSpan.FromSeconds(10);
}