// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Buffers;
using System.Collections.Concurrent;
using Itexoft.Extensions;
using Itexoft.Threading;

namespace Itexoft.Net.Core.Internal;

/// <summary>
/// Fixed-size buffer that retains payload blocks until acknowledgements are received.
/// </summary>
internal sealed class BufferedReliabilityQueue(int capacityBytes) : IDisposable
{
    private readonly ConcurrentDictionary<long, TaskCompletionSource<bool>> ackWaiters = new();
    private readonly int capacityBytes = capacityBytes.RequiredPositive();
    private readonly ConcurrentDictionary<long, BufferEntry> inflight = new();
    private readonly ConcurrentQueue<BufferEntry> ordered = new();
    private readonly SemaphoreSlim spaceAvailable = new(0, int.MaxValue);
    private readonly Lock sync = new();
    private int currentBytes;

    /// <summary>
    /// Number of bytes currently buffered.
    /// </summary>
    public int BufferedBytes => Volatile.Read(ref this.currentBytes);

    /// <summary>
    /// Number of blocks currently buffered.
    /// </summary>
    public int BufferedBlocks => this.inflight.Count;

    public void Dispose()
    {
        while (this.ordered.TryDequeue(out var item))
            ArrayPool<byte>.Shared.Return(item.Buffer);

        this.inflight.Clear();

        foreach (var waiter in this.ackWaiters.Values)
            waiter.TrySetCanceled();

        this.ackWaiters.Clear();
        this.spaceAvailable.Dispose();
    }

    /// <summary>
    /// Tries to enqueue the specified payload block. Returns <c>false</c> when the buffer is full.
    /// </summary>
    public bool TryEnqueue(long sequence, ReadOnlyMemory<byte> payload)
    {
        var size = payload.Length;

        lock (this.sync)
        {
            if (Volatile.Read(ref this.currentBytes) + size > this.capacityBytes)
                return false;

            var buffer = ArrayPool<byte>.Shared.Rent(size);
            payload.Span.CopyTo(buffer.AsSpan(0, size));
            var entry = new BufferEntry(sequence, buffer, size);
            this.ordered.Enqueue(entry);
            this.inflight[sequence] = entry;
            this.ackWaiters[sequence] = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Interlocked.Add(ref this.currentBytes, size);

            return true;
        }
    }

    /// <summary>
    /// Returns the next payload block without removing it from the buffer.
    /// </summary>
    public bool TryGetNext(out long sequence, out ReadOnlyMemory<byte> payload)
    {
        if (this.ordered.TryPeek(out var item))
        {
            sequence = item.Sequence;
            payload = new(item.Buffer, 0, item.Length);

            return true;
        }

        sequence = 0;
        payload = ReadOnlyMemory<byte>.Empty;

        return false;
    }

    /// <summary>
    /// Marks the specified sequence as acknowledged and releases its resources.
    /// </summary>
    public void Acknowledge(long sequence)
    {
        if (this.inflight.TryRemove(sequence, out var entry))
        {
            Interlocked.Add(ref this.currentBytes, -entry.Length);
            ArrayPool<byte>.Shared.Return(entry.Buffer);
            this.spaceAvailable.Release();
        }

        // dequeue head if acknowledged
        while (this.ordered.TryPeek(out var head))
        {
            if (head.Sequence == sequence || !this.inflight.ContainsKey(head.Sequence))
                this.ordered.TryDequeue(out _);
            else
                break;
        }

        if (this.ackWaiters.TryRemove(sequence, out var waiter))
            waiter.TrySetResult(true);
    }

    /// <summary>
    /// Waits until space becomes available in the buffer.
    /// </summary>
    public async ValueTask WaitForSpaceAsync(CancelToken cancelToken)
    {
        using (cancelToken.Bridge(out var token))
            await this.spaceAvailable.WaitAsync(token);
    }

    /// <summary>
    /// Waits until the specified sequence number is acknowledged.
    /// </summary>
    public async ValueTask WaitForAcknowledgementAsync(long sequence, CancelToken cancelToken)
    {
        if (!this.ackWaiters.TryGetValue(sequence, out var waiter))
            return;

        if (cancelToken.IsNone)
            await waiter.Task;

        using (cancelToken.Bridge(out var token))
            await waiter.Task.WaitAsync(token);
    }

    private readonly record struct BufferEntry(long Sequence, byte[] Buffer, int Length);
}
