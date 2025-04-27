// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Text;
using Itexoft.Networking.Reliable;
using Itexoft.Networking.Reliable.Internal;

namespace Itexoft.Tests.Reliable;

public class ReliableStreamTests
{
    [Test]
    public async Task WriteBlocksUntilAcknowledged()
    {
        var (client, server) = MemoryReliableStreamTransport.CreatePair();
        var writer = new ReliableStreamWriter(client);
        var reader = new ReliableStreamReader(server);

        var payload = Encoding.UTF8.GetBytes("hello");
        var writeTask = writer.WriteAsync(payload).AsTask();

        Assert.IsFalse(writeTask.IsCompleted);

        var buffer = new byte[payload.Length];
#pragma warning disable CA2022
        var read = await reader.ReadAsync(buffer);
#pragma warning restore CA2022
        Assert.AreEqual(payload.Length, read);
        Assert.AreEqual(payload, buffer);

        await writeTask;
    }

    [Test]
    public async Task NonBlockingModeRequiresManualAcknowledgementProcessing()
    {
        var (client, server) = MemoryReliableStreamTransport.CreatePair();
        var options = new ReliableStreamOptions
        {
            EnsureDeliveredBeforeRelease = false
        };
        var writer = new ReliableStreamWriter(client, options);
        var reader = new ReliableStreamReader(server);

        var payload = Encoding.UTF8.GetBytes("data");
        await writer.WriteAsync(payload);

        var buffer = new byte[payload.Length];
#pragma warning disable CA2022
        var read = await reader.ReadAsync(buffer);
#pragma warning restore CA2022
        Assert.AreEqual(payload, buffer);

        var ackSequence = await writer.ProcessNextAcknowledgementAsync();
        Assert.AreEqual(0, ackSequence);

        var metrics = writer.GetMetrics();
        Assert.AreEqual(0, metrics.BufferedBytes);
        Assert.AreEqual(0, metrics.BufferedBlocks);
        Assert.AreEqual(0, metrics.LastAcknowledgedSequence);
        Assert.IsTrue(metrics.LastAcknowledgementLatencyMilliseconds >= 0);
    }

    [Test]
    public async Task BufferCapacityIsHonoured()
    {
        var (client, server) = MemoryReliableStreamTransport.CreatePair();
        var options = new ReliableStreamOptions
        {
            BufferCapacityBytes = 4,
            EnsureDeliveredBeforeRelease = false
        };
        var writer = new ReliableStreamWriter(client, options);
        var reader = new ReliableStreamReader(server);

        var payload = Encoding.UTF8.GetBytes("ping");
        await writer.WriteAsync(payload);

        var pending = writer.WriteAsync("pong"u8.ToArray()).AsTask();
        await Task.Delay(100);
        Assert.IsFalse(pending.IsCompleted);

#pragma warning disable CA2022
        await reader.ReadAsync(new byte[payload.Length]);
#pragma warning restore CA2022
        await writer.ProcessNextAcknowledgementAsync();

#pragma warning disable CA2022
        await reader.ReadAsync(new byte[payload.Length]);
#pragma warning restore CA2022
        await writer.ProcessNextAcknowledgementAsync();

        await pending;

        var metrics = writer.GetMetrics();
        Assert.AreEqual(0, metrics.BufferedBlocks);
    }
}