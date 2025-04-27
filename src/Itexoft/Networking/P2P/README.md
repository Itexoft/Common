# P2P Transport & Connection Manager

This directory contains a reusable peer-to-peer stack. It helps independent processes establish TCP-based links, detect
failures quickly, and recover without manual intervention.

## What Problem Does It Solve?

When several autonomous tasks must exchange data or coordinate work, they need:

- a way to dial peers over multiple endpoints (IPv4/IPv6, loopback, relay);
- quick failure detection (no waiting for OS keep-alive);
- automatic reconnection without packet loss;
- shared “cooling-off” periods so peers do not overload unstable nodes.

The classes in this folder provide those capabilities out of the box.

## Components at a Glance

| Layer                | Responsibility                                 | Key Types                                                                |
|----------------------|------------------------------------------------|--------------------------------------------------------------------------|
| Transport adapters   | Knows how to open control/data sockets         | `IP2PTransportAdapter`, `TcpP2PTransportAdapter`, `P2PTransportEndpoint` |
| Connection manager   | Drives dialing, handshakes, make-before-break  | `P2PConnectionManager`, `P2PConnectionHandle`, `P2PConnectionOptions`    |
| Liveness & telemetry | Heartbeats, φ-detector, retry budgets          | `P2PControlFrame`, `FailureSeverity`, `RetryBudget`                      |
| Shared penalties     | Shares Retry-After hints across local peers    | `SharedBlacklistRegistry`                                                |
| Hosting              | Accepts inbound connections and pairs channels | `P2PConnectionHost`, `P2PTransportConnection`                            |

## How It Works (Plain Language)

1. **Dial strategy.** `P2PConnectionManager` looks at the endpoint list and fires hedged dials (Happy-Eyeballs). Failed
   endpoints are temporarily blacklisted so other peers do not hammer them.
2. **Handshake.** Once control/data sockets are open, both sides exchange JSON envelopes (`P2PHandshake`) describing
   node IDs, session epochs, and optional resume tokens.
3. **Heartbeat loop.** A background loop sends ping/pong frames or piggybacks acknowledgements onto user traffic.
   φ-detector and write probes decide when the link is unhealthy.
4. **Make-before-break.** During reconnects a shadow connection is established first. The old stream stays alive until
   the new one is ready, so no bytes are lost.
5. **Shared blacklist.** When any manager encounters Retry-After or repeated failures, it updates
   `SharedBlacklistRegistry`. All managers in the same process respect that penalty.

## When to Use

- Building an automation agent that must watch multiple long-lived processes.
- Synchronising background workers spread across several binaries on the same host.
- Creating a mock peer-to-peer testbed where resilience (reconnects, jitter, chaos) is required.
- Any scenario where TCP connections must be monitored and kept alive without writing handshake/liveness code from
  scratch.

## Quick Start

```csharp
using var listener = await P2PConnectionHost
    .StartAsync(new P2PConnectionHostOptions
    {
        NodeId = "server-1",
        TransportListenerOptions = new TcpP2PTransportListenerOptions
        {
            Port = 4500
        }
    });

var options = new P2PConnectionOptions(
    nodeId: "client-1",
    endpoints: new[]
    {
        new P2PTransportEndpoint("127.0.0.1", 4500, transport: P2PTransportNames.Tcp)
    },
    heartbeatInterval: TimeSpan.FromMilliseconds(250),
    heartbeatTimeout: TimeSpan.FromSeconds(2))
{
    EndpointBlacklistDuration = TimeSpan.FromSeconds(10),
    MaxConcurrentDials = 2
};

await using var manager = new P2PConnectionManager(options, new TcpP2PTransportAdapter());
manager.ConnectionStateChanged += (_, args) =>
    Console.WriteLine($"{args.PreviousState} -> {args.CurrentState} ({args.Cause})");

var connection = await manager.ConnectAsync();
await connection.DataStream.WriteAsync(Encoding.UTF8.GetBytes("hello, peer!\n"));

// Later
var metrics = manager.GetMetrics();
Console.WriteLine($"Last RTT (ms): {metrics.SrttMilliseconds:F2}");
await manager.DisconnectAsync();
```

### Sticky blacklist example

```csharp
SharedBlacklistRegistry.SetPenalty(
    key: "tcp:127.0.0.1:4500",
    severity: FailureSeverity.Hard,
    duration: TimeSpan.FromSeconds(15));
```

Managers subscribe automatically; penalties expire via TTL, and pruning is throttled to avoid lock contention.

## Connection Management Features

- **Happy-Eyeballs & priority groups** — hedge dial attempts with limited concurrency and per-priority delays.
- **Make-before-break** — `ReconnectAsync` keeps the old transport alive until the new one confirms handshakes.
- **Heartbeat + φ-detector** — adaptive intervals with jitter plus write probes before declaring failure.
- **Retry budgets and sticky blacklists** — prevent dial storms across processes and respect peer-supplied Retry-After
  hints.
- **Metrics & diagnostics** — `P2PConnectionMetrics` exposes SRTT, φ, dial rates, last failure severity, heartbeat
  percentiles, etc.

## Testing & Chaos Utilities

- `TcpP2PTransportAdapter` powers real loopback verification.
- `ChaosStream` and `ChaosTcpTransportAdapter` simulate latency, drops, half-open sockets, and heavy jitter.
- `SharedBlacklist_PruneRemovesExpiredEntries` and related tests demonstrate how TTL cleanup behaves after crashes.

See `src/Itexoft.Common.Tests/P2P` for runnable scenarios:

- `HappyEyeballs_*` — multi-endpoint hedging.
- `ChaosTcpAdapter_*` — resilience under disconnections.
- `DiagnosticsBroadcast_*` — sticky blacklist broadcasts (frame `0xB1`).

## Integration Guidelines

1. **Configure endpoints explicitly.** `P2PConnectionOptions` requires at least one endpoint with a transport
   identifier.
2. **Dispose managers.** Always wrap `P2PConnectionManager` in `await using` (it stops heartbeat loops and releases
   sockets).
3. **Handle metrics periodically.** Poll `GetMetrics()` to emit observability data or trigger manual recovery.
4. **Reset shared penalties** during unit tests via `SharedBlacklistRegistry.Clear()` to avoid cross-test interference.
5. **Watch dial budgets** when adding custom adapters — reuse `RetryBudget` and `EndpointDialTracker` utilities to stay
   consistent.
