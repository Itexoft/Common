# Reliable TCP Connections

This folder provides a simplified client/server API that sits on top of the proven P2P runtime. It targets
scenarios where you want make-before-break reconnects, heartbeat supervision, retry budgets and sticky blacklists,
but you do not need the full peer-to-peer surface area.

## Components

| Type                                                    | Description                                                                     |
|---------------------------------------------------------|---------------------------------------------------------------------------------|
| `ReliableTcpClient`                                     | Maintains a single reliable connection to one of the configured endpoints.      |
| `ReliableTcpServer`                                     | Accepts incoming client connections and raises events with connection contexts. |
| `ReliableTcpClientOptions` / `ReliableTcpServerOptions` | Configuration for heartbeat intervals, retry policies, metadata, etc.           |
| `ReliableTcpEndpoint`                                   | Describes a TCP target (host, port, priority, optional TLS).                    |
| `ReliableConnectionMetrics`                             | Snapshot of telemetry exported by the client (SRTT, φ, retry counts).           |
| `ReliableConnectionState`                               | Lifecycle states emitted by the client manager.                                 |
| `ReliableStreamWriter` / `ReliableStreamReader`         | Buffering stream wrappers with acknowledgement-based delivery guarantees.       |
| `ReliableStreamOptions`                                 | Tunable parameters for buffering and delivery guarantees.                       |
| `P2PReliableStreamTransport`                            | Bridge that exposes a P2P connection as an `IReliableStreamTransport`.          |

## Reliable Streams

```csharp
IReliableStreamTransport clientTransport = /* obtain from connection layer */;
IReliableStreamTransport serverTransport = /* obtain from connection layer */;
var writer = new ReliableStreamWriter(clientTransport);
var reader = new ReliableStreamReader(serverTransport);

await writer.WriteAsync("important"u8.ToArray()); // blocks until the reader acknowledges

var buffer = new byte[16];
var read = await reader.ReadAsync(buffer);
Console.WriteLine(Encoding.UTF8.GetString(buffer, 0, read));
```

By default `ReliableStreamWriter` keeps a bounded buffer (configured via `ReliableStreamOptions.BufferCapacityBytes`
and acknowledged automatically when `EnsureDeliveredBeforeRelease = true`). If you disable automatic handling, call
`ProcessNextAcknowledgementAsync()` from your own background task to acknowledge blocks and free buffer space. Use
`GetMetrics()` on the writer to monitor current queue size and acknowledgement latency.

## Example

```csharp
var serverOptions = new ReliableTcpServerOptions { Port = 4500, ServerId = "server" };
await using var server = new ReliableTcpServer(serverOptions);
server.ConnectionAccepted += async ctx =>
{
    using var reader = new StreamReader(ctx.DataStream, Encoding.UTF8, leaveOpen: true);
    await using var writer = new StreamWriter(ctx.DataStream, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };
    var line = await reader.ReadLineAsync();
    await writer.WriteLineAsync($"echo: {line}");
};
await server.StartAsync();

var clientOptions = new ReliableTcpClientOptions("client-1", new[] { new ReliableTcpEndpoint("localhost", 4500) });
await using var client = new ReliableTcpClient(clientOptions);
var handle = await client.ConnectAsync();
await using var writer = new StreamWriter(handle.DataStream, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };
using var reader = new StreamReader(handle.DataStream, Encoding.UTF8, leaveOpen: true);
await writer.WriteLineAsync("hello");
Console.WriteLine(await reader.ReadLineAsync()); // echo: hello

await client.DisconnectAsync();
await server.StopAsync();
```

The client inherits all resilience features from the underlying P2P manager: hedged dialing, sticky blacklist,
retry budgets, φ-based heartbeat detection and make-before-break reconnection.
