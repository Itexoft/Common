// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Buffers;

namespace Itexoft.IO;

public sealed class ChaosStream : Stream
{
    private static readonly ValueTask CompletedTask = ValueTask.CompletedTask;
    private readonly Func<TimeSpan, CancellationToken, ValueTask> delayScheduler;

    private readonly Stream inner;
    private readonly bool leaveOpen;
    private readonly ChaosStreamOptions options;
    private readonly Random random;
    private readonly long startTimestamp;
    private readonly TimeProvider timeProvider;
    private long bytesRead;
    private long bytesWritten;
    private int disconnected;
    private bool disposed;
    private long flushOperations;

    private long readOperations;
    private long writeOperations;

    public ChaosStream(Stream inner, ChaosStreamOptions? options = null, bool leaveOpen = false)
    {
        this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        this.options = (options ?? new ChaosStreamOptions()).Clone();
        this.options.Validate();
        this.leaveOpen = leaveOpen;
        this.random = this.options.Random ?? Random.Shared;
        this.delayScheduler = this.options.DelayScheduler ?? DefaultDelayScheduler;
        this.timeProvider = this.options.TimeProvider ?? TimeProvider.System;
        this.startTimestamp = this.timeProvider.GetTimestamp();
    }

    public Stream InnerStream => this.inner;

    public override bool CanRead => !this.disposed && this.inner.CanRead;

    public override bool CanSeek => !this.disposed && this.inner.CanSeek;

    public override bool CanWrite => !this.disposed && this.inner.CanWrite;

    public override long Length
    {
        get
        {
            this.ThrowIfUnavailable();

            return this.inner.Length;
        }
    }

    public override long Position
    {
        get
        {
            this.ThrowIfUnavailable();

            return this.inner.Position;
        }
        set
        {
            this.ThrowIfUnavailable();
            this.inner.Position = value;
        }
    }

    public override void Flush()
    {
        this.ThrowIfUnavailable();
        var context = this.PrepareContext(ChaosOperation.Flush, 0);
        this.MaybeDisconnect(context);

        this.MaybeStall(context, CancellationToken.None);

        var inject = this.ShouldInjectNoise(context);

        if (inject)
        {
            this.MaybeDelay(
                this.options.MinWriteDelay,
                this.options.MaxWriteDelay,
                this.options.WriteDelayProbability,
                CancellationToken.None);
            this.MaybeThrow(this.options.FlushFailureProbability, "flush");
        }

        this.inner.Flush();
        this.RecordOperation(ChaosOperation.Flush, 0);
    }

    public async override Task FlushAsync(CancellationToken cancellationToken)
    {
        this.ThrowIfUnavailable();
        var context = this.PrepareContext(ChaosOperation.Flush, 0);
        this.MaybeDisconnect(context);

        await this.MaybeStallAsync(context, cancellationToken).ConfigureAwait(false);

        var inject = this.ShouldInjectNoise(context);

        if (inject)
        {
            await this.MaybeDelayAsync(
                this.options.MinWriteDelay,
                this.options.MaxWriteDelay,
                this.options.WriteDelayProbability,
                cancellationToken).ConfigureAwait(false);
            this.MaybeThrow(this.options.FlushFailureProbability, "flush");
        }

        await this.inner.FlushAsync(cancellationToken).ConfigureAwait(false);
        this.RecordOperation(ChaosOperation.Flush, 0);
    }

    public override int Read(byte[] buffer, int offset, int count) => this.Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        this.ThrowIfUnavailable();
        var context = this.PrepareContext(ChaosOperation.Read, buffer.Length);
        this.MaybeDisconnect(context);

        this.MaybeStall(context, CancellationToken.None);

        var inject = this.ShouldInjectNoise(context);

        if (inject)
        {
            this.MaybeDelay(
                this.options.MinReadDelay,
                this.options.MaxReadDelay,
                this.options.ReadDelayProbability,
                CancellationToken.None);
            this.MaybeThrow(this.options.ReadFailureProbability, "read");

            if (this.ShouldSkip(this.options.DropReadProbability))
            {
                this.RecordOperation(ChaosOperation.Read, 0);

                return 0;
            }
        }

