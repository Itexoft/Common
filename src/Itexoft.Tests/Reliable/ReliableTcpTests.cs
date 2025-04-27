// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Itexoft.Networking.Reliable;

namespace Itexoft.Tests.Reliable;

public class ReliableTcpTests
{
    [Test]
    public async Task ReliableTcpClientServer_Roundtrip()
    {
        var serverOptions = new ReliableTcpServerOptions
        {
            Port = 0,
            ServerId = "server-node"
        };

        await using var server = new ReliableTcpServer(serverOptions);
        var receivedTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        server.ConnectionAccepted += async context =>
        {
            await using var writer = new StreamWriter(context.DataStream, Encoding.UTF8, 1024, true)
            {
                AutoFlush = true
            };
            using var reader = new StreamReader(context.DataStream, Encoding.UTF8, leaveOpen: true);
            var line = await reader.ReadLineAsync();
            receivedTcs.TrySetResult(line ?? string.Empty);
            await writer.WriteLineAsync("pong");
        };

        await server.StartAsync();
        var port = ((IPEndPoint)server.LocalEndPoint!).Port;

        var clientOptions = new ReliableTcpClientOptions("client-node", [new("127.0.0.1", port)]);
        await using var client = new ReliableTcpClient(clientOptions);
        var handle = await client.ConnectAsync();

        await using var clientWriter = new StreamWriter(handle.DataStream, Encoding.UTF8, 1024, true)
        {
            AutoFlush = true
        };
        using var clientReader = new StreamReader(handle.DataStream, Encoding.UTF8, leaveOpen: true);

        await clientWriter.WriteLineAsync("ping");
        var response = await clientReader.ReadLineAsync();

        Assert.AreEqual("ping", await receivedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.AreEqual("pong", response);

        await client.DisconnectAsync();
        await server.StopAsync();
    }

    [Test]
    public async Task ReliableTcpServer_AcceptsMultipleClients()
    {
        var serverOptions = new ReliableTcpServerOptions
        {
            Port = 0,
            ServerId = "multi-server"
        };

        await using var server = new ReliableTcpServer(serverOptions);
        var connected = new ConcurrentBag<string>();
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        server.ConnectionAccepted += context =>
        {
            connected.Add(context.RemoteNodeId);
            if (connected.Count >= 3)
                completion.TrySetResult(true);

            return Task.CompletedTask;
        };

        await server.StartAsync();
        var port = ((IPEndPoint)server.LocalEndPoint!).Port;

        var tasks = new Task[3];
        for (var i = 0; i < tasks.Length; i++)
        {
            var options = new ReliableTcpClientOptions($"client-{i}", [new("127.0.0.1", port)]);
            tasks[i] = ConnectAndDisconnectAsync(options);
        }

        await Task.WhenAll(tasks);
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await server.StopAsync();
    }

    private static async Task ConnectAndDisconnectAsync(ReliableTcpClientOptions options)
    {
        await using var client = new ReliableTcpClient(options);
        await client.ConnectAsync();
        await client.DisconnectAsync();
    }
}