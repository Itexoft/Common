// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Collections.Concurrent;
using Itexoft.Networking.Reliable.Internal;

namespace Itexoft.Networking.Reliable;

/// <summary>
/// Write-only stream that guarantees ordering until acknowledgements are received.
/// </summary>
public sealed class ReliableStreamWriter : Stream
{
    private readonly CancellationTokenSource? _ackCts;
    private readonly Task? _ackLoop;
    private readonly bool _automaticAckProcessing;
    private readonly BufferedReliabilityQueue _buffer;
    private readonly ReliableStreamOptions _options;
    private readonly ConcurrentDictionary<long, DateTimeOffset> _sendTimestamps = new();
    private readonly IReliableStreamTransport _transport;
    private Exception? _ackLoopException;
    private bool _disposed;
    private double _lastAckLatencyMs;
    private long _lastAcknowledgedSequence = -1;
    private long _nextSequence;

    /// <summary>
    /// Creates a new reliable stream writer.
    /// </summary>
    /// <param name="transport">Underlying transport used for sending payload blocks.</param>
    /// <param name="options">Writer configuration.</param>
    public ReliableStreamWriter(IReliableStreamTransport transport, ReliableStreamOptions? options = null)
    {
        this._transport = transport ?? throw new ArgumentNullException(nameof(transport));
        this._options = options ?? new ReliableStreamOptions();
        this._buffer = new(this._options.BufferCapacityBytes);
        this._automaticAckProcessing = this._options.EnsureDeliveredBeforeRelease;

        if (this._automaticAckProcessing)
        {
            this._ackCts = new();
            this._ackLoop = Task.Run(() => this.RunAcknowledgementLoopAsync(this._ackCts.Token));
        }
    }

    /// <inheritdoc />
    public override bool CanRead => false;

    /// <inheritdoc />
    public override bool CanSeek => false;

    /// <inheritdoc />
    public override bool CanWrite => true;

    /// <inheritdoc />
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    /// <inheritdoc />
    public override void Flush() { }

    /// <inheritdoc />
    public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) =>
        this.WriteAsync(buffer.AsMemory(offset, count), CancellationToken.None).GetAwaiter().GetResult();

    /// <inheritdoc />
    public async override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        this.ThrowIfDisposed();
        this.ThrowIfAckLoopFaulted();

        var sequence = this._nextSequence;
        while (!this._buffer.TryEnqueue(sequence, buffer))
        {
            await this._buffer.WaitForSpaceAsync(cancellationToken).ConfigureAwait(false);
            this.ThrowIfAckLoopFaulted();
        }

        this._sendTimestamps[sequence] = DateTimeOffset.UtcNow;

        await this._transport.SendAsync(sequence, buffer, cancellationToken).ConfigureAwait(false);
        this._nextSequence++;

        if (this._options.EnsureDeliveredBeforeRelease)
            await this._buffer.WaitForAcknowledgementAsync(sequence, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Waits for the next acknowledgement and returns the sequence number it refers to.
    /// </summary>
    public async ValueTask<long> ProcessNextAcknowledgementAsync(CancellationToken cancellationToken = default)
    {
        this.ThrowIfDisposed();

        if (this._automaticAckProcessing)
            throw new InvalidOperationException(
                "Acknowledgements are processed automatically when EnsureDeliveredBeforeRelease is enabled.");

        while (true)
        {
            var block = await this._transport.ReceiveAsync(cancellationToken).ConfigureAwait(false);

            if (!block.IsAcknowledgement)
                throw new InvalidOperationException("Unexpected payload received on the acknowledgement channel.");

            this._buffer.Acknowledge(block.SequenceNumber);
            this.OnAcknowledged(block.SequenceNumber);

            return block.SequenceNumber;
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing && !this._disposed)
        {
            this._buffer.Dispose();
            this._ackCts?.Cancel();
            this._disposed = true;
        }

        base.Dispose(disposing);
    }

    /// <inheritdoc />
    public async override ValueTask DisposeAsync()
    {
        if (!this._disposed)
        {
            this._ackCts?.Cancel();
            this._buffer.Dispose();
            this._disposed = true;
        }

        if (this._ackLoop is not null)
            try
            {
                await this._ackLoop.ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }

        this._ackCts?.Dispose();
        await this._transport.DisposeAsync().ConfigureAwait(false);
        await base.DisposeAsync().ConfigureAwait(false);
    }

    private void ThrowIfDisposed()
    {
        if (this._disposed)
            throw new ObjectDisposedException(nameof(ReliableStreamWriter));
    }

    private void ThrowIfAckLoopFaulted()
    {
        if (this._ackLoopException is not null)
            throw new IOException("Acknowledgement processing loop failed.", this._ackLoopException);
    }

    private async Task RunAcknowledgementLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var block = await this._transport.ReceiveAsync(cancellationToken).ConfigureAwait(false);
                if (block.IsAcknowledgement)
                {
                    this._buffer.Acknowledge(block.SequenceNumber);
                    this.OnAcknowledged(block.SequenceNumber);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            this._ackLoopException = ex;
        }
    }

    private void OnAcknowledged(long sequence)
    {
        if (this._sendTimestamps.TryRemove(sequence, out var timestamp))
        {
            var now = DateTimeOffset.UtcNow;
            this._lastAcknowledgedSequence = sequence;
            this._lastAckLatencyMs = (now - timestamp).TotalMilliseconds;
        }
    }

    /// <summary>
    /// Provides a snapshot of buffering/acknowledgement metrics.
    /// </summary>
    public ReliableStreamMetrics GetMetrics() => new(
        this._buffer.BufferedBytes,
        this._buffer.BufferedBlocks,
        this._lastAcknowledgedSequence,
        this._lastAckLatencyMs);
}