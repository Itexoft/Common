// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Collections.Concurrent;
using Itexoft.IO;
using Itexoft.Threading;

namespace Itexoft.Tests;

public sealed class ChaosStreamTests
{
    [Test]
    public async Task DropReadReturnsZeroAndDoesNotConsumeInnerStream()
    {
        await using var inner = new RamAsyncStream([1, 2, 3]);

        var options = new ChaosStreamOptions
        {
            DropReadProbability = 1,
            Random = new SequenceRandom(doubles: [0]),
        };

        await using var chaos = new ChaosAsyncStream(inner, options);
        var buffer = new byte[3].AsMemory();

        var read = await chaos.ReadAsync(buffer);

        Assert.That(read, Is.EqualTo(0));
        Assert.That(inner.Position, Is.EqualTo(0));
    }

    [Test]
    public void ReadTruncationDropsConfiguredBytes()
    {
        using var inner = new RamStream([1, 2, 3]);

        var options = new ChaosStreamOptions
        {
            ReadTruncationProbability = 1,
            MaxReadTruncationBytes = 1,
            Random = new SequenceRandom(doubles: [0]),
        };

        using var chaos = new ChaosStream(inner, options);
        Span<byte> buffer = stackalloc byte[3];

        var read = chaos.Read(buffer);

        Assert.That(read, Is.EqualTo(2));
        Assert.That(inner.Position, Is.EqualTo(3));
        Assert.That(buffer[0], Is.EqualTo(1));
        Assert.That(buffer[1], Is.EqualTo(2));
    }

    [Test]
    public void ReadBitFlipMutatesReturnedData()
    {
        using var inner = new RamStream([0]);

        var options = new ChaosStreamOptions
        {
            ReadBitFlipProbability = 1,
            MaxReadBitFlips = 1,
            Random = new SequenceRandom([0], [0]),
        };

        using var chaos = new ChaosStream(inner, options);
        var buffer = new byte[1];

        var read = chaos.Read(buffer);

        Assert.That(read, Is.EqualTo(1));
        Assert.That(buffer[0], Is.EqualTo(1));
    }

    [Test]
    public void WriteDropSkipsWritingToInnerStream()
    {
        using var inner = new RamStream();

        var options = new ChaosStreamOptions
        {
            DropWriteProbability = 1,
            Random = new SequenceRandom(doubles: [0]),
        };

        using var chaos = new ChaosStream(inner, options);
        chaos.Write([1, 2, 3]);

        Assert.That(inner.Length, Is.EqualTo(0));
    }

    [Test]
    public void WriteBitFlipCorruptsInnerStreamData()
    {
        using var inner = new RamStream();

        var options = new ChaosStreamOptions
        {
            WriteBitFlipProbability = 1,
            MaxWriteBitFlips = 1,
            Random = new SequenceRandom([0], [0]),
        };

        using var chaos = new ChaosStream(inner, options);
        chaos.Write([0]);

        var stored = ((IStreamR)inner).ToArray();
        Assert.That(stored, Has.Length.EqualTo(1));
        Assert.That(stored[0], Is.EqualTo(1));
    }

    [Test]
    public void ReadFailureThrowsIoException()
    {
        using var inner = new RamStream([1]);

        var options = new ChaosStreamOptions
        {
            ReadFailureProbability = 1,
            Random = new SequenceRandom(doubles: [0]),
        };

        using var chaos = new ChaosStream(inner, options);

        Assert.Throws<IOException>(() => chaos.Read(new byte[1]));
    }

    [Test]
    public async Task DelaySchedulerReceivesComputedDelayAsync()
    {
        await using var inner = new RamAsyncStream([1]);
        var recorded = new ConcurrentQueue<TimeSpan>();

        var options = new ChaosStreamOptions
        {
            MinReadDelay = TimeSpan.FromMilliseconds(5),
            MaxReadDelay = TimeSpan.FromMilliseconds(5),
            ReadDelayProbability = 1,
            Random = new SequenceRandom(doubles: [0]),
            DelayScheduler = (delay, _) =>
            {
                recorded.Enqueue(delay);

                return ValueTask.CompletedTask;
            },
        };

        await using var chaos = new ChaosAsyncStream(inner, options);
        var buffer = new byte[1];

        var read = await chaos.ReadAsync(buffer, CancelToken.None);

        Assert.That(read, Is.EqualTo(1));
        Assert.That(recorded.TryDequeue(out var captured), Is.True);
        Assert.That(captured, Is.EqualTo(TimeSpan.FromMilliseconds(5)));
        Assert.That(recorded.IsEmpty, Is.True);
    }

    [Test]
    public void StallDurationProviderTriggersDelay()
    {
        using var inner = new RamStream([1]);
        var captured = new List<TimeSpan>();

        var options = new ChaosStreamOptions
        {
            StallDurationProvider = _ => TimeSpan.FromMilliseconds(12),
            DelayScheduler = (delay, _) =>
            {
                captured.Add(delay);

                return ValueTask.CompletedTask;
            },
            ReadDelayProbability = 0,
            WriteDelayProbability = 0,
        };

        using var chaos = new ChaosStream(inner, options);
        var buffer = new byte[1];

        var read = chaos.Read(buffer);

        Assert.That(read, Is.EqualTo(1));
        Assert.That(captured, Has.Count.EqualTo(1));
        Assert.That(captured[0], Is.EqualTo(TimeSpan.FromMilliseconds(12)));
    }

    [Test]
    public void DisconnectionStopsFurtherReads()
    {
        using var inner = new RamStream([1, 2, 3]);

        var options = new ChaosStreamOptions
        {
            ShouldDisconnect = context => context.Operation == ChaosOperation.Read && context.OperationNumber == 2,
            DisconnectMessage = "Simulated disconnect",
            ReadDelayProbability = 0,
        };

        using var chaos = new ChaosStream(inner, options);
        var buffer = new byte[1];

        var first = chaos.Read(buffer);
        Assert.That(first, Is.EqualTo(1));

        var ex = Assert.Throws<IOException>(() => chaos.Read(buffer))!;
        Assert.That(ex.Message, Is.EqualTo("Simulated disconnect"));

        Assert.Throws<IOException>(() => chaos.Read(buffer));
    }

    [Test]
    public void PerByteDelayAddsExpectedLatencyOnWrite()
    {
        using var inner = new RamStream();
        var captured = new List<TimeSpan>();

        var options = new ChaosStreamOptions
        {
            WritePerByteDelay = TimeSpan.FromMilliseconds(4),
            DelayScheduler = (delay, _) =>
            {
                captured.Add(delay);

                return ValueTask.CompletedTask;
            },
            WriteDelayProbability = 0,
        };

        using var chaos = new ChaosStream(inner, options);
        chaos.Write([0x01, 0x02, 0x03]);

        Assert.That(captured, Has.Count.EqualTo(1));
        Assert.That(captured[0], Is.EqualTo(TimeSpan.FromMilliseconds(12)));
    }

    [Test]
    public void ReadNoiseRespectsOperationFilter()
    {
        using var inner = new RamStream([0x00, 0x00, 0x00]);

        var options = new ChaosStreamOptions
        {
            ReadBitFlipProbability = 1,
            MaxReadBitFlips = 1,
            Random = new SequenceRandom([0], [0]),
            ShouldApplyReadNoise = context => context.OperationNumber == 2,
        };

        using var chaos = new ChaosStream(inner, options);
        var buffer = new byte[1];

        buffer[0] = 0;
        var first = chaos.Read(buffer);
        Assert.That(first, Is.EqualTo(1));
        Assert.That(buffer[0], Is.EqualTo(0x00));

        buffer[0] = 0;
        var second = chaos.Read(buffer);
        Assert.That(second, Is.EqualTo(1));
        Assert.That(buffer[0], Is.EqualTo(0x01));

        buffer[0] = 0;
        var third = chaos.Read(buffer);
        Assert.That(third, Is.EqualTo(1));
        Assert.That(buffer[0], Is.EqualTo(0x00));
    }

    [Test]
    public void GlobalNoiseFilterUsesElapsedTime()
    {
        using IStreamRw inner = new RamStream();
        var time = new ManualTimeProvider();

        var options = new ChaosStreamOptions
        {
            WriteBitFlipProbability = 1,
            MaxWriteBitFlips = 1,
            Random = new SequenceRandom([0], [0]),
            ShouldApplyNoise = context => context.Metrics.Elapsed >= TimeSpan.FromSeconds(1),
            TimeProvider = time,
        };

        using var chaos = new ChaosStream(inner, options);
        chaos.Write([0x00]);
        Assert.That(inner.ToArray(), Is.EqualTo(new byte[] { 0x00 }));

        time.Advance(TimeSpan.FromSeconds(2));
        chaos.Write([0x00]);

        Assert.That(inner.ToArray(), Is.EqualTo(new byte[] { 0x00, 0x01 }));
    }

    [Test]
    public void WriteNoiseCanDependOnBytesWritten()
    {
        using IStreamRw inner = new RamStream();

        var options = new ChaosStreamOptions
        {
            WriteBitFlipProbability = 1,
            MaxWriteBitFlips = 1,
            Random = new SequenceRandom([0], [0]),
            ShouldApplyWriteNoise = context => context.Metrics.BytesWritten >= 4,
        };

        using var chaos = new ChaosStream(inner, options);
        chaos.Write([0x00, 0x00]);
        chaos.Write([0x00, 0x00]);
        chaos.Write([0x00]);

        Assert.That(inner.ToArray(), Is.EqualTo(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x01 }));
    }

    [Test]
    public void ReadBitFlipFlipsSpecificBits()
    {
        using var inner = new RamStream([0x00, 0x00]);

        var options = new ChaosStreamOptions
        {
            ReadBitFlipProbability = 1,
            MaxReadBitFlips = 3,
            Random = new SequenceRandom([0], [2, 0, 15]),
        };

        using var chaos = new ChaosStream(inner, options);
        var buffer = new byte[2];

        var read = chaos.Read(buffer);

        Assert.That(read, Is.EqualTo(2));
        Assert.That(buffer, Is.EqualTo(new byte[] { 0x01, 0x80 }));
    }

    [Test]
    public void WriteBitFlipFlipsSpecificBits()
    {
        using IStreamRw inner = new RamStream();

        var options = new ChaosStreamOptions
        {
            WriteBitFlipProbability = 1,
            MaxWriteBitFlips = 3,
            Random = new SequenceRandom([0], [2, 5, 10]),
        };

        using var chaos = new ChaosStream(inner, options);
        chaos.Write("\0\0"u8);

        var stored = inner.ToArray();
        Assert.That(stored, Is.EqualTo(new byte[] { 0x20, 0x04 }));
    }

    [Test]
    public void WriteTruncationDropsExpectedBytes()
    {
        using IStreamRw inner = new RamStream();

        var options = new ChaosStreamOptions
        {
            WriteTruncationProbability = 1,
            MaxWriteTruncationBytes = 4,
            Random = new SequenceRandom([0], [3]),
        };

        using var chaos = new ChaosStream(inner, options);
        chaos.Write([1, 2, 3, 4, 5]);

        var stored = inner.ToArray();
        Assert.That(stored, Is.EqualTo(new byte[] { 1, 2 }));
    }

    [Test]
    public void ReadTruncationDoesNotExceedMaxSetting()
    {
        using var inner = new RamStream([1, 2, 3, 4, 5]);

        var options = new ChaosStreamOptions
        {
            ReadTruncationProbability = 1,
            MaxReadTruncationBytes = 2,
            Random = new SequenceRandom([0], [5]),
        };

        using var chaos = new ChaosStream(inner, options);
        var buffer = new byte[5];

        var read = chaos.Read(buffer);

        Assert.That(read, Is.EqualTo(3));
        Assert.That(inner.Position, Is.EqualTo(5));
        Assert.That(buffer.AsSpan(0, read).ToArray(), Is.EqualTo(new byte[] { 1, 2, 3 }));
    }

    [Test]
    public void DropWriteProbabilityZeroKeepsPayload()
    {
        using IStreamRw inner = new RamStream();

        var options = new ChaosStreamOptions
        {
            DropWriteProbability = 0.5,
            Random = new SequenceRandom(doubles: [0.75]),
        };

        using var chaos = new ChaosStream(inner, options);
        chaos.Write([7, 8, 9]);

        Assert.That(inner.ToArray(), Is.EqualTo(new byte[] { 7, 8, 9 }));
    }

    private sealed class SequenceRandom(IEnumerable<double>? doubles = null, IEnumerable<int>? ints = null) : Random
    {
        private readonly Queue<double> doubles = new(doubles ?? []);
        private readonly Queue<int> ints = new(ints ?? []);

        protected override double Sample() => this.doubles.Count > 0 ? this.doubles.Dequeue() : base.Sample();

        public override int Next(int minValue, int maxValue)
        {
            if (this.ints.Count > 0)
            {
                var value = this.ints.Dequeue();

                if (value < minValue)
                    return minValue;

                if (value >= maxValue)
                    return maxValue - 1;

                return value;
            }

            return base.Next(minValue, maxValue);
        }
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => Volatile.Read(ref this.ticks);

        public override DateTimeOffset GetUtcNow()
        {
            var ticks = Volatile.Read(ref this.ticks);

            return DateTimeOffset.UnixEpoch.AddTicks(ticks);
        }

        public void Advance(TimeSpan value) => Interlocked.Add(ref this.ticks, value.Ticks);
    }
}