        var read = this.inner.Read(buffer);
        if (read <= 0)
        {
            this.RecordOperation(ChaosOperation.Read, 0);

            return read;
        }

        if (inject)
        {
            read = this.MaybeTruncate(read, this.options.ReadTruncationProbability, this.options.MaxReadTruncationBytes);
            if (read > 0)
                this.MaybeFlipBits(buffer[..read], this.options.ReadBitFlipProbability, this.options.MaxReadBitFlips);
        }

        this.RecordOperation(ChaosOperation.Read, read);
        this.ApplyPerByteDelay(ChaosOperation.Read, read, CancellationToken.None);

        return read;
    }

    public async override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        this.ThrowIfUnavailable();
        var context = this.PrepareContext(ChaosOperation.Read, buffer.Length);
        this.MaybeDisconnect(context);

        await this.MaybeStallAsync(context, cancellationToken).ConfigureAwait(false);

        var inject = this.ShouldInjectNoise(context);

        if (inject)
        {
            await this.MaybeDelayAsync(
                this.options.MinReadDelay,
                this.options.MaxReadDelay,
                this.options.ReadDelayProbability,
                cancellationToken).ConfigureAwait(false);
            this.MaybeThrow(this.options.ReadFailureProbability, "read");

            if (this.ShouldSkip(this.options.DropReadProbability))
            {
                this.RecordOperation(ChaosOperation.Read, 0);

                return 0;
            }
        }

        var read = await this.inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        if (read <= 0)
        {
            this.RecordOperation(ChaosOperation.Read, 0);

            return read;
        }

        if (inject)
        {
            read = this.MaybeTruncate(read, this.options.ReadTruncationProbability, this.options.MaxReadTruncationBytes);
            if (read > 0)
            {
                var span = buffer.Span[..read];
                this.MaybeFlipBits(span, this.options.ReadBitFlipProbability, this.options.MaxReadBitFlips);
            }
        }

        this.RecordOperation(ChaosOperation.Read, read);
        await this.ApplyPerByteDelayAsync(ChaosOperation.Read, read, cancellationToken).ConfigureAwait(false);

        return read;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        this.ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override void Write(byte[] buffer, int offset, int count) => this.Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        this.ThrowIfUnavailable();
        var context = this.PrepareContext(ChaosOperation.Write, buffer.Length);
        this.MaybeDisconnect(context);

        this.MaybeStall(context, CancellationToken.None);

        var inject = this.ShouldInjectNoise(context);

        if (inject)
        {
            this.MaybeDelay(
                this.options.MinWriteDelay,
                this.options.MaxWriteDelay,
                this.options.WriteDelayProbability,
                CancellationToken.None);
            this.MaybeThrow(this.options.WriteFailureProbability, "write");

            if (this.ShouldSkip(this.options.DropWriteProbability))
            {
                this.RecordOperation(ChaosOperation.Write, 0);

                return;
            }
        }

        var effectiveLength = buffer.Length;
        if (inject)
        {
            effectiveLength = this.MaybeTruncate(
                effectiveLength,
                this.options.WriteTruncationProbability,
                this.options.MaxWriteTruncationBytes);
            if (effectiveLength <= 0)
            {
                this.RecordOperation(ChaosOperation.Write, 0);

                return;
            }
        }

        var writtenBytes = effectiveLength;
        var slice = buffer[..effectiveLength];
        if (inject && this.ShouldFlip(this.options.WriteBitFlipProbability, this.options.MaxWriteBitFlips, out var flips))
        {
            var rented = ArrayPool<byte>.Shared.Rent(effectiveLength);
            try
            {
                var temp = rented.AsSpan(0, effectiveLength);
                slice.CopyTo(temp);
                this.ApplyBitFlips(temp, flips);
                this.inner.Write(temp);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
        else
        {
            this.inner.Write(slice);
        }

        this.RecordOperation(ChaosOperation.Write, writtenBytes);
        this.ApplyPerByteDelay(ChaosOperation.Write, writtenBytes, CancellationToken.None);
    }

    public async override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        this.ThrowIfUnavailable();
        var context = this.PrepareContext(ChaosOperation.Write, buffer.Length);
        this.MaybeDisconnect(context);

        await this.MaybeStallAsync(context, cancellationToken).ConfigureAwait(false);

        var inject = this.ShouldInjectNoise(context);

        if (inject)
        {
            await this.MaybeDelayAsync(
                this.options.MinWriteDelay,
                this.options.MaxWriteDelay,
                this.options.WriteDelayProbability,
                cancellationToken).ConfigureAwait(false);
            this.MaybeThrow(this.options.WriteFailureProbability, "write");

            if (this.ShouldSkip(this.options.DropWriteProbability))
            {
                this.RecordOperation(ChaosOperation.Write, 0);

                return;
            }
        }

        var effectiveLength = buffer.Length;
        if (inject)
        {
            effectiveLength = this.MaybeTruncate(
                effectiveLength,
                this.options.WriteTruncationProbability,
                this.options.MaxWriteTruncationBytes);
            if (effectiveLength <= 0)
            {
                this.RecordOperation(ChaosOperation.Write, 0);

                return;
            }
        }

        var writtenBytes = effectiveLength;
        var memory = buffer[..effectiveLength];
        if (inject && this.ShouldFlip(this.options.WriteBitFlipProbability, this.options.MaxWriteBitFlips, out var flips))
        {
            var rented = ArrayPool<byte>.Shared.Rent(effectiveLength);
            try
            {
                var temp = rented.AsMemory(0, effectiveLength);
                memory.CopyTo(temp);
                this.ApplyBitFlips(temp.Span, flips);
                await this.inner.WriteAsync(temp, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
        else
        {
            await this.inner.WriteAsync(memory, cancellationToken).ConfigureAwait(false);
        }

        this.RecordOperation(ChaosOperation.Write, writtenBytes);
        await this.ApplyPerByteDelayAsync(ChaosOperation.Write, writtenBytes, cancellationToken).ConfigureAwait(false);
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        this.WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override long Seek(long offset, SeekOrigin origin)
    {
        this.ThrowIfUnavailable();

        return this.inner.Seek(offset, origin);
    }

    public override void SetLength(long value)
    {
        this.ThrowIfUnavailable();
        this.inner.SetLength(value);
    }

    protected override void Dispose(bool disposing)
    {
        if (!this.disposed && disposing)
            if (!this.leaveOpen)
                this.inner.Dispose();

        this.disposed = true;
        base.Dispose(disposing);
    }

    public async override ValueTask DisposeAsync()
    {
        if (!this.disposed)
        {
            if (!this.leaveOpen)
                await this.inner.DisposeAsync().ConfigureAwait(false);

            this.disposed = true;
        }

        await base.DisposeAsync().ConfigureAwait(false);
    }

    private static ValueTask DefaultDelayScheduler(TimeSpan delay, CancellationToken cancellationToken)
    {
        if (delay <= TimeSpan.Zero)
            return CompletedTask;

        return new(Task.Delay(delay, cancellationToken));
    }

    private void ThrowIfUnavailable()
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);
        if (Volatile.Read(ref this.disconnected) == 1)
            this.ThrowDisconnected();
    }

    private void ThrowDisconnected() => throw this.CreateDisconnectException();

    private IOException CreateDisconnectException() => new(this.options.DisconnectMessage ?? "ChaosStream injected a disconnection.");

    private bool ShouldInjectNoise(in ChaosOperationContext context)
    {
        if (this.options.ShouldApplyNoise is { } global && !global(context))
            return false;

        var specific = context.Operation switch
        {
            ChaosOperation.Read => this.options.ShouldApplyReadNoise,
            ChaosOperation.Write => this.options.ShouldApplyWriteNoise,
            ChaosOperation.Flush => this.options.ShouldApplyFlushNoise,
            _ => null
        };

        if (specific is null)
            return true;

        return specific(context);
    }

    private ChaosOperationContext PrepareContext(ChaosOperation operation, int requestedBytes)
    {
        var metrics = this.GetMetrics();
        var operationNumber = operation switch
        {
            ChaosOperation.Read => metrics.ReadOperations + 1,
            ChaosOperation.Write => metrics.WriteOperations + 1,
            ChaosOperation.Flush => metrics.FlushOperations + 1,
            _ => metrics.TotalOperations + 1
        };

        return new(operation, requestedBytes, operationNumber, metrics);
    }

    private ChaosMetrics GetMetrics()
    {
        var elapsed = this.timeProvider.GetElapsedTime(this.startTimestamp);

        return new(
            elapsed,
            Volatile.Read(ref this.readOperations),
            Volatile.Read(ref this.writeOperations),
            Volatile.Read(ref this.flushOperations),
            Volatile.Read(ref this.bytesRead),
            Volatile.Read(ref this.bytesWritten));
    }

    private void MaybeDisconnect(in ChaosOperationContext context)
    {
        if (Volatile.Read(ref this.disconnected) == 1)
            this.ThrowDisconnected();

        if (this.options.ShouldDisconnect is { } predicate && predicate(context))
            this.TriggerDisconnect();
        else if (this.options.DisconnectProbability > 0 && this.NextDouble() < this.options.DisconnectProbability)
            this.TriggerDisconnect();
    }

    private void TriggerDisconnect()
    {
        if (Interlocked.Exchange(ref this.disconnected, 1) == 0)
            throw this.CreateDisconnectException();

        this.ThrowDisconnected();
    }

    private void MaybeStall(in ChaosOperationContext context, CancellationToken cancellationToken)
    {
        var duration = this.GetStallDuration(context);

        if (duration is null || duration <= TimeSpan.Zero)
            return;

        this.delayScheduler(duration.Value, cancellationToken).GetAwaiter().GetResult();
    }

    private ValueTask MaybeStallAsync(in ChaosOperationContext context, CancellationToken cancellationToken)
    {
        var duration = this.GetStallDuration(context);

        if (duration is null || duration <= TimeSpan.Zero)
            return CompletedTask;

        return this.delayScheduler(duration.Value, cancellationToken);
    }

    private TimeSpan? GetStallDuration(in ChaosOperationContext context)
    {
        if (this.options.StallDurationProvider is { } provider)
        {
            var value = provider(context);

            return value is { } stall && stall > TimeSpan.Zero ? stall : null;
        }

        if (this.options.StallProbability <= 0)
            return null;

        if (this.NextDouble() >= this.options.StallProbability)
            return null;

        var duration = this.NextDelay(this.options.MinStallDuration, this.options.MaxStallDuration);

        return duration > TimeSpan.Zero ? duration : null;
    }

    private void RecordOperation(ChaosOperation operation, int affectedBytes)
    {
        switch (operation)
        {
            case ChaosOperation.Read:
                Interlocked.Increment(ref this.readOperations);
                if (affectedBytes > 0)
                    Interlocked.Add(ref this.bytesRead, affectedBytes);

                break;
            case ChaosOperation.Write:
                Interlocked.Increment(ref this.writeOperations);
                if (affectedBytes > 0)
                    Interlocked.Add(ref this.bytesWritten, affectedBytes);

                break;
            case ChaosOperation.Flush:
                Interlocked.Increment(ref this.flushOperations);

                break;
        }
    }

    private void ApplyPerByteDelay(ChaosOperation operation, int bytes, CancellationToken cancellationToken)
    {
        var duration = this.CalculatePerByteDelay(operation, bytes);

        if (duration is null || duration <= TimeSpan.Zero)
            return;

        this.delayScheduler(duration.Value, cancellationToken).GetAwaiter().GetResult();
    }

    private ValueTask ApplyPerByteDelayAsync(ChaosOperation operation, int bytes, CancellationToken cancellationToken)
    {
        var duration = this.CalculatePerByteDelay(operation, bytes);

        if (duration is null || duration <= TimeSpan.Zero)
            return CompletedTask;

        return this.delayScheduler(duration.Value, cancellationToken);
    }

    private TimeSpan? CalculatePerByteDelay(ChaosOperation operation, int bytes)
    {
        if (bytes <= 0)
            return null;

        var perByte = operation switch
        {
            ChaosOperation.Read => this.options.ReadPerByteDelay,
            ChaosOperation.Write => this.options.WritePerByteDelay,
            _ => TimeSpan.Zero
        };

        if (perByte <= TimeSpan.Zero)
            return null;

        try
        {
            var totalTicks = checked(perByte.Ticks * (long)bytes);

            if (totalTicks <= 0)
                return null;

            return TimeSpan.FromTicks(totalTicks);
        }
        catch (OverflowException)
        {
            return TimeSpan.MaxValue;
        }
    }

    private void MaybeThrow(double probability, string operation)
    {
        if (probability <= 0)
            return;

        if (this.NextDouble() < probability)
            throw new IOException($"ChaosStream injected a failure during {operation}.");
    }

    private bool ShouldSkip(double probability) => probability > 0 && this.NextDouble() < probability;

    private bool ShouldFlip(double probability, int maxFlips, out int flips)
    {
        flips = 0;

        if (probability <= 0 || maxFlips <= 0)
            return false;

        if (this.NextDouble() >= probability)
            return false;

        flips = Math.Min(maxFlips, int.MaxValue);
        if (flips > 1)
            flips = this.NextInt32(1, flips + 1);
        else
            flips = 1;

        return flips > 0;
    }

    private void ApplyBitFlips(Span<byte> data, int flips)
    {
        if (data.IsEmpty || flips <= 0)
            return;

        var bitCount = data.Length * 8;
        for (var i = 0; i < flips; i++)
        {
            var bitIndex = this.NextInt32(0, bitCount);
            var byteIndex = bitIndex / 8;
            var bitOffset = bitIndex % 8;
            data[byteIndex] ^= (byte)(1 << bitOffset);
        }
    }

    private int MaybeTruncate(int count, double probability, int maxBytes)
    {
        if (count <= 0 || probability <= 0 || maxBytes <= 0)
            return count;

        if (this.NextDouble() >= probability)
            return count;

        var dropLimit = Math.Min(count, maxBytes);
        var drop = dropLimit <= 1 ? 1 : this.NextInt32(1, dropLimit + 1);
        var remaining = count - drop;

        return remaining < 0 ? 0 : remaining;
    }

    private void MaybeFlipBits(Span<byte> data, double probability, int maxFlips)
    {
        if (!this.ShouldFlip(probability, maxFlips, out var flips))
            return;

        this.ApplyBitFlips(data, flips);
    }

    private void MaybeDelay(TimeSpan min, TimeSpan max, double probability, CancellationToken cancellationToken) =>
        this.MaybeDelayAsync(min, max, probability, cancellationToken).GetAwaiter().GetResult();

    private ValueTask MaybeDelayAsync(TimeSpan min, TimeSpan max, double probability, CancellationToken cancellationToken)
    {
        if (max <= TimeSpan.Zero || probability <= 0)
            return CompletedTask;

        if (this.NextDouble() >= probability)
            return CompletedTask;

        var delay = this.NextDelay(min, max);

        if (delay <= TimeSpan.Zero)
            return CompletedTask;

        return this.delayScheduler(delay, cancellationToken);
    }

    private TimeSpan NextDelay(TimeSpan min, TimeSpan max)
    {
        if (max <= min)
            return min;

        var rangeTicks = max.Ticks - min.Ticks;
        var sample = this.NextDouble();
        var offset = (long)(rangeTicks * sample);

        return min + TimeSpan.FromTicks(offset);
    }

    private double NextDouble() => this.random.NextDouble();

    private int NextInt32(int minValue, int maxValue) => minValue >= maxValue ? minValue : this.random.Next(minValue, maxValue);
}