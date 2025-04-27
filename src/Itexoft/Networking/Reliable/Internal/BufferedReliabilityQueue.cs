// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Buffers;
using System.Collections.Concurrent;

namespace Itexoft.Networking.Reliable.Internal;

/// <summary>
/// Fixed-size buffer that retains payload blocks until acknowledgements are received.
/// </summary>
internal sealed class BufferedReliabilityQueue : IDisposable
{
    private readonly ConcurrentDictionary<long, TaskCompletionSource<bool>> _ackWaiters = new();
    private readonly int _capacityBytes;
    private readonly ConcurrentDictionary<long, BufferEntry> _inflight = new();
    private readonly ConcurrentQueue<BufferEntry> _ordered = new();
    private readonly SemaphoreSlim _spaceAvailable = new(0, int.MaxValue);
    private readonly object _sync = new();
    private int _currentBytes;

    public BufferedReliabilityQueue(int capacityBytes)
    {
        if (capacityBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacityBytes));

        this._capacityBytes = capacityBytes;
    }

    /// <summary>
    /// Number of bytes currently buffered.
    /// </summary>
    public int BufferedBytes => Volatile.Read(ref this._currentBytes);

    /// <summary>
    /// Number of blocks currently buffered.
    /// </summary>
    public int BufferedBlocks => this._inflight.Count;

    public void Dispose()
    {
        while (this._ordered.TryDequeue(out var item))
            ArrayPool<byte>.Shared.Return(item.Buffer);

        this._inflight.Clear();
        foreach (var waiter in this._ackWaiters.Values)
            waiter.TrySetCanceled();

        this._ackWaiters.Clear();
        this._spaceAvailable.Dispose();
    }

    /// <summary>
    /// Tries to enqueue the specified payload block. Returns <c>false</c> when the buffer is full.
    /// </summary>
    public bool TryEnqueue(long sequence, ReadOnlyMemory<byte> payload)
    {
        var size = payload.Length;
        lock (this._sync)
        {
            if (this._currentBytes + size > this._capacityBytes)
                return false;

            var buffer = ArrayPool<byte>.Shared.Rent(size);
            payload.Span.CopyTo(buffer.AsSpan(0, size));
            var entry = new BufferEntry(sequence, buffer, size);
            this._ordered.Enqueue(entry);
            this._inflight[sequence] = entry;
            this._ackWaiters[sequence] = new(TaskCreationOptions.RunContinuationsAsynchronously);
            this._currentBytes += size;

            return true;
        }
    }

    /// <summary>
    /// Returns the next payload block without removing it from the buffer.
    /// </summary>
    public bool TryGetNext(out long sequence, out ReadOnlyMemory<byte> payload)
    {
        if (this._ordered.TryPeek(out var item))
        {
            sequence = item.Sequence;
            payload = new(item.Buffer, 0, item.Length);

            return true;
        }

        sequence = default;
        payload = ReadOnlyMemory<byte>.Empty;

        return false;
    }

    /// <summary>
    /// Marks the specified sequence as acknowledged and releases its resources.
    /// </summary>
    public void Acknowledge(long sequence)
    {
        if (this._inflight.TryRemove(sequence, out var entry))
        {
            lock (this._sync)
            {
                this._currentBytes -= entry.Length;
            }

            ArrayPool<byte>.Shared.Return(entry.Buffer);
            this._spaceAvailable.Release();
        }

        // dequeue head if acknowledged
        while (this._ordered.TryPeek(out var head))
            if (head.Sequence == sequence)
                this._ordered.TryDequeue(out _);
            else if (!this._inflight.ContainsKey(head.Sequence))
                this._ordered.TryDequeue(out _);
            else
                break;

        if (this._ackWaiters.TryRemove(sequence, out var waiter))
            waiter.TrySetResult(true);
    }

    /// <summary>
    /// Waits until space becomes available in the buffer.
    /// </summary>
    public ValueTask WaitForSpaceAsync(CancellationToken cancellationToken) =>
        new(this._spaceAvailable.WaitAsync(cancellationToken));

    /// <summary>
    /// Waits until the specified sequence number is acknowledged.
    /// </summary>
    public ValueTask WaitForAcknowledgementAsync(long sequence, CancellationToken cancellationToken)
    {
        if (!this._ackWaiters.TryGetValue(sequence, out var waiter))
            return ValueTask.CompletedTask;

        if (!cancellationToken.CanBeCanceled)
            return new(waiter.Task);

        return new(waiter.Task.WaitAsync(cancellationToken));
    }

    private readonly record struct BufferEntry(long Sequence, byte[] Buffer, int Length);
}