// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Buffers.Binary;
using System.Threading.Channels;
using Itexoft.Networking.P2P;

namespace Itexoft.Networking.Reliable;

/// <summary>
/// <see cref="IReliableStreamTransport" /> implementation on top of <see cref="P2PTransportConnection" />.
/// </summary>
public sealed class P2PReliableStreamTransport : IReliableStreamTransport
{
    private const int HeaderSize = sizeof(long) + sizeof(int);

    private readonly P2PTransportConnection _connection;
    private readonly IP2PControlChannel _controlChannel;
    private readonly Task _controlLoop;
    private readonly SemaphoreSlim _controlWriteLock = new(1, 1);
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _dataLoop;
    private readonly Stream _dataStream;
    private readonly SemaphoreSlim _dataWriteLock = new(1, 1);
    private readonly Channel<TransportBlock> _inbound;
    private readonly bool _leaveConnectionOpen;
    private bool _disposed;

    public P2PReliableStreamTransport(P2PTransportConnection connection, bool leaveConnectionOpen = false)
    {
        this._connection = connection ?? throw new ArgumentNullException(nameof(connection));
        this._dataStream = connection.DataStream
                           ?? throw new ArgumentException("Connection does not expose a data stream.", nameof(connection));
        this._controlChannel = connection.ControlChannel
                               ?? throw new ArgumentException("Connection does not expose a control channel.", nameof(connection));
        this._leaveConnectionOpen = leaveConnectionOpen;
        this._inbound = Channel.CreateUnbounded<TransportBlock>(
            new()
            {
                SingleReader = false,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            });

        this._dataLoop = Task.Run(() => this.DataReceiveLoopAsync(this._cts.Token));
        this._controlLoop = Task.Run(() => this.ControlReceiveLoopAsync(this._cts.Token));
    }

    public ValueTask SendAsync(long sequenceNumber, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default)
    {
        this.ThrowIfDisposed();

        return this.SendCoreAsync(sequenceNumber, payload, cancellationToken);
    }

    public ValueTask<TransportBlock> ReceiveAsync(CancellationToken cancellationToken = default)
    {
        this.ThrowIfDisposed();

        return this._inbound.Reader.ReadAsync(cancellationToken);
    }

    public ValueTask AcknowledgeAsync(long sequenceNumber, CancellationToken cancellationToken = default)
    {
        this.ThrowIfDisposed();

        return this.SendAcknowledgementAsync(sequenceNumber, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (this._disposed)
            return;

        this._disposed = true;
        this._cts.Cancel();

        try
        {
            await Task.WhenAll(this._dataLoop, this._controlLoop).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }

        this._cts.Dispose();
        this._dataWriteLock.Dispose();
        this._controlWriteLock.Dispose();
        this._inbound.Writer.TryComplete();

        if (!this._leaveConnectionOpen)
            await this._connection.DisposeAsync().ConfigureAwait(false);
    }

    private async ValueTask SendCoreAsync(long sequenceNumber, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        await this._dataWriteLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var header = new byte[HeaderSize];
            BinaryPrimitives.WriteInt64LittleEndian(header, sequenceNumber);
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(sizeof(long)), payload.Length);

            await this._dataStream.WriteAsync(header.AsMemory(0, HeaderSize), cancellationToken).ConfigureAwait(false);
            if (!payload.IsEmpty)
                await this._dataStream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
            await this._dataStream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            this._dataWriteLock.Release();
        }
    }

    private async ValueTask SendAcknowledgementAsync(long sequenceNumber, CancellationToken cancellationToken)
    {
        await this._controlWriteLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var payload = new byte[sizeof(long)];
            BinaryPrimitives.WriteInt64LittleEndian(payload, sequenceNumber);
            var frame = new P2PControlFrame(
                P2PControlFrameType.FlowControl,
                DateTimeOffset.UtcNow.UtcTicks,
                sequenceNumber,
                payload);
            await this._controlChannel.SendAsync(frame, cancellationToken).ConfigureAwait(false);
            await this._controlChannel.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            this._controlWriteLock.Release();
        }
    }

    private async Task DataReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var header = new byte[HeaderSize];

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await this._dataStream.ReadExactlyAsync(header.AsMemory(0, HeaderSize), cancellationToken).ConfigureAwait(false);
                var sequence = BinaryPrimitives.ReadInt64LittleEndian(header);
                var length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(sizeof(long)));

                if (length < 0)
                    throw new InvalidDataException($"Negative payload length received: {length}.");

                var buffer = new byte[length];
                if (length > 0)
                    await this._dataStream.ReadExactlyAsync(buffer.AsMemory(0, length), cancellationToken).ConfigureAwait(false);

                await this._inbound.Writer.WriteAsync(new(sequence, buffer, false), cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        catch (EndOfStreamException) { }
        catch (Exception ex)
        {
            this._inbound.Writer.TryComplete(ex);
            this._cts.Cancel();
        }
    }

    private async Task ControlReceiveLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var frame in this._controlChannel.ReadFramesAsync(cancellationToken))
            {
                if (frame.Type != P2PControlFrameType.FlowControl)
                    continue;

                if (frame.Payload.Length < sizeof(long))
                    continue;

                var sequence = BinaryPrimitives.ReadInt64LittleEndian(frame.Payload.Span);
                await this._inbound.Writer.WriteAsync(new(sequence, ReadOnlyMemory<byte>.Empty, true), cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            this._inbound.Writer.TryComplete(ex);
            this._cts.Cancel();
        }
    }

    private void ThrowIfDisposed()
    {
        if (this._disposed)
            throw new ObjectDisposedException(nameof(P2PReliableStreamTransport));
    }
}