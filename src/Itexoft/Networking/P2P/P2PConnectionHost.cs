// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using System.Threading.Channels;
using Itexoft.Networking.P2P.Adapters;

namespace Itexoft.Networking.P2P;

public sealed class P2PConnectionHost : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private readonly ConcurrentDictionary<Guid, P2PTransportConnection> _activeConnections = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly TcpP2PTransportListener _listener;

    private readonly P2PConnectionHostOptions _options;
    private Task? _acceptLoopTask;

    public P2PConnectionHost(P2PConnectionHostOptions options)
    {
        this._options = options ?? throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(this._options.NodeId))
        {
            throw new ArgumentException("NodeId must be specified.", nameof(options));
        }

        this._listener = new TcpP2PTransportListener(this._options.TransportListenerOptions);
    }

    public EndPoint? LocalEndPoint => this._listener.LocalEndPoint;

    public async ValueTask DisposeAsync()
    {
        await this.StopAsync().ConfigureAwait(false);
        this._cts.Dispose();
        await this._listener.DisposeAsync().ConfigureAwait(false);
    }

    public event Func<P2PConnectionAcceptedContext, Task>? ConnectionAccepted;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await this._listener.StartAsync(cancellationToken).ConfigureAwait(false);
        this._acceptLoopTask = Task.Run(() => this.AcceptLoopAsync(this._cts.Token), CancellationToken.None);
    }

    public async Task StopAsync()
    {
        this._cts.Cancel();
        await this._listener.StopAsync().ConfigureAwait(false);

        if (this._acceptLoopTask is not null)
        {
            try
            {
                await this._acceptLoopTask.ConfigureAwait(false);
            }
            catch
            {
                // ignored
            }
        }

        foreach (var pair in this._activeConnections)
        {
            await pair.Value.DisposeAsync().ConfigureAwait(false);
        }

        this._activeConnections.Clear();
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            P2PTransportConnection connection;
            try
            {
                connection = await this._listener.AcceptAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ChannelClosedException)
            {
                break;
            }

            _ = this.HandleConnectionAsync(connection, cancellationToken);
        }
    }

    private async Task HandleConnectionAsync(P2PTransportConnection connection, CancellationToken cancellationToken)
    {
        var controlChannel = connection.ControlChannel;
        try
        {
            var clientHello = await WaitForClientHelloAsync(controlChannel, cancellationToken).ConfigureAwait(false);
            var serverSessionId = Guid.NewGuid().ToString("N");

            var response = new P2PHandshakeEnvelope
            {
                Type = P2PHandshakeMessageType.ServerHello,
                NodeId = this._options.NodeId,
                SessionId = serverSessionId,
                SessionEpoch = 0,
                Metadata = this._options.Metadata,
                TimestampTicks = DateTimeOffset.UtcNow.UtcTicks
            };

            var payload = JsonSerializer.SerializeToUtf8Bytes(response, JsonOptions);
            var diagnosticFrame = new P2PControlFrame(
                P2PControlFrameType.Diagnostics,
                DateTimeOffset.UtcNow.UtcTicks,
                1,
                payload);
            await controlChannel.SendAsync(diagnosticFrame, cancellationToken).ConfigureAwait(false);
            await controlChannel.FlushAsync(cancellationToken).ConfigureAwait(false);

            var sessionId = Guid.NewGuid();
            if (!this._activeConnections.TryAdd(sessionId, connection))
            {
                await connection.DisposeAsync().ConfigureAwait(false);

                return;
            }

            _ = Task.Run(() => this.ControlLoopAsync(sessionId, connection, cancellationToken), CancellationToken.None);

            var handler = this.ConnectionAccepted;
            if (handler is not null)
            {
                var context = new P2PConnectionAcceptedContext(
                    connection,
                    clientHello.NodeId ?? "unknown",
                    clientHello.Metadata,
                    clientHello.SessionId,
                    response.SessionId);
                foreach (Func<P2PConnectionAcceptedContext, Task> single in handler.GetInvocationList())
                {
                    await single(context).ConfigureAwait(false);
                }
            }
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task ControlLoopAsync(Guid sessionId, P2PTransportConnection connection, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var frame in connection.ControlChannel.ReadFramesAsync(cancellationToken))
            {
                if (frame.Type == P2PControlFrameType.Ping)
                {
                    var pong = P2PControlFrame.Pong(frame.TimestampUtcTicks, frame.Sequence, ReadOnlyMemory<byte>.Empty);
                    await connection.ControlChannel.SendAsync(pong, cancellationToken).ConfigureAwait(false);
                    await connection.ControlChannel.FlushAsync(cancellationToken).ConfigureAwait(false);
                }
                else if (frame.Type == P2PControlFrameType.Shutdown)
                {
                    break;
                }
            }
        }
        catch
        {
            // ignore loop errors to ensure cleanup below
        }
        finally
        {
            if (this._activeConnections.TryRemove(sessionId, out var removed))
            {
                await removed.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private static async Task<P2PHandshakeEnvelope> WaitForClientHelloAsync(
        IP2PControlChannel controlChannel,
        CancellationToken cancellationToken)
    {
        await foreach (var frame in controlChannel.ReadFramesAsync(cancellationToken))
        {
            if (frame.Type != P2PControlFrameType.Diagnostics)
            {
                continue;
            }

            var envelope = JsonSerializer.Deserialize<P2PHandshakeEnvelope>(frame.Payload.Span, JsonOptions);
            if (envelope is null)
            {
                continue;
            }

            if (envelope.Type != P2PHandshakeMessageType.ClientHello)
            {
                continue;
            }

            return envelope;
        }

        throw new InvalidOperationException("Client did not send a ClientHello envelope.");
    }
}

public sealed class P2PConnectionHostOptions
{
    public string NodeId { get; set; } = Environment.MachineName;
    public TcpP2PTransportListenerOptions TransportListenerOptions { get; set; } = new();
    public IReadOnlyDictionary<string, string>? Metadata { get; set; }
}

public sealed class P2PConnectionAcceptedContext
{
    public P2PConnectionAcceptedContext(
        P2PTransportConnection connection,
        string remoteNodeId,
        IReadOnlyDictionary<string, string>? metadata,
        string? remoteSessionId,
        string? localSessionId)
    {
        this.Connection = connection;
        this.RemoteNodeId = remoteNodeId;
        this.Metadata = metadata;
        this.RemoteSessionId = remoteSessionId;
        this.LocalSessionId = localSessionId;
    }

    public P2PTransportConnection Connection { get; }
    public string RemoteNodeId { get; }
    public IReadOnlyDictionary<string, string>? Metadata { get; }
    public string? RemoteSessionId { get; }
    public string? LocalSessionId { get; }
}