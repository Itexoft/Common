// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Itexoft.IO;
using Itexoft.Networking.Core;
using Itexoft.Networking.P2P;
using Itexoft.Networking.P2P.Adapters;

namespace Itexoft.Tests.P2P;

public class P2PConnectionManagerTests
{
    [Test]
    public async Task ConnectAsync_StubAdapter_TransitionsToEstablishedAndTracksHeartbeat()
    {
        var registry = new P2PTransportAdapterRegistry(
            [("stub", (IP2PTransportAdapter)new StubTransportAdapter("remote-node"))]);

        var options = new P2PConnectionOptions(
            "local-node",
            [
                new(
                    "stub-host",
                    1000,
                    label: "stub-endpoint",
                    transport: "stub")
            ],
            heartbeatInterval: TimeSpan.FromMilliseconds(120),
            heartbeatTimeout: TimeSpan.FromMilliseconds(400));

        var stateHistory = new ConcurrentQueue<string>();

        await using var manager = new P2PConnectionManager(options, registry);

        manager.ConnectionStateChanged += (_, args) =>
        {
            stateHistory.Enqueue($"{args.PreviousState}->{args.CurrentState}:{args.Cause}:{args.Evidence}");
        };

        var handle = await manager.ConnectAsync();
        Assert.AreEqual("remote-node", handle.RemoteNodeId);
        Assert.AreEqual("stub-endpoint", handle.Endpoint.Label);
        Assert.IsFalse(string.IsNullOrWhiteSpace(handle.LocalSessionId));

        Assert.IsTrue(
            await WaitForFreshHeartbeatAsync(manager, options.HeartbeatTimeout, TimeSpan.FromSeconds(5)),
            "Heartbeat ack did not refresh within the expected timeout.");
        Assert.IsTrue(
            await WaitForConditionAsync(() => manager.State == P2PConnectionState.Established, TimeSpan.FromSeconds(5)),
            "Менеджер не вернулся в состояние Established.");
    }

    [Test]
    public async Task Registry_ResolvesRegisteredAdapter_ByTransportName()
    {
        var adapter = new StubTransportAdapter("node");
        var registry = new P2PTransportAdapterRegistry(
            [(P2PTransportNames.Tcp, (IP2PTransportAdapter)new TcpP2PTransportAdapter()), ("stub", (IP2PTransportAdapter)adapter)]);

        var resolved = registry.Resolve(new("h", 1, transport: "stub"));
        Assert.AreSame(adapter, resolved);
        await Task.CompletedTask;
    }

    [Test]
    public async Task HappyEyeballs_SecondaryEndpointWinsWhenPrimaryStalls()
    {
        var attempts = new ConcurrentQueue<(string Label, DateTimeOffset Timestamp)>();
        var adapter = new ProgrammableTransportAdapter(
            endpoint => { attempts.Enqueue((endpoint.Label ?? $"{endpoint.Host}:{endpoint.Port}", DateTimeOffset.UtcNow)); },
            async (endpoint, options, cancellationToken) =>
            {
                switch (endpoint.Label)
                {
                    case "primary":
                        await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);

                        throw new SocketException((int)SocketError.TimedOut);
                    case "secondary":
                        await Task.Delay(TimeSpan.FromMilliseconds(40), cancellationToken).ConfigureAwait(false);

                        return CreateStubTransportConnection(endpoint, cancellationToken, "remote-secondary");
                    default:
                        throw new InvalidOperationException("Unexpected endpoint label.");
                }
            });

        var registry = new P2PTransportAdapterRegistry(
            [("prog", (IP2PTransportAdapter)adapter)]);

        var options = new P2PConnectionOptions(
            "happy-client",
            [
                new("primary.example", 4100, label: "primary", transport: "prog"),
                new("secondary.example", 4200, label: "secondary", transport: "prog")
            ],
            TimeSpan.FromSeconds(3),
            TimeSpan.FromMilliseconds(30),
            heartbeatInterval: TimeSpan.FromMilliseconds(200),
            heartbeatTimeout: TimeSpan.FromMilliseconds(800),
            maxConcurrentDials: 2);

        await using var manager = new P2PConnectionManager(options, registry);
        var handle = await manager.ConnectAsync();

        Assert.AreEqual("remote-secondary", handle.RemoteNodeId);
        Assert.AreEqual("secondary", handle.Endpoint.Label);

        Assert.IsTrue(attempts.Count >= 2, "Менеджер не инициировал две попытки dial.");
        Assert.IsTrue(attempts.TryDequeue(out var first), "Первая попытка dial не зафиксирована.");
        Assert.IsTrue(attempts.TryDequeue(out var second), "Вторая попытка dial не зафиксирована.");
        Assert.AreEqual("primary", first.Label);
        Assert.AreEqual("secondary", second.Label);
        var delta = (second.Timestamp - first.Timestamp).TotalMilliseconds;
        Assert.That(delta, Is.InRange(0, 200));

        await manager.DisconnectAsync();
    }

    [Test]
    public async Task GetMetrics_ReportsDialRateSnapshotsAndBlacklist()
    {
        var adapter = new ProgrammableTransportAdapter(
            null,
            async (endpoint, options, cancellationToken) =>
            {
                if (endpoint.Label == "primary")
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(60)).ConfigureAwait(false);

                    throw new SocketException((int)SocketError.TimedOut);
                }

                await Task.Delay(TimeSpan.FromMilliseconds(120), cancellationToken).ConfigureAwait(false);

                return CreateStubTransportConnection(endpoint, cancellationToken, "metrics-remote");
            });

        var registry = new P2PTransportAdapterRegistry(
            [("prog", (IP2PTransportAdapter)adapter)]);

        var options = new P2PConnectionOptions(
            "metrics-client",
            [
                new("primary.example", 5100, label: "primary", transport: "prog"),
                new("secondary.example", 5200, label: "secondary", transport: "prog")
            ],
            TimeSpan.FromSeconds(3),
            TimeSpan.FromMilliseconds(20),
            heartbeatInterval: TimeSpan.FromMilliseconds(200),
            heartbeatTimeout: TimeSpan.FromMilliseconds(800),
            maxConcurrentDials: 2)
        {
            MaxDialsPerMinutePerEndpoint = 10,
            EndpointBlacklistDuration = TimeSpan.FromSeconds(5)
        };

        await using var manager = new P2PConnectionManager(options, registry);
        await manager.ConnectAsync();

        var metrics = manager.GetMetrics();
        Assert.IsTrue(metrics.TotalDialAttemptsLastMinute >= 2, "Неверное число dial-попыток в метриках.");
        Assert.That(metrics.BlacklistedEndpointCount, Is.InRange(1, 1));
        Assert.IsNotEmpty(metrics.DialRates);

        var primaryEntry = metrics.DialRates.First(rate => rate.EndpointKey.Contains("primary.example", StringComparison.Ordinal));
        Assert.IsTrue(primaryEntry.AttemptsLastMinute >= 1);
        Assert.IsTrue(primaryEntry.BlacklistedUntilUtc > DateTimeOffset.UtcNow);

        var secondaryEntry = metrics.DialRates.First(rate => rate.EndpointKey.Contains("secondary.example", StringComparison.Ordinal));
        Assert.IsTrue(secondaryEntry.AttemptsLastMinute >= 1);
        Assert.AreEqual(DateTimeOffset.MinValue, secondaryEntry.BlacklistedUntilUtc);

        await manager.DisconnectAsync();
    }

    [Test]
    public async Task HappyEyeballs_RespectsMaxConcurrentDials()
    {
        var attemptsStarted = 0;
        var maxObserved = 0;
        var currentActive = 0;
        var startedTwo = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFailures = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        void TrackActive(int active)
        {
            while (true)
            {
                var snapshot = Volatile.Read(ref maxObserved);

                if (active <= snapshot)
                    return;

                if (Interlocked.CompareExchange(ref maxObserved, active, snapshot) == snapshot)
                    return;
            }
        }

        var adapter = new ProgrammableTransportAdapter(
            endpoint =>
            {
                var started = Interlocked.Increment(ref attemptsStarted);
                if (started == 2)
                    startedTwo.TrySetResult(true);
            },
            async (endpoint, options, cancellationToken) =>
            {
                var active = Interlocked.Increment(ref currentActive);
                TrackActive(active);
                try
                {
                    if (endpoint.Label is "fail-1" or "fail-2")
                    {
                        await releaseFailures.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

                        throw new SocketException((int)SocketError.TimedOut);
                    }

                    await Task.Delay(TimeSpan.FromMilliseconds(40), cancellationToken).ConfigureAwait(false);

                    return CreateStubTransportConnection(endpoint, cancellationToken, "success-remote");
                }
                finally
                {
                    Interlocked.Decrement(ref currentActive);
                }
            });

        var registry = new P2PTransportAdapterRegistry(
            [("prog", (IP2PTransportAdapter)adapter)]);

        var options = new P2PConnectionOptions(
            "max-concurrent-client",
            [
                new("fail1.example", 6001, label: "fail-1", transport: "prog"),
                new("fail2.example", 6002, label: "fail-2", transport: "prog"),
                new("success.example", 6003, label: "success", transport: "prog")
            ],
            TimeSpan.FromSeconds(4),
            TimeSpan.FromMilliseconds(25),
            heartbeatInterval: TimeSpan.FromMilliseconds(200),
            heartbeatTimeout: TimeSpan.FromMilliseconds(800),
            maxConcurrentDials: 2)
        {
            EndpointBlacklistDuration = TimeSpan.FromMilliseconds(200)
        };

        await using var manager = new P2PConnectionManager(options, registry);
        var connectTask = manager.ConnectAsync();

        Assert.IsTrue(
            await startedTwo.Task.WaitAsync(TimeSpan.FromSeconds(2)),
            "Две первые попытки не стартовали своевременно.");
        Assert.AreEqual(2, Volatile.Read(ref attemptsStarted));
        Assert.IsFalse(connectTask.IsCompleted, "Подключение завершилось до переключения на успешный эндпоинт.");

        releaseFailures.TrySetResult(true);
        var handle = await connectTask;
        Assert.AreEqual("success", handle.Endpoint.Label);
        Assert.IsTrue(Volatile.Read(ref attemptsStarted) >= 3);
        Assert.IsTrue(Volatile.Read(ref maxObserved) <= options.MaxConcurrentDials, $"maxObserved={maxObserved}");

        await manager.DisconnectAsync();
    }

    [Test]
    public async Task TcpAdapter_LoopbackHandshakeSucceeds()
    {
        var hostOptions = new P2PConnectionHostOptions
        {
            NodeId = "tcp-server",
            TransportListenerOptions = new()
            {
                Address = IPAddress.Loopback,
                Port = 0
            }
        };

        await using var host = new P2PConnectionHost(hostOptions);
        var acceptedTcs = new TaskCompletionSource<P2PConnectionAcceptedContext>(TaskCreationOptions.RunContinuationsAsynchronously);
        host.ConnectionAccepted += context =>
        {
            acceptedTcs.TrySetResult(context);

            return Task.CompletedTask;
        };

        await host.StartAsync();
        var endPoint = (IPEndPoint?)host.LocalEndPoint ?? throw new InvalidOperationException("Listener not started.");

        var registry = new P2PTransportAdapterRegistry(
            [(P2PTransportNames.Tcp, (IP2PTransportAdapter)new TcpP2PTransportAdapter())]);

        var options = new P2PConnectionOptions(
            "tcp-client",
            [new(endPoint.Address.ToString(), endPoint.Port, label: "primary", transport: P2PTransportNames.Tcp)],
            TimeSpan.FromSeconds(5),
            TimeSpan.FromMilliseconds(50),
            heartbeatInterval: TimeSpan.FromMilliseconds(220),
            heartbeatTimeout: TimeSpan.FromMilliseconds(900));

        await using var manager = new P2PConnectionManager(options, registry);
        var handle = await manager.ConnectAsync();

        var accepted = await acceptedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual("tcp-client", accepted.RemoteNodeId);
        Assert.AreEqual(endPoint.Port, handle.Endpoint.Port);
        Assert.AreEqual("tcp-server", handle.RemoteNodeId);

        await manager.DisconnectAsync();
        await host.StopAsync();
    }

    [Test]
    public async Task TcpAdapter_FallbackBetweenLoopbackPorts()
    {
        var unusedListener = new TcpListener(IPAddress.Loopback, 0);
        unusedListener.Start();
        var unusedPort = ((IPEndPoint)unusedListener.LocalEndpoint).Port;
        unusedListener.Stop();

        var hostOptions = new P2PConnectionHostOptions
        {
            NodeId = "tcp-server-fallback",
            TransportListenerOptions = new()
            {
                Address = IPAddress.Loopback,
                Port = 0
            }
        };

        await using var host = new P2PConnectionHost(hostOptions);
        var acceptedTcs = new TaskCompletionSource<P2PConnectionAcceptedContext>(TaskCreationOptions.RunContinuationsAsynchronously);
        host.ConnectionAccepted += context =>
        {
            acceptedTcs.TrySetResult(context);

            return Task.CompletedTask;
        };

        await host.StartAsync();
        var activeEndPoint = (IPEndPoint?)host.LocalEndPoint ?? throw new InvalidOperationException("Listener not started.");

        var registry = new P2PTransportAdapterRegistry(
            [(P2PTransportNames.Tcp, (IP2PTransportAdapter)new TcpP2PTransportAdapter())]);

        var options = new P2PConnectionOptions(
            "tcp-client-fallback",
            [
                new("127.0.0.1", unusedPort, label: "closed", transport: P2PTransportNames.Tcp, priority: 0),
                new(
                    activeEndPoint.Address.ToString(),
                    activeEndPoint.Port,
                    label: "active",
                    transport: P2PTransportNames.Tcp,
                    priority: 0)
            ],
            TimeSpan.FromSeconds(5),
            TimeSpan.FromMilliseconds(50),
            heartbeatInterval: TimeSpan.FromMilliseconds(220),
            heartbeatTimeout: TimeSpan.FromMilliseconds(900));

        await using var manager = new P2PConnectionManager(options, registry);
        var handle = await manager.ConnectAsync();
        Assert.AreEqual(activeEndPoint.Port, handle.Endpoint.Port);
        Assert.AreEqual("tcp-server-fallback", handle.RemoteNodeId);

        var accepted = await acceptedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual("tcp-client-fallback", accepted.RemoteNodeId);

        await manager.DisconnectAsync();
        await host.StopAsync();
    }

    [Test]
    public async Task HappyEyeballs_PrimaryTimeoutsSecondarySucceeds()
    {
        var attempts = new ConcurrentQueue<string>();
        var adapter = new ProgrammableTransportAdapter(
            endpoint => { attempts.Enqueue(endpoint.Label ?? endpoint.Host); },
            async (endpoint, options, cancellationToken) =>
            {
                if (endpoint.Label == "primary")
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(150), cancellationToken).ConfigureAwait(false);

                    throw new SocketException((int)SocketError.TimedOut);
                }

                await Task.Delay(TimeSpan.FromMilliseconds(40), cancellationToken).ConfigureAwait(false);

                return CreateStubTransportConnection(endpoint, cancellationToken, "secondary-remote");
            });

        var registry = new P2PTransportAdapterRegistry(
            [("prog", (IP2PTransportAdapter)adapter)]);

        var options = new P2PConnectionOptions(
            "he02-client",
            [
                new("primary.example", 8010, label: "primary", transport: "prog", priority: 0),
                new("secondary.example", 8020, label: "secondary", transport: "prog", priority: 0)
            ],
            TimeSpan.FromSeconds(4),
            TimeSpan.FromMilliseconds(30),
            heartbeatInterval: TimeSpan.FromMilliseconds(200),
            heartbeatTimeout: TimeSpan.FromMilliseconds(700),
            maxConcurrentDials: 2);

        await using var manager = new P2PConnectionManager(options, registry);
        var handle = await manager.ConnectAsync();

        Assert.AreEqual("secondary", handle.Endpoint.Label);
        Assert.IsTrue(attempts.Count >= 2);
        Assert.That(attempts, Does.Contain("primary"));
        Assert.That(attempts, Does.Contain("secondary"));

        await manager.DisconnectAsync();
    }

    [Test]
    public async Task HappyEyeballs_PrioritizesPreferredFamilyOverRelay()
    {
        var adapter = new ProgrammableTransportAdapter(
            null,
            async (endpoint, options, cancellationToken) =>
            {
                if (endpoint.Label == "ipv6")
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(80), cancellationToken).ConfigureAwait(false);

                    return CreateStubTransportConnection(endpoint, cancellationToken, "ipv6-remote");
                }

                if (endpoint.Label == "relay")
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(20), cancellationToken).ConfigureAwait(false);

                    return CreateStubTransportConnection(endpoint, cancellationToken, "relay-remote");
                }

                throw new InvalidOperationException("Unexpected endpoint label.");
            });

        var registry = new P2PTransportAdapterRegistry(
            [("prog", (IP2PTransportAdapter)adapter)]);

        var options = new P2PConnectionOptions(
            "he04-client",
            [
                new("ipv6.example", 8210, label: "ipv6", transport: "prog", priority: 0),
                new("relay.example", 8220, label: "relay", transport: "prog", priority: 2)
            ],
            TimeSpan.FromSeconds(4),
            TimeSpan.FromMilliseconds(25),
            heartbeatInterval: TimeSpan.FromMilliseconds(200),
            heartbeatTimeout: TimeSpan.FromMilliseconds(700),
            maxConcurrentDials: 2);

        await using var manager = new P2PConnectionManager(options, registry);
        var handle = await manager.ConnectAsync();

        Assert.AreEqual("ipv6", handle.Endpoint.Label);
        Assert.AreEqual("ipv6-remote", handle.RemoteNodeId);

        await manager.DisconnectAsync();
    }

    [Test]
    public async Task HappyEyeballs_RelayHandlesFullOutage()
    {
        var adapter = new ProgrammableTransportAdapter(
            null,
            async (endpoint, options, cancellationToken) =>
            {
                switch (endpoint.Label)
                {
                    case "ipv6":
                    case "ipv4":
                        await Task.Delay(TimeSpan.FromMilliseconds(60), cancellationToken).ConfigureAwait(false);

                        throw new SocketException((int)SocketError.TimedOut);
                    case "relay":
                        await Task.Delay(TimeSpan.FromMilliseconds(40), cancellationToken).ConfigureAwait(false);

                        return CreateStubTransportConnection(endpoint, cancellationToken, "relay-remote");
                    default:
                        throw new InvalidOperationException("Unexpected endpoint.");
                }
            });

        var registry = new P2PTransportAdapterRegistry(
            [("prog", (IP2PTransportAdapter)adapter)]);

        var options = new P2PConnectionOptions(
            "he04-fallback-client",
            [
                new("ipv6.example", 8310, label: "ipv6", transport: "prog", priority: 0),
                new("ipv4.example", 8320, label: "ipv4", transport: "prog", priority: 0),
                new("relay.example", 8330, label: "relay", transport: "prog", priority: 1)
            ],
            TimeSpan.FromSeconds(5),
            TimeSpan.FromMilliseconds(25),
            heartbeatInterval: TimeSpan.FromMilliseconds(220),
            heartbeatTimeout: TimeSpan.FromMilliseconds(800),
            maxConcurrentDials: 2);

        await using var manager = new P2PConnectionManager(options, registry);
        var handle = await manager.ConnectAsync();

        Assert.AreEqual("relay", handle.Endpoint.Label);
        Assert.AreEqual("relay-remote", handle.RemoteNodeId);

        await manager.DisconnectAsync();
    }

    [Test]
    public async Task HappyEyeballs_SwitchesToPrimaryAfterRecovery()
    {
        var adapterState = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);

        var adapter = new ProgrammableTransportAdapter(
            endpoint => { adapterState.AddOrUpdate(endpoint.Label ?? endpoint.Host, 1, (_, c) => c + 1); },
            async (endpoint, options, cancellationToken) =>
            {
                adapterState.TryGetValue("primary", out var primaryCount);
                if (endpoint.Label == "primary" && primaryCount <= 1)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(120), cancellationToken).ConfigureAwait(false);

                    throw new SocketException((int)SocketError.TimedOut);
                }

                await Task.Delay(TimeSpan.FromMilliseconds(40), cancellationToken).ConfigureAwait(false);

                return CreateStubTransportConnection(
                    endpoint,
                    cancellationToken,
                    endpoint.Label == "primary" ? "primary-remote" : "secondary-remote");
            });

        var registry = new P2PTransportAdapterRegistry(
            [("prog", (IP2PTransportAdapter)adapter)]);

        var options = new P2PConnectionOptions(
            "he03-client",
            [
                new("primary.example", 8110, label: "primary", transport: "prog"),
                new("secondary.example", 8120, label: "secondary", transport: "prog")
            ],
            TimeSpan.FromSeconds(4),
            TimeSpan.FromMilliseconds(30),
            heartbeatInterval: TimeSpan.FromMilliseconds(220),
            heartbeatTimeout: TimeSpan.FromMilliseconds(800),
            maxConcurrentDials: 2)
        {
            MaxDialsPerMinutePerEndpoint = 10,
            EndpointBlacklistDuration = TimeSpan.FromMilliseconds(200)
        };

        await using var manager = new P2PConnectionManager(options, registry);

        var first = await manager.ConnectAsync();
        Assert.AreEqual("secondary", first.Endpoint.Label);

        await manager.DisconnectAsync();
        await Task.Delay(TimeSpan.FromMilliseconds(250));

        var second = await manager.ConnectAsync();
        Assert.AreEqual("primary", second.Endpoint.Label);
        Assert.IsTrue(adapterState.TryGetValue("primary", out var primaryCount) && primaryCount >= 2);

        await manager.DisconnectAsync();
    }

    [Test]
    public async Task RateLimiter_BlacklistExpiresAndPrimaryDialResumes()
    {
        var attemptCounts = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);
        var primaryAllowSuccess = 0;

        var adapter = new ProgrammableTransportAdapter(
            endpoint =>
            {
                if (!string.IsNullOrEmpty(endpoint.Label))
                    attemptCounts.AddOrUpdate(endpoint.Label, 1, (_, count) => count + 1);
            },
            async (endpoint, options, cancellationToken) =>
            {
                if (endpoint.Label == "primary")
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(30), cancellationToken).ConfigureAwait(false);

                    if (Volatile.Read(ref primaryAllowSuccess) == 0)
                        throw new SocketException((int)SocketError.TimedOut);

                    return CreateStubTransportConnection(endpoint, cancellationToken, "primary-remote");
                }

                await Task.Delay(TimeSpan.FromMilliseconds(15), cancellationToken).ConfigureAwait(false);

                return CreateStubTransportConnection(endpoint, cancellationToken, "secondary-remote");
            });

        var registry = new P2PTransportAdapterRegistry(
            [("prog", (IP2PTransportAdapter)adapter)]);

        var blacklistDuration = TimeSpan.FromMilliseconds(350);
        var options = new P2PConnectionOptions(
            "rate-limit-client",
            [
                new("primary.example", 7100, label: "primary", transport: "prog"),
                new("secondary.example", 7200, label: "secondary", transport: "prog")
            ],
            TimeSpan.FromSeconds(3),
            TimeSpan.FromMilliseconds(25),
            heartbeatInterval: TimeSpan.FromMilliseconds(180),
            heartbeatTimeout: TimeSpan.FromMilliseconds(600),
            maxConcurrentDials: 1)
        {
            MaxDialsPerMinutePerEndpoint = 4,
            EndpointBlacklistDuration = blacklistDuration
        };

        await using var manager = new P2PConnectionManager(options, registry);

        var first = await manager.ConnectAsync();
        Assert.AreEqual("secondary", first.Endpoint.Label);
        Assert.IsTrue(attemptCounts.TryGetValue("primary", out var primaryAttempts) && primaryAttempts == 1);

        var metricsAfterFirst = manager.GetMetrics();
        Assert.IsTrue(metricsAfterFirst.BlacklistedEndpointCount >= 1);

        await manager.DisconnectAsync();
        Assert.IsTrue(
            await WaitForConditionAsync(() => manager.State == P2PConnectionState.Disconnected, TimeSpan.FromSeconds(2)),
            "Менеджер не перешёл в состояние Disconnected.");

        var second = await manager.ConnectAsync();
        Assert.AreEqual("secondary", second.Endpoint.Label);
        Assert.IsTrue(attemptCounts.TryGetValue("primary", out primaryAttempts) && primaryAttempts == 1);

        await manager.DisconnectAsync();
        Assert.IsTrue(
            await WaitForConditionAsync(() => manager.State == P2PConnectionState.Disconnected, TimeSpan.FromSeconds(2)),
            "Менеджер не перешёл в состояние Disconnected перед третьим подключением.");

        Volatile.Write(ref primaryAllowSuccess, 1);
        await Task.Delay(blacklistDuration + TimeSpan.FromMilliseconds(150));

        var third = await manager.ConnectAsync();
        Assert.AreEqual("primary", third.Endpoint.Label);
        Assert.IsTrue(attemptCounts.TryGetValue("primary", out primaryAttempts) && primaryAttempts == 2);

        var metricsAfterThird = manager.GetMetrics();
        Assert.AreEqual("primary", metricsAfterThird.LastEndpointLabel);

        await manager.DisconnectAsync();
    }

    [Test]
    public async Task RateLimiter_HonorsRetryAfterFromPeer()
    {
        var retryAfterIssued = false;
        var retryAfterApplied = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var adapter = new ProgrammableTransportAdapter(
            null,
            async (endpoint, options, cancellationToken) =>
            {
                if (!retryAfterIssued)
                {
                    retryAfterIssued = true;

                    throw new RetryAfterException(TimeSpan.FromMilliseconds(250));
                }

                retryAfterApplied.TrySetResult(true);
                await Task.Delay(TimeSpan.FromMilliseconds(30), cancellationToken).ConfigureAwait(false);

                return CreateStubTransportConnection(endpoint, cancellationToken, "retry-remote");
            });

        var registry = new P2PTransportAdapterRegistry(
            [("prog", (IP2PTransportAdapter)adapter)]);

        var options = new P2PConnectionOptions(
            "retry-after-client",
            [new("primary.example", 8300, label: "primary", transport: "prog")],
            TimeSpan.FromSeconds(4),
            TimeSpan.FromMilliseconds(25),
            heartbeatInterval: TimeSpan.FromMilliseconds(200),
            heartbeatTimeout: TimeSpan.FromMilliseconds(700),
            maxConcurrentDials: 1);

        await using var manager = new P2PConnectionManager(options, registry);
        var stopwatch = Stopwatch.StartNew();
        var handle = await manager.ConnectAsync();
        stopwatch.Stop();

        Assert.AreEqual("primary", handle.Endpoint.Label);
        Assert.IsTrue(
            stopwatch.Elapsed >= TimeSpan.FromMilliseconds(250),
            $"Переподключение произошло слишком быстро: {stopwatch.Elapsed}");
        Assert.IsTrue(retryAfterApplied.Task.IsCompleted);

        await manager.DisconnectAsync();
    }

    [Test]
    public async Task HappyEyeballs_TemporaryBlacklistRecoversEndpoint()
    {
        var state = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);
        var allowFlakySuccess = 0;

        var adapter = new ProgrammableTransportAdapter(
            endpoint => { state.AddOrUpdate(endpoint.Label ?? endpoint.Host, 1, (_, count) => count + 1); },
            async (endpoint, options, cancellationToken) =>
            {
                var attempts = state.GetValueOrDefault(endpoint.Label ?? endpoint.Host, 0);
                if (endpoint.Label == "flaky")
                {
                    if (Volatile.Read(ref allowFlakySuccess) == 0 && attempts <= 2)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(40), cancellationToken).ConfigureAwait(false);

                        throw new SocketException((int)SocketError.TimedOut);
                    }

                    await Task.Delay(TimeSpan.FromMilliseconds(30), cancellationToken).ConfigureAwait(false);

                    return CreateStubTransportConnection(endpoint, cancellationToken, "flaky-remote");
                }

                await Task.Delay(TimeSpan.FromMilliseconds(20), cancellationToken).ConfigureAwait(false);

                return CreateStubTransportConnection(endpoint, cancellationToken, "stable-remote");
            });

        var registry = new P2PTransportAdapterRegistry(
            [("prog", (IP2PTransportAdapter)adapter)]);

        var options = new P2PConnectionOptions(
            "he05-client",
            [
                new("flaky.example", 8400, label: "flaky", transport: "prog", priority: 0),
                new("stable.example", 8410, label: "stable", transport: "prog", priority: 0)
            ],
            TimeSpan.FromSeconds(4),
            TimeSpan.FromMilliseconds(25),
            heartbeatInterval: TimeSpan.FromMilliseconds(200),
            heartbeatTimeout: TimeSpan.FromMilliseconds(700),
            maxConcurrentDials: 2)
        {
            MaxDialsPerMinutePerEndpoint = 10,
            EndpointBlacklistDuration = TimeSpan.FromMilliseconds(200)
        };

        await using var manager = new P2PConnectionManager(options, registry);

        var first = await manager.ConnectAsync();
        Assert.AreEqual("stable", first.Endpoint.Label);
        await manager.DisconnectAsync();

        // Wait for blacklist TTL and allow flaky endpoint to succeed
        await Task.Delay(TimeSpan.FromMilliseconds(400));
        Volatile.Write(ref allowFlakySuccess, 1);

        P2PConnectionHandle? recovered = null;
        for (var attempt = 0; attempt < 3 && recovered is null; attempt++)
        {
            var candidate = await manager.ConnectAsync();
            if (candidate.Endpoint.Label == "flaky")
            {
                recovered = candidate;

                break;
            }

            await manager.DisconnectAsync();
            await Task.Delay(TimeSpan.FromMilliseconds(150));
        }

        Assert.IsNotNull(recovered);
        Assert.AreEqual("flaky", recovered!.Endpoint.Label);
        await manager.DisconnectAsync();
    }

    [Test]
    public async Task Reconnect_MakeBeforeBreakPreservesSessions()
    {
        var attempt = 0;
        var streams = new ConcurrentDictionary<int, TrackingStream>();

        var adapter = new ProgrammableTransportAdapter(
            _ => { },
            (endpoint, options, cancellationToken) =>
            {
                var id = Interlocked.Increment(ref attempt);
                var stream = new TrackingStream($"attempt-{id}");
                streams[id] = stream;

                var controlChannel = new ProgrammableControlChannel(
                    $"remote-{id}",
                    envelope => new()
                    {
                        Type = P2PHandshakeMessageType.ServerHello,
                        NodeId = $"remote-{id}",
                        SessionId = $"session-{id}",
                        SessionEpoch = id,
                        ResumeToken = $"resume-token-{id}",
                        TimestampTicks = DateTimeOffset.UtcNow.UtcTicks
                    });

                async ValueTask DisposeAsync()
                {
                    controlChannel.Complete();
                    stream.Dispose();
                    await Task.CompletedTask;
                }

                var connection = new P2PTransportConnection(
                    stream,
                    controlChannel,
                    new DnsEndPoint(endpoint.Host, endpoint.Port),
                    null,
                    DisposeAsync)
                {
                    TransportCancellationToken = cancellationToken
                };

                return Task.FromResult(connection);
            });

        var options = new P2PConnectionOptions(
            "mb-client",
            [new("shadow.example", 8500, label: "primary", transport: "prog")],
            TimeSpan.FromSeconds(5),
            TimeSpan.FromMilliseconds(50),
            heartbeatInterval: TimeSpan.FromSeconds(5),
            heartbeatTimeout: TimeSpan.FromSeconds(30));

        await using var manager = new P2PConnectionManager(options, adapter);

        var firstHandle = await manager.ConnectAsync();
        Assert.AreEqual("remote-1", firstHandle.RemoteNodeId);
        var firstStream = streams[1];
        Assert.IsFalse(firstStream.IsDisposed);

        var secondHandle = await manager.ReconnectAsync();
        Assert.AreEqual("remote-2", secondHandle.RemoteNodeId);
        Assert.AreNotEqual(firstHandle.RemoteSessionId, secondHandle.RemoteSessionId);

        await firstStream.WaitDisposedAsync(TimeSpan.FromSeconds(1));
        Assert.IsTrue(firstStream.IsDisposed);

        await manager.DisconnectAsync();
    }

    [Test]
    public async Task Reconnect_InvalidResumeFallsBackToFullHandshake()
    {
        var handshakeId = 0;
        var adapter = new ProgrammableTransportAdapter(
            _ => { },
            (endpoint, options, cancellationToken) =>
            {
                var id = Interlocked.Increment(ref handshakeId);
                var controlChannel = new ProgrammableControlChannel(
                    $"resume-remote-{id}",
                    envelope =>
                    {
                        if (id == 1)
                            return new()
                            {
                                Type = P2PHandshakeMessageType.ServerHello,
                                NodeId = "resume-remote-1",
                                SessionId = "resume-session-1",
                                SessionEpoch = 1,
                                ResumeToken = "resume-token-1",
                                TimestampTicks = DateTimeOffset.UtcNow.UtcTicks
                            };

                        Assert.AreEqual(P2PHandshakeMessageType.ResumeRequest, envelope.Type);
                        Assert.AreEqual("resume-token-1", envelope.ResumeToken);

                        return new()
                        {
                            Type = P2PHandshakeMessageType.ServerHello,
                            NodeId = "resume-remote-2",
                            SessionId = "resume-session-2",
                            SessionEpoch = 2,
                            TimestampTicks = DateTimeOffset.UtcNow.UtcTicks
                        };
                    });

                async ValueTask DisposeAsync()
                {
                    controlChannel.Complete();
                    await Task.CompletedTask;
                }

                var connection = new P2PTransportConnection(
                    new MemoryStream(),
                    controlChannel,
                    new DnsEndPoint(endpoint.Host, endpoint.Port),
                    null,
                    DisposeAsync)
                {
                    TransportCancellationToken = cancellationToken
                };

                return Task.FromResult(connection);
            });

        var options = new P2PConnectionOptions(
            "resume-client",
            [new("resume.example", 8600, label: "primary", transport: "prog")],
            TimeSpan.FromSeconds(4),
            TimeSpan.FromMilliseconds(30),
            heartbeatInterval: TimeSpan.FromSeconds(5),
            heartbeatTimeout: TimeSpan.FromSeconds(30))
        {
            EnableSessionResume = true
        };

        await using var manager = new P2PConnectionManager(options, adapter);

        await manager.ConnectAsync();
        var metricsBefore = manager.GetMetrics();

        var handleAfterReconnect = await manager.ReconnectAsync();
        var metricsAfter = manager.GetMetrics();

        Assert.AreEqual("resume-remote-2", handleAfterReconnect.RemoteNodeId);
        Assert.IsTrue(metricsAfter.ResumeSuccessRatio <= metricsBefore.ResumeSuccessRatio + 1e-9);
        Assert.AreEqual(2, handshakeId);

        await manager.DisconnectAsync();
    }

    [Test]
    public async Task Reconnect_ParallelCallsShareSingleDial()
    {
        var attempt = 0;
        var adapter = new ProgrammableTransportAdapter(
            _ => { },
            (endpoint, options, cancellationToken) =>
            {
                var id = Interlocked.Increment(ref attempt);
                var controlChannel = new ProgrammableControlChannel(
                    $"parallel-remote-{id}",
                    envelope =>
                    {
                        if (id == 1)
                            return new()
                            {
                                Type = P2PHandshakeMessageType.ServerHello,
                                NodeId = "parallel-remote-1",
                                SessionId = "parallel-session-1",
                                SessionEpoch = 1,
                                ResumeToken = "parallel-resume-1",
                                TimestampTicks = DateTimeOffset.UtcNow.UtcTicks
                            };

                        Assert.AreEqual(P2PHandshakeMessageType.ResumeRequest, envelope.Type);

                        return new()
                        {
                            Type = P2PHandshakeMessageType.ResumeAcknowledge,
                            NodeId = "parallel-remote-2",
                            SessionId = "parallel-session-2",
                            SessionEpoch = 2,
                            ResumeToken = "parallel-resume-2",
                            TimestampTicks = DateTimeOffset.UtcNow.UtcTicks
                        };
                    });

                async ValueTask DisposeAsync()
                {
                    controlChannel.Complete();
                    await Task.CompletedTask;
                }

                var connection = new P2PTransportConnection(
                    new MemoryStream(),
                    controlChannel,
                    new DnsEndPoint(endpoint.Host, endpoint.Port),
                    null,
                    DisposeAsync)
                {
                    TransportCancellationToken = cancellationToken
                };

                return Task.FromResult(connection);
            });

        var options = new P2PConnectionOptions(
            "parallel-client",
            [new("parallel.example", 8700, label: "primary", transport: "prog")],
            TimeSpan.FromSeconds(4),
            TimeSpan.FromMilliseconds(30),
            heartbeatInterval: TimeSpan.FromSeconds(5),
            heartbeatTimeout: TimeSpan.FromSeconds(30))
        {
            EnableSessionResume = true
        };

        await using var manager = new P2PConnectionManager(options, adapter);

        await manager.ConnectAsync();

        var reconnectTasks = new[] { manager.ReconnectAsync(), manager.ReconnectAsync() };
        var handles = await Task.WhenAll(reconnectTasks);

        var distinctSessions = handles.Select(h => h.RemoteSessionId).Distinct(StringComparer.Ordinal).Count();
        Assert.That(distinctSessions, Is.InRange(1, 2));
        Assert.That(attempt, Is.InRange(2, 3)); // initial dial + up to two sequential reconnects

        await manager.DisconnectAsync();
    }

    [Test]
    public async Task Reconnect_FailsPrimarySwitchesToSecondaryEndpoint()
    {
        var attempt = 0;
        var wifiAvailable = true;

        var adapter = new ProgrammableTransportAdapter(
            endpoint => { },
            async (endpoint, options, cancellationToken) =>
            {
                if (endpoint.Label == "wifi" && !wifiAvailable)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(20), cancellationToken).ConfigureAwait(false);

                    throw new SocketException((int)SocketError.TimedOut);
                }

                var id = Interlocked.Increment(ref attempt);
                var controlChannel = new ProgrammableControlChannel(
                    $"failover-remote-{id}",
                    envelope => new()
                    {
                        Type = P2PHandshakeMessageType.ServerHello,
                        NodeId = envelope.Type == P2PHandshakeMessageType.ResumeRequest ? "failover-remote-2" : "failover-remote-1",
                        SessionId = envelope.Type == P2PHandshakeMessageType.ResumeRequest ? "failover-session-2" : "failover-session-1",
                        SessionEpoch = id,
                        ResumeToken = envelope.Type == P2PHandshakeMessageType.ResumeRequest ? "failover-resume-2" : "failover-resume-1",
                        TimestampTicks = DateTimeOffset.UtcNow.UtcTicks
                    });

                async ValueTask DisposeAsync()
                {
                    controlChannel.Complete();
                    await Task.CompletedTask;
                }

                var connection = new P2PTransportConnection(
                    new MemoryStream(),
                    controlChannel,
                    new DnsEndPoint(endpoint.Host, endpoint.Port),
                    null,
                    DisposeAsync)
                {
                    TransportCancellationToken = cancellationToken
                };

                return connection;
            });

        var options = new P2PConnectionOptions(
            "failover-client",
            [
                new("wifi.example", 8800, label: "wifi", transport: "prog", priority: 0),
                new("ethernet.example", 8810, label: "ethernet", transport: "prog", priority: 0)
            ],
            TimeSpan.FromSeconds(4),
            TimeSpan.FromMilliseconds(30),
            heartbeatInterval: TimeSpan.FromSeconds(5),
            heartbeatTimeout: TimeSpan.FromSeconds(30))
        {
            EnableSessionResume = true
        };

        await using var manager = new P2PConnectionManager(options, adapter);

        var first = await manager.ConnectAsync();
        Assert.AreEqual("wifi", first.Endpoint.Label);

        wifiAvailable = false;
        var second = await manager.ReconnectAsync();
        Assert.AreEqual("ethernet", second.Endpoint.Label);

        await manager.DisconnectAsync();
    }

    [Test]
    public async Task Reconnect_SessionEpochMonotonicAcrossShadowDials()
    {
        var epoch = 0;
        var adapter = new ProgrammableTransportAdapter(
            _ => { },
            (endpoint, options, cancellationToken) =>
            {
                var currentEpoch = Interlocked.Increment(ref epoch);
                var controlChannel = new ProgrammableControlChannel(
                    $"epoch-remote-{currentEpoch}",
                    envelope => new()
                    {
                        Type = envelope.Type == P2PHandshakeMessageType.ResumeRequest
                            ? P2PHandshakeMessageType.ResumeAcknowledge
                            : P2PHandshakeMessageType.ServerHello,
                        NodeId = $"epoch-remote-{currentEpoch}",
                        SessionId = $"epoch-session-{currentEpoch}",
                        SessionEpoch = currentEpoch,
                        ResumeToken = $"epoch-resume-{currentEpoch}",
                        TimestampTicks = DateTimeOffset.UtcNow.UtcTicks
                    });

                async ValueTask DisposeAsync()
                {
                    controlChannel.Complete();
                    await Task.CompletedTask;
                }

                var connection = new P2PTransportConnection(
                    new MemoryStream(),
                    controlChannel,
                    new DnsEndPoint(endpoint.Host, endpoint.Port),
                    null,
                    DisposeAsync)
                {
                    TransportCancellationToken = cancellationToken
                };

                return Task.FromResult(connection);
            });

        var options = new P2PConnectionOptions(
            "epoch-client",
            [new("epoch.example", 8900, label: "primary", transport: "prog")],
            TimeSpan.FromSeconds(4),
            TimeSpan.FromMilliseconds(30),
            heartbeatInterval: TimeSpan.FromSeconds(5),
            heartbeatTimeout: TimeSpan.FromSeconds(30))
        {
            EnableSessionResume = true
        };

        await using var manager = new P2PConnectionManager(options, adapter);

        var epochs = new List<long>();
        var first = await manager.ConnectAsync();
        epochs.Add(first.SessionEpoch);

        for (var i = 0; i < 3; i++)
        {
            var handle = await manager.ReconnectAsync();
            epochs.Add(handle.SessionEpoch);
        }

        for (var i = 1; i < epochs.Count; i++)
            Assert.IsTrue(epochs[i] > epochs[i - 1], $"Session epoch did not increase: {string.Join(",", epochs)}");

        await manager.DisconnectAsync();
    }

    [Test]
    public async Task DiagnosticsBroadcast_SendsBinaryBlacklistFrame()
    {
        SharedBlacklistRegistry.Clear();
        var dial = 0;
        ProgrammableControlChannel? initialChannel = null;
        ProgrammableControlChannel? latestChannel = null;
        var retryDelay = TimeSpan.FromMilliseconds(280);

        var adapter = new ProgrammableTransportAdapter(
            _ => { },
            (endpoint, options, cancellationToken) =>
            {
                var current = Interlocked.Increment(ref dial);

                if (current == 2)
                    throw new RetryAfterException(retryDelay);

                var channel = new ProgrammableControlChannel(
                    $"diag-remote-{current}",
                    _ => new()
                    {
                        Type = P2PHandshakeMessageType.ServerHello,
                        NodeId = $"diag-remote-{current}",
                        SessionId = $"diag-session-{current}",
                        SessionEpoch = current,
                        ResumeToken = $"diag-resume-{current}",
                        TimestampTicks = DateTimeOffset.UtcNow.UtcTicks
                    });
                latestChannel = channel;
                if (current == 1)
                    initialChannel = channel;

                async ValueTask DisposeAsync()
                {
                    channel.Complete();
                    await Task.CompletedTask;
                }

                var connection = new P2PTransportConnection(
                    new MemoryStream(),
                    channel,
                    new DnsEndPoint(endpoint.Host, endpoint.Port),
                    null,
                    DisposeAsync)
                {
                    TransportCancellationToken = cancellationToken
                };

                return Task.FromResult(connection);
            });

        var options = new P2PConnectionOptions(
            "diag-client",
            [new("diag.example", 9400, label: "primary", transport: "prog")],
            TimeSpan.FromSeconds(3),
            TimeSpan.FromMilliseconds(20),
            heartbeatInterval: TimeSpan.FromMilliseconds(200),
            heartbeatTimeout: TimeSpan.FromMilliseconds(800));

        await using var manager = new P2PConnectionManager(options, adapter);
        await manager.ConnectAsync();
        Assert.IsNotNull(initialChannel);

        await manager.ReconnectAsync();
        Assert.IsNotNull(latestChannel);

        var frames = initialChannel!.SentDiagnostics
            .Where(f => f.Payload.Length > 0 && f.Payload.Span[0] == 0xB1)
            .ToList();

        Assert.IsNotEmpty(frames);
        var payload = frames.Last().Payload.Span;
        Assert.AreEqual(0xB1, payload[0]);
        var flags = payload[1];
        Assert.AreEqual((int)FailureSeverity.Soft, flags & 0x03);
        Assert.AreNotEqual(0, flags & 0x04);
        var ttlMs = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(2, 4));
        Assert.That(ttlMs, Is.InRange(200, 2000));
        var keyLength = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(6, 2));
        var key = Encoding.UTF8.GetString(payload.Slice(8, keyLength));
        Assert.That(key, Does.Contain("diag.example"));

        await manager.DisconnectAsync();
    }

    [Test]
    public async Task DiagnosticsBroadcast_AppliesRemotePenalty()
    {
        SharedBlacklistRegistry.Clear();
        ProgrammableControlChannel? currentChannel = null;
        var adapter = new ProgrammableTransportAdapter(
            _ => { },
            (endpoint, options, cancellationToken) =>
            {
                var channel = new ProgrammableControlChannel(
                    "recv-remote",
                    _ => new()
                    {
                        Type = P2PHandshakeMessageType.ServerHello,
                        NodeId = "recv-remote",
                        SessionId = "recv-session",
                        SessionEpoch = 1,
                        ResumeToken = "recv-resume",
                        TimestampTicks = DateTimeOffset.UtcNow.UtcTicks
                    });
                currentChannel = channel;

                async ValueTask DisposeAsync()
                {
                    channel.Complete();
                    await Task.CompletedTask;
                }

                var connection = new P2PTransportConnection(
                    new MemoryStream(),
                    channel,
                    new DnsEndPoint(endpoint.Host, endpoint.Port),
                    null,
                    DisposeAsync)
                {
                    TransportCancellationToken = cancellationToken
                };

                return Task.FromResult(connection);
            });

        var endpoint = new P2PTransportEndpoint("recv.example", 9500, label: "primary", transport: "prog");
        var options = new P2PConnectionOptions(
            "recv-client",
            [endpoint],
            TimeSpan.FromSeconds(3),
            TimeSpan.FromMilliseconds(20),
            heartbeatInterval: TimeSpan.FromMilliseconds(200),
            heartbeatTimeout: TimeSpan.FromMilliseconds(800));

        await using var manager = new P2PConnectionManager(options, adapter);
        await manager.ConnectAsync();
        Assert.IsNotNull(currentChannel);

        var key = "prog:recv.example:9500";
        var ttl = TimeSpan.FromMilliseconds(600);
        var keyBytes = Encoding.UTF8.GetBytes(key);
        var payload = new byte[8 + keyBytes.Length];
        payload[0] = 0xB1;
        payload[1] = (byte)((int)FailureSeverity.Hard & 0x03);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(2, 4), (int)ttl.TotalMilliseconds);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(6, 2), (ushort)keyBytes.Length);
        keyBytes.CopyTo(payload.AsSpan(8));

        currentChannel!.InjectDiagnostics(payload);

        Assert.IsTrue(
            await WaitForConditionAsync(
                () =>
                {
                    var metrics = manager.GetMetrics();

                    return metrics.DialRates.Any(r => r.EndpointKey == key && r.BlacklistedUntilUtc > DateTimeOffset.UtcNow);
                },
                TimeSpan.FromSeconds(2)),
            "Менеджер не применил blacklist после получения диагностики.");

        var metricsAfter = manager.GetMetrics();
        Assert.AreEqual(FailureSeverity.Hard, metricsAfter.LastFailureSeverity);
        Assert.IsTrue(SharedBlacklistRegistry.TryGetPenalty(key, DateTimeOffset.UtcNow, out var entry));
        Assert.AreEqual(FailureSeverity.Hard, entry.Severity);

        await manager.DisconnectAsync();
    }

    [Test]
    public async Task SharedBlacklist_BusPropagatesAcrossManagers()
    {
        SharedBlacklistRegistry.Clear();
        var retryDelay = TimeSpan.FromMilliseconds(350);
        var dial = 0;

        var adapter = new ProgrammableTransportAdapter(
            _ => { },
            (endpoint, options, cancellationToken) =>
            {
                var current = Interlocked.Increment(ref dial);

                if (current == 1)
                    throw new RetryAfterException(retryDelay);

                return Task.FromResult(CreateStubTransportConnection(endpoint, cancellationToken, $"shared-remote-{current}"));
            });

        var endpoints = new[]
        {
            new P2PTransportEndpoint("shared.example", 9600, label: "primary", transport: "prog")
        };

        var optionsA = new P2PConnectionOptions(
            "shared-client-A",
            endpoints,
            TimeSpan.FromSeconds(3),
            TimeSpan.FromMilliseconds(25),
            heartbeatInterval: TimeSpan.FromMilliseconds(200),
            heartbeatTimeout: TimeSpan.FromMilliseconds(800))
        {
            EnableNetworkMonitoring = false
        };

        var optionsB = new P2PConnectionOptions(
            "shared-client-B",
            endpoints,
            TimeSpan.FromSeconds(3),
            TimeSpan.FromMilliseconds(25),
            heartbeatInterval: TimeSpan.FromMilliseconds(200),
            heartbeatTimeout: TimeSpan.FromMilliseconds(800))
        {
            EnableNetworkMonitoring = false
        };

        await using var managerA = new P2PConnectionManager(optionsA, adapter);
        await using var managerB = new P2PConnectionManager(optionsB, adapter);

        var key = "prog:shared.example:9600";
        var connectTask = managerA.ConnectAsync();
        var observedExpiry = DateTimeOffset.MinValue;

        var propagated = await WaitForConditionAsync(
            () =>
            {
                var metrics = managerB.GetMetrics();
                foreach (var entry in metrics.DialRates)
                    if (entry.EndpointKey == key && entry.BlacklistedUntilUtc > DateTimeOffset.UtcNow)
                    {
                        observedExpiry = entry.BlacklistedUntilUtc;

                        return true;
                    }

                return false;
            },
            TimeSpan.FromSeconds(2));

        await connectTask;

        Assert.IsTrue(propagated, "Второй менеджер не получил обновление blacklist через локальную шину.");
        Assert.AreNotEqual(DateTimeOffset.MinValue, observedExpiry);

        var metricsB = managerB.GetMetrics();
        var trackerEntry = metricsB.DialRates.First(r => r.EndpointKey == key);
        Assert.IsTrue(trackerEntry.BlacklistedUntilUtc >= observedExpiry.AddMilliseconds(-50), "Срок действия blacklist не сохранён.");

        await managerA.DisconnectAsync();
        await managerB.DisconnectAsync();
    }

    [Test]
    public async Task SharedBlacklist_ExpiredPenaltyAllowsReconnectAfterOriginCrash()
    {
        SharedBlacklistRegistry.Clear();
        var endpoint = new P2PTransportEndpoint("recover.example", 9700, label: "primary", transport: "prog");
        var key = "prog:recover.example:9700";
        SharedBlacklistRegistry.SetPenalty(key, FailureSeverity.Hard, TimeSpan.FromMilliseconds(120), Guid.NewGuid());

        var attempts = 0;
        var adapter = new ProgrammableTransportAdapter(
            _ => Interlocked.Increment(ref attempts),
            (ep, options, cancellationToken) =>
                Task.FromResult(CreateStubTransportConnection(ep, cancellationToken, "recover-remote")));

        var options = new P2PConnectionOptions(
            "recover-client",
            [endpoint],
            TimeSpan.FromSeconds(3),
            TimeSpan.FromMilliseconds(10),
            heartbeatInterval: TimeSpan.FromMilliseconds(200),
            heartbeatTimeout: TimeSpan.FromMilliseconds(600))
        {
            EndpointBlacklistDuration = TimeSpan.FromMilliseconds(150),
            MaxDialsPerMinutePerEndpoint = 10,
            EnableNetworkMonitoring = false
        };

        await using var manager = new P2PConnectionManager(options, adapter);
        var handle = await manager.ConnectAsync();

        Assert.AreEqual("recover-remote", handle.RemoteNodeId);
        Assert.IsTrue(attempts >= 1, "Dial attempts were not resumed after TTL.");
        Assert.IsFalse(SharedBlacklistRegistry.TryGetPenalty(key, DateTimeOffset.UtcNow, out _));

        await manager.DisconnectAsync();
    }

    [Test]
    public async Task SharedBlacklist_PruneRemovesExpiredEntries()
    {
        SharedBlacklistRegistry.Clear();
        var key = "test:endpoint:1000";
        SharedBlacklistRegistry.SetPenalty(key, FailureSeverity.Soft, TimeSpan.FromMilliseconds(40), Guid.NewGuid());

        await Task.Delay(TimeSpan.FromMilliseconds(80));
        SharedBlacklistRegistry.PruneExpiredEntries(DateTimeOffset.UtcNow, true);

        Assert.IsFalse(SharedBlacklistRegistry.TryGetPenalty(key, DateTimeOffset.UtcNow, out _));
    }

    [Test]
    public async Task SharedBlacklist_DisposeSubscriptionStopsNotifications()
    {
        SharedBlacklistRegistry.Clear();
        var notifications = 0;
        var subscription = SharedBlacklistRegistry.Subscribe(_ => Interlocked.Increment(ref notifications));
        subscription.Dispose();
        SharedBlacklistRegistry.SetPenalty("ghost:endpoint", FailureSeverity.Soft, TimeSpan.FromMilliseconds(20), Guid.NewGuid());
        await Task.Delay(30);
        Assert.AreEqual(0, notifications);
    }

    [Test]
    public async Task ErrorPolicy_SoftFailureUpdatesMetrics()
    {
        SharedBlacklistRegistry.Clear();
        var adapter = new ProgrammableTransportAdapter(
            _ => { },
            (endpoint, options, cancellationToken) =>
            {
                var controlChannel = new ProgrammableControlChannel(
                    "soft-remote",
                    _ => new()
                    {
                        Type = P2PHandshakeMessageType.ServerHello,
                        NodeId = "soft-remote",
                        SessionId = "soft-session",
                        SessionEpoch = 1,
                        ResumeToken = "soft-resume",
                        TimestampTicks = DateTimeOffset.UtcNow.UtcTicks
                    });

                async ValueTask DisposeAsync()
                {
                    controlChannel.Complete();
                    await Task.CompletedTask;
                }

                var connection = new P2PTransportConnection(
                    new MemoryStream(),
                    controlChannel,
                    new DnsEndPoint(endpoint.Host, endpoint.Port),
                    null,
                    DisposeAsync)
                {
                    TransportCancellationToken = cancellationToken
                };

                return Task.FromResult(connection);
            });

        var options = new P2PConnectionOptions(
            "soft-client",
            [new("soft.example", 9000, label: "primary", transport: "prog")],
            TimeSpan.FromSeconds(4),
            TimeSpan.FromMilliseconds(25),
            heartbeatInterval: TimeSpan.FromMilliseconds(200),
            heartbeatTimeout: TimeSpan.FromMilliseconds(400));

        await using var manager = new P2PConnectionManager(options, adapter);
        await manager.ConnectAsync();

        var ackField = typeof(P2PConnectionManager).GetField("_lastHeartbeatAck", BindingFlags.NonPublic | BindingFlags.Instance);
        ackField!.SetValue(manager, DateTimeOffset.UtcNow - options.HeartbeatTimeout - TimeSpan.FromMilliseconds(50));

        var method = typeof(P2PConnectionManager).GetMethod("CheckHeartbeatTimeout", BindingFlags.NonPublic | BindingFlags.Instance);
        method!.Invoke(manager, [DateTimeOffset.UtcNow]);

        Assert.IsTrue(
            await WaitForConditionAsync(() => manager.State == P2PConnectionState.Established, TimeSpan.FromSeconds(5)),
            "Менеджер не восстановился после мягкого сбоя.");

        var metrics = manager.GetMetrics();
        Assert.AreEqual(FailureSeverity.Soft, metrics.LastFailureSeverity);

        await manager.DisconnectAsync();
    }

    [Test]
    public async Task ErrorPolicy_HardFailureUpdatesMetrics()
    {
        SharedBlacklistRegistry.Clear();
        var adapter = new ProgrammableTransportAdapter(
            _ => { },
            (endpoint, options, cancellationToken) =>
            {
                var controlChannel = new ProgrammableControlChannel(
                    "hard-remote",
                    _ => new()
                    {
                        Type = P2PHandshakeMessageType.ServerHello,
                        NodeId = "hard-remote",
                        SessionId = "hard-session",
                        SessionEpoch = 1,
                        ResumeToken = "hard-resume",
                        TimestampTicks = DateTimeOffset.UtcNow.UtcTicks
                    });

                async ValueTask DisposeAsync()
                {
                    controlChannel.Complete();
                    await Task.CompletedTask;
                }

                var connection = new P2PTransportConnection(
                    new MemoryStream(),
                    controlChannel,
                    new DnsEndPoint(endpoint.Host, endpoint.Port),
                    null,
                    DisposeAsync)
                {
                    TransportCancellationToken = cancellationToken
                };

                return Task.FromResult(connection);
            });

        var options = new P2PConnectionOptions(
            "hard-client",
            [new("hard.example", 9100, label: "primary", transport: "prog")],
            TimeSpan.FromSeconds(4),
            TimeSpan.FromMilliseconds(25),
            heartbeatInterval: TimeSpan.FromMilliseconds(200),
            heartbeatTimeout: TimeSpan.FromMilliseconds(400));

        await using var manager = new P2PConnectionManager(options, adapter);
        await manager.ConnectAsync();

        var method = typeof(P2PConnectionManager).GetMethod("SignalDataStreamFailureAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        await InvokePrivateAsync(manager, method!, new IOException("hard-failure"));

        Assert.IsTrue(
            await WaitForConditionAsync(() => manager.State == P2PConnectionState.Established, TimeSpan.FromSeconds(5)),
            "Менеджер не восстановился после жёсткого сбоя.");

        var metrics = manager.GetMetrics();
        Assert.AreEqual(FailureSeverity.Hard, metrics.LastFailureSeverity);

        await manager.DisconnectAsync();
    }

    [Test]
    public async Task ErrorPolicy_FatalHandshakeSetsFailedState()
    {
        SharedBlacklistRegistry.Clear();
        var adapter = new FailingHandshakeTransportAdapter();
        var options = new P2PConnectionOptions(
            "fatal-client",
            [new("fatal.example", 9200, label: "primary", transport: P2PTransportNames.Tcp)],
            TimeSpan.FromSeconds(2),
            TimeSpan.FromMilliseconds(25),
            heartbeatInterval: TimeSpan.FromSeconds(5),
            heartbeatTimeout: TimeSpan.FromSeconds(30));

        await using var manager = new P2PConnectionManager(options, adapter);

        Assert.ThrowsAsync<P2PHandshakeException>(() => manager.ConnectAsync());

        var metrics = manager.GetMetrics();
        Assert.AreEqual(FailureSeverity.Fatal, metrics.LastFailureSeverity);
        Assert.AreEqual(P2PConnectionState.Failed, manager.State);
    }

    [Test]
    public async Task ConnectAsync_TcpAdapterAndListener_PerformsHandshake()
    {
        var hostOptions = new P2PConnectionHostOptions
        {
            NodeId = "server-node"
        };

        await using var host = new P2PConnectionHost(hostOptions);
        await host.StartAsync();

        var localEndPoint = (IPEndPoint?)host.LocalEndPoint ?? throw new InvalidOperationException("Listener not started.");

        var acceptedTcs = new TaskCompletionSource<P2PConnectionAcceptedContext>(TaskCreationOptions.RunContinuationsAsynchronously);
        host.ConnectionAccepted += context =>
        {
            acceptedTcs.TrySetResult(context);

            return Task.CompletedTask;
        };

        var registry = new P2PTransportAdapterRegistry(
            [(P2PTransportNames.Tcp, (IP2PTransportAdapter)new TcpP2PTransportAdapter())]);

        var options = new P2PConnectionOptions(
            "client-node",
            [
                new(
                    "127.0.0.1",
                    localEndPoint.Port,
                    label: "tcp",
                    transport: P2PTransportNames.Tcp)
            ],
            heartbeatInterval: TimeSpan.FromMilliseconds(150),
            heartbeatTimeout: TimeSpan.FromMilliseconds(500));

        await using var manager = new P2PConnectionManager(options, registry);
        await manager.ConnectAsync();

        var accepted = await acceptedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual("client-node", accepted.RemoteNodeId);

        await manager.DisconnectAsync();
        await host.StopAsync();
    }

    [Test]
    public async Task TcpAdapter_RemoteResetTriggersImmediateReconnect()
    {
        var hostOptions = new P2PConnectionHostOptions
        {
            NodeId = "rst-host",
            TransportListenerOptions = new()
            {
                Address = IPAddress.Loopback,
                Port = 0
            }
        };

        await using var host = new P2PConnectionHost(hostOptions);
        var initialAcceptTcs = new TaskCompletionSource<P2PConnectionAcceptedContext>(TaskCreationOptions.RunContinuationsAsynchronously);
        host.ConnectionAccepted += context =>
        {
            initialAcceptTcs.TrySetResult(context);

            return Task.CompletedTask;
        };

        await host.StartAsync();
        var endpoint = (IPEndPoint?)host.LocalEndPoint ?? throw new InvalidOperationException();

        var registry = new P2PTransportAdapterRegistry(
            [(P2PTransportNames.Tcp, (IP2PTransportAdapter)new TcpP2PTransportAdapter())]);

        var options = new P2PConnectionOptions(
            "rst-client",
            [new("127.0.0.1", endpoint.Port, transport: P2PTransportNames.Tcp)],
            heartbeatInterval: TimeSpan.FromMilliseconds(150),
            heartbeatTimeout: TimeSpan.FromMilliseconds(450));

        await using var manager = new P2PConnectionManager(options, registry);
        await manager.ConnectAsync();

        var serverContext = await initialAcceptTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var initialRemoteSessionId = manager.CurrentConnection?.RemoteSessionId ?? throw new InvalidOperationException();

        ForceRst(serverContext.Connection);

        if (manager.CurrentConnection is not null)
            Assert.That(
                async () => await manager.CurrentConnection!.DataStream.WriteAsync([1], 0, 1),
                Throws.InstanceOf<Exception>());

        var reconnectionTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        host.ConnectionAccepted += context =>
        {
            if (!string.Equals(context.LocalSessionId, initialRemoteSessionId, StringComparison.Ordinal))
                reconnectionTcs.TrySetResult(true);

            return Task.CompletedTask;
        };

        Assert.IsTrue(
            await reconnectionTcs.Task.WaitAsync(TimeSpan.FromSeconds(20)),
            "Повторное подключение после RST не произошло.");

        Assert.IsTrue(
            await WaitForConditionAsync(
                () =>
                {
                    var current = manager.CurrentConnection;

                    return current is not null && !string.Equals(current.RemoteSessionId, initialRemoteSessionId, StringComparison.Ordinal);
                },
                TimeSpan.FromSeconds(10)),
            "Менеджер не сменил sessionId после RST.");

        await manager.DisconnectAsync();
        await host.StopAsync();
    }

    [Test]
    public async Task NetworkEventSource_TriggersWriteProbeAndReconnect()
    {
        var eventSource = new FakeNetworkEventSource();
        var hostOptions = new P2PConnectionHostOptions
        {
            NodeId = "net-event-host",
            TransportListenerOptions = new()
            {
                Address = IPAddress.Loopback,
                Port = 0
            }
        };

        await using var host = new P2PConnectionHost(hostOptions);
        var acceptedCount = 0;
        P2PConnectionAcceptedContext? latestContext = null;
        host.ConnectionAccepted += context =>
        {
            Interlocked.Increment(ref acceptedCount);
            latestContext = context;

            return Task.CompletedTask;
        };
        await host.StartAsync();
        var endpoint = (IPEndPoint?)host.LocalEndPoint ?? throw new InvalidOperationException();

        var registry = new P2PTransportAdapterRegistry(
            [(P2PTransportNames.Tcp, (IP2PTransportAdapter)new TcpP2PTransportAdapter())]);

        var options = new P2PConnectionOptions(
            "net-event-client",
            [new("127.0.0.1", endpoint.Port, transport: P2PTransportNames.Tcp)],
            heartbeatInterval: TimeSpan.FromMilliseconds(150),
            heartbeatTimeout: TimeSpan.FromMilliseconds(450))
        {
            NetworkEventSource = eventSource
        };

        await using var manager = new P2PConnectionManager(options, registry);
        await manager.ConnectAsync();

        Assert.IsTrue(
            await WaitForConditionAsync(() => Volatile.Read(ref acceptedCount) >= 1, TimeSpan.FromSeconds(2)),
            "Первое подключение не состоялось.");

        eventSource.RaiseAvailabilityLost();
        if (latestContext is not null)
            ForceRst(latestContext.Connection);

        Assert.IsTrue(
            await WaitForConditionAsync(() => Volatile.Read(ref acceptedCount) >= 2, TimeSpan.FromSeconds(20)),
            "Повторное подключение после сигнала network-change не состоялось.");

        await manager.DisconnectAsync();
        await host.StopAsync();
    }

    [TestCase(4, 150, 30)]
    [TestCase(8, 300, 45)]
    public async Task TcpAdapter_HandlesParallelClientConnectionsAndMetrics(int clientCount, int heartbeatIntervalMs, int probes)
    {
        var hostOptions = new P2PConnectionHostOptions
        {
            NodeId = "server-parallel",
            TransportListenerOptions = new()
            {
                Address = IPAddress.Loopback,
                Port = 0,
                SessionPairTimeout = TimeSpan.FromSeconds(5)
            }
        };

        await using var host = new P2PConnectionHost(hostOptions);
        await host.StartAsync();

        var localEndpoint = (IPEndPoint?)host.LocalEndPoint ?? throw new InvalidOperationException("Listener not started.");
        var registry = new P2PTransportAdapterRegistry(
            [(P2PTransportNames.Tcp, (IP2PTransportAdapter)new TcpP2PTransportAdapter())]);

        var acceptanceSources = new ConcurrentDictionary<string, TaskCompletionSource<P2PConnectionAcceptedContext>>();
        host.ConnectionAccepted += context =>
        {
            if (acceptanceSources.TryRemove(context.RemoteNodeId, out var tcs))
                tcs.TrySetResult(context);

            return Task.CompletedTask;
        };

        var srttSamples = new ConcurrentBag<double>();
        var connectTasks = Enumerable.Range(0, clientCount).Select(async i =>
        {
            var nodeId = $"client-concurrent-{i}";
            var acceptTcs = new TaskCompletionSource<P2PConnectionAcceptedContext>(TaskCreationOptions.RunContinuationsAsynchronously);
            acceptanceSources[nodeId] = acceptTcs;

            var options = new P2PConnectionOptions(
                nodeId,
                [
                    new(
                        "127.0.0.1",
                        localEndpoint.Port,
                        label: $"tcp-{i}",
                        transport: P2PTransportNames.Tcp)
                ],
                heartbeatInterval: TimeSpan.FromMilliseconds(heartbeatIntervalMs),
                heartbeatTimeout: TimeSpan.FromMilliseconds(heartbeatIntervalMs * 3));

            await using var manager = new P2PConnectionManager(options, registry);

            await manager.ConnectAsync();
            var accepted = await acceptTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.AreEqual(nodeId, accepted.RemoteNodeId);

            var metricsTask = Task.Run(async () =>
            {
                for (var iteration = 0; iteration < probes; iteration++)
                {
                    var metrics = manager.GetMetrics();
                    srttSamples.Add(metrics.SrttMilliseconds);
                    Assert.IsTrue(metrics.LastHeartbeatAckUtc >= DateTimeOffset.MinValue);
                    await Task.Delay(10);
                }
            });

            await metricsTask;
            await manager.DisconnectAsync();
        }).ToArray();

        await Task.WhenAll(connectTasks);
        Assert.IsTrue(srttSamples.Count >= clientCount * probes);
        await host.StopAsync();
    }

    [TestCase(4, 100)]
    [TestCase(8, 200)]
    public async Task TcpAdapter_SurvivesClientBurstsAndDisconnects(int clientCount, int messagesPerClient)
    {
        var hostOptions = new P2PConnectionHostOptions
        {
            NodeId = "host-burst",
            TransportListenerOptions = new()
            {
                Address = IPAddress.Loopback,
                Port = 0,
                SessionPairTimeout = TimeSpan.FromSeconds(3)
            }
        };

        await using var host = new P2PConnectionHost(hostOptions);
        await host.StartAsync();

        var localEndPoint = (IPEndPoint?)host.LocalEndPoint ?? throw new InvalidOperationException();
        var registry = new P2PTransportAdapterRegistry(
            [(P2PTransportNames.Tcp, (IP2PTransportAdapter)new TcpP2PTransportAdapter())]);

        var acceptanceCounters = new ConcurrentDictionary<string, int>();
        host.ConnectionAccepted += context =>
        {
            acceptanceCounters.AddOrUpdate(context.RemoteNodeId, 1, (_, value) => value + 1);

            return Task.CompletedTask;
        };

        async Task ClientRoutine(int index)
        {
            var options = new P2PConnectionOptions(
                $"burst-{index}",
                [new("127.0.0.1", localEndPoint.Port, label: $"burst-{index}", transport: P2PTransportNames.Tcp)],
                heartbeatInterval: TimeSpan.FromMilliseconds(90),
                heartbeatTimeout: TimeSpan.FromMilliseconds(270));

            await using var manager = new P2PConnectionManager(options, registry);
            await manager.ConnectAsync();

            for (var iteration = 0; iteration < messagesPerClient; iteration++)
            {
                var metrics = manager.GetMetrics();
                Assert.IsTrue(metrics.LastHeartbeatAckUtc >= DateTimeOffset.MinValue);
                if (iteration % 15 == 0)
                    await Task.Delay(5 + iteration % 7);
            }

            await manager.DisconnectAsync();
        }

        await Task.WhenAll(Enumerable.Range(0, clientCount).Select(ClientRoutine));
        Assert.AreEqual(clientCount, acceptanceCounters.Count);

        await host.StopAsync();
    }

    [Test]
    public async Task Listener_CanRestartBetweenClientConnects()
    {
        var hostOptions = new P2PConnectionHostOptions
        {
            NodeId = "host-restart",
            TransportListenerOptions = new()
            {
                Address = IPAddress.Loopback,
                Port = 0
            }
        };

        await using var host = new P2PConnectionHost(hostOptions);
        await host.StartAsync();
        var firstEndPoint = (IPEndPoint?)host.LocalEndPoint ?? throw new InvalidOperationException();

        await host.StopAsync();

        await using var host2 = new P2PConnectionHost(hostOptions);
        await host2.StartAsync();
        var secondEndPoint = (IPEndPoint?)host2.LocalEndPoint ?? throw new InvalidOperationException();

        Assert.AreNotEqual(firstEndPoint.Port, secondEndPoint.Port);

        var registry = new P2PTransportAdapterRegistry(
            [(P2PTransportNames.Tcp, (IP2PTransportAdapter)new TcpP2PTransportAdapter())]);

        var options = new P2PConnectionOptions(
            "restart-client",
            [new("127.0.0.1", secondEndPoint.Port, transport: P2PTransportNames.Tcp)],
            heartbeatInterval: TimeSpan.FromMilliseconds(120),
            heartbeatTimeout: TimeSpan.FromMilliseconds(400));

        await using var manager = new P2PConnectionManager(options, registry);
        await manager.ConnectAsync();
        await manager.DisconnectAsync();

        await host2.StopAsync();
    }

    [Test]
    public async Task ConnectAsync_WithHandshakeError_Throws()
    {
        var registry = new P2PTransportAdapterRegistry(
            [("fail", (IP2PTransportAdapter)new FailingHandshakeTransportAdapter())]);

        var options = new P2PConnectionOptions(
            "client",
            [new("fail", 1234, label: "fail", transport: "fail")],
            heartbeatInterval: TimeSpan.FromMilliseconds(100),
            heartbeatTimeout: TimeSpan.FromMilliseconds(300));

        await using var manager = new P2PConnectionManager(options, registry);
        Assert.ThrowsAsync<P2PHandshakeException>(() => manager.ConnectAsync());
    }

    [Test]
    public async Task Listener_DropsIncompleteSessionAfterTimeout()
    {
        var hostOptions = new P2PConnectionHostOptions
        {
            NodeId = "host-timeout",
            TransportListenerOptions = new()
            {
                Address = IPAddress.Loopback,
                Port = 0,
                SessionPairTimeout = TimeSpan.FromMilliseconds(200)
            }
        };

        await using var host = new P2PConnectionHost(hostOptions);
        await host.StartAsync();
        var local = (IPEndPoint?)host.LocalEndPoint ?? throw new InvalidOperationException();

        var sessionId = Guid.NewGuid();
        using (var straySocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
        {
            await straySocket.ConnectAsync(new DnsEndPoint("127.0.0.1", local.Port));
            await SendChannelHeaderAsync(straySocket, sessionId, 0, CancellationToken.None);
            await Task.Delay(400);
        }

        var registry = new P2PTransportAdapterRegistry(
            [(P2PTransportNames.Tcp, (IP2PTransportAdapter)new TcpP2PTransportAdapter())]);

        var acceptanceTcs = new TaskCompletionSource<P2PConnectionAcceptedContext>(TaskCreationOptions.RunContinuationsAsynchronously);
        host.ConnectionAccepted += context =>
        {
            acceptanceTcs.TrySetResult(context);

            return Task.CompletedTask;
        };

        var options = new P2PConnectionOptions(
            "fresh-client",
            [new("127.0.0.1", local.Port, transport: P2PTransportNames.Tcp)],
            heartbeatInterval: TimeSpan.FromMilliseconds(120),
            heartbeatTimeout: TimeSpan.FromMilliseconds(400));

        await using var manager = new P2PConnectionManager(options, registry);
        await manager.ConnectAsync();
        await acceptanceTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await manager.DisconnectAsync();
        await host.StopAsync();
    }

    private static Task SendChannelHeaderAsync(Socket socket, Guid sessionId, byte channel, CancellationToken cancellationToken)
    {
        var buffer = new byte[22];
        BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(0, 4), 0x50325031);
        buffer[4] = 1;
        buffer[5] = channel;
        sessionId.TryWriteBytes(buffer.AsSpan(6, 16));

        return socket.SendAsync(buffer, SocketFlags.None, cancellationToken).AsTask();
    }


    [Test]
    public async Task ChaosTcpAdapter_WithDelays_PreservesConnectivity()
    {
        var hostOptions = new P2PConnectionHostOptions
        {
            NodeId = "chaos-host",
            TransportListenerOptions = new()
            {
                Address = IPAddress.Loopback,
                Port = 0,
                SessionPairTimeout = TimeSpan.FromSeconds(5)
            }
        };

        await using var host = new P2PConnectionHost(hostOptions);
        await host.StartAsync();
        var endPoint = (IPEndPoint?)host.LocalEndPoint ?? throw new InvalidOperationException();

        var dataChaos = new ChaosStreamOptions
        {
            MinReadDelay = TimeSpan.FromMilliseconds(2),
            MaxReadDelay = TimeSpan.FromMilliseconds(10),
            MinWriteDelay = TimeSpan.FromMilliseconds(2),
            MaxWriteDelay = TimeSpan.FromMilliseconds(5),
            ReadPerByteDelay = TimeSpan.FromMilliseconds(0.2),
            WritePerByteDelay = TimeSpan.FromMilliseconds(0.2),
            StallProbability = 0.2,
            MinStallDuration = TimeSpan.FromMilliseconds(1),
            MaxStallDuration = TimeSpan.FromMilliseconds(3)
        };

        var controlChaos = new ChaosStreamOptions
        {
            MinReadDelay = TimeSpan.FromMilliseconds(1),
            MaxReadDelay = TimeSpan.FromMilliseconds(5),
            MinWriteDelay = TimeSpan.FromMilliseconds(1),
            MaxWriteDelay = TimeSpan.FromMilliseconds(5)
        };

        var registry = new P2PTransportAdapterRegistry(
            [(P2PTransportNames.Tcp, (IP2PTransportAdapter)new ChaosTcpTransportAdapter(dataChaos, controlChaos))]);

        var options = new P2PConnectionOptions(
            "chaos-client",
            [new("127.0.0.1", endPoint.Port, transport: P2PTransportNames.Tcp)],
            heartbeatInterval: TimeSpan.FromMilliseconds(150),
            heartbeatTimeout: TimeSpan.FromMilliseconds(450));

        await using var manager = new P2PConnectionManager(options, registry);
        await manager.ConnectAsync();
        Assert.IsTrue(
            await WaitForFreshHeartbeatAsync(manager, options.HeartbeatTimeout, TimeSpan.FromSeconds(5)),
            "Heartbeat ack did not refresh under chaos conditions.");

        await manager.DisconnectAsync();
        await host.StopAsync();
    }

    [Test]
    public async Task ChaosTcpAdapter_DisconnectDuringHandshake_Throws()
    {
        var hostOptions = new P2PConnectionHostOptions
        {
            NodeId = "chaos-host-fail",
            TransportListenerOptions = new()
            {
                Address = IPAddress.Loopback,
                Port = 0,
                SessionPairTimeout = TimeSpan.FromSeconds(5)
            }
        };

        await using var host = new P2PConnectionHost(hostOptions);
        await host.StartAsync();
        var endPoint = (IPEndPoint?)host.LocalEndPoint ?? throw new InvalidOperationException();

        var disconnectTriggered = 0;
        var controlChaos = new ChaosStreamOptions
        {
            DisconnectProbability = 1,
            DisconnectMessage = "intentional",
            ShouldDisconnect = context =>
            {
                if (context.Operation == ChaosOperation.Read && Interlocked.Exchange(ref disconnectTriggered, 1) == 0)
                    return true;

                return false;
            }
        };

        var registry = new P2PTransportAdapterRegistry(
            [(P2PTransportNames.Tcp, (IP2PTransportAdapter)new ChaosTcpTransportAdapter(null, controlChaos))]);

        var options = new P2PConnectionOptions(
            "chaos-fail",
            [new("127.0.0.1", endPoint.Port, transport: P2PTransportNames.Tcp)],
            heartbeatInterval: TimeSpan.FromMilliseconds(100),
            heartbeatTimeout: TimeSpan.FromMilliseconds(300));

        await using var manager = new P2PConnectionManager(options, registry);
        Assert.ThrowsAsync<IOException>(() => manager.ConnectAsync());
        await host.StopAsync();
    }

    [Test]
    public async Task TcpAdapter_ServerReceivesClientPayload()
    {
        var hostOptions = new P2PConnectionHostOptions
        {
            NodeId = "payload-host",
            TransportListenerOptions = new()
            {
                Address = IPAddress.Loopback,
                Port = 0
            }
        };

        await using var host = new P2PConnectionHost(hostOptions);
        await host.StartAsync();

        var receivedTcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        host.ConnectionAccepted += async context =>
        {
            var buffer = new byte[8];
            var read = await context.Connection.DataStream.ReadAsync(buffer, 0, buffer.Length);
            receivedTcs.TrySetResult(buffer[..read].ToArray());
        };

        var endpoint = (IPEndPoint?)host.LocalEndPoint ?? throw new InvalidOperationException();
        var registry = new P2PTransportAdapterRegistry(
            [(P2PTransportNames.Tcp, (IP2PTransportAdapter)new TcpP2PTransportAdapter())]);

        var options = new P2PConnectionOptions(
            "payload-client",
            [new("127.0.0.1", endpoint.Port, transport: P2PTransportNames.Tcp)],
            heartbeatInterval: TimeSpan.FromMilliseconds(120),
            heartbeatTimeout: TimeSpan.FromMilliseconds(360));

        await using var manager = new P2PConnectionManager(options, registry);
        var handle = await manager.ConnectAsync();

        var payload = new byte[] { 1, 2, 3, 4, 5 };
        await handle.DataStream.WriteAsync(payload, 0, payload.Length);
        await handle.DataStream.FlushAsync();

        var received = await receivedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(payload, received);

        await manager.DisconnectAsync();
        await host.StopAsync();
    }

    [Test]
    public async Task TcpAdapter_SequentialConnectDisconnectFlood()
    {
        const int iterations = 40;

        var hostOptions = new P2PConnectionHostOptions
        {
            NodeId = "flood-host",
            TransportListenerOptions = new()
            {
                Address = IPAddress.Loopback,
                Port = 0
            }
        };

        await using var host = new P2PConnectionHost(hostOptions);
        await host.StartAsync();
        var endpoint = (IPEndPoint?)host.LocalEndPoint ?? throw new InvalidOperationException();

        var registry = new P2PTransportAdapterRegistry(
            [(P2PTransportNames.Tcp, (IP2PTransportAdapter)new TcpP2PTransportAdapter())]);

        for (var i = 0; i < iterations; i++)
        {
            var options = new P2PConnectionOptions(
                $"flood-client-{i}",
                [new("127.0.0.1", endpoint.Port, transport: P2PTransportNames.Tcp)],
                heartbeatInterval: TimeSpan.FromMilliseconds(100),
                heartbeatTimeout: TimeSpan.FromMilliseconds(300));

            await using var manager = new P2PConnectionManager(options, registry);
            await manager.ConnectAsync();
            var metrics = manager.GetMetrics();
            Assert.IsTrue(metrics.LastHeartbeatSentUtc <= DateTimeOffset.UtcNow);
            await manager.DisconnectAsync();
        }

        await host.StopAsync();
    }

    [Test]
    public async Task ChaosTcpAdapter_ConnectTimeoutOnControlStall()
    {
        var hostOptions = new P2PConnectionHostOptions
        {
            NodeId = "stall-host",
            TransportListenerOptions = new()
            {
                Address = IPAddress.Loopback,
                Port = 0
            }
        };

        await using var host = new P2PConnectionHost(hostOptions);
        await host.StartAsync();
        var endpoint = (IPEndPoint?)host.LocalEndPoint ?? throw new InvalidOperationException();

        var controlChaos = new ChaosStreamOptions
        {
            StallProbability = 1,
            MinStallDuration = TimeSpan.FromSeconds(5),
            MaxStallDuration = TimeSpan.FromSeconds(5)
        };

        var registry = new P2PTransportAdapterRegistry(
            [(P2PTransportNames.Tcp, (IP2PTransportAdapter)new ChaosTcpTransportAdapter(null, controlChaos))]);

        var options = new P2PConnectionOptions(
            "stall-client",
            [new("127.0.0.1", endpoint.Port, transport: P2PTransportNames.Tcp)],
            TimeSpan.FromMilliseconds(200),
            heartbeatInterval: TimeSpan.FromMilliseconds(100),
            heartbeatTimeout: TimeSpan.FromMilliseconds(300));

        await using var manager = new P2PConnectionManager(options, registry);
        Assert.That(async () => await manager.ConnectAsync(), Throws.InstanceOf<Exception>());
        await host.StopAsync();
    }

    [Test]
    public async Task TcpAdapter_ParallelConnectionsWithSharedRegistry()
    {
        var hostOptions = new P2PConnectionHostOptions
        {
            NodeId = "parallel-host",
            TransportListenerOptions = new()
            {
                Address = IPAddress.Loopback,
                Port = 0
            }
        };

        await using var host = new P2PConnectionHost(hostOptions);
        await host.StartAsync();
        var endpoint = (IPEndPoint?)host.LocalEndPoint ?? throw new InvalidOperationException();

        var registry = new P2PTransportAdapterRegistry(
            [(P2PTransportNames.Tcp, (IP2PTransportAdapter)new TcpP2PTransportAdapter())]);

        var tasks = Enumerable.Range(0, 12).Select(async i =>
        {
            var options = new P2PConnectionOptions(
                $"parallel-{i}",
                [new("127.0.0.1", endpoint.Port, transport: P2PTransportNames.Tcp)],
                heartbeatInterval: TimeSpan.FromMilliseconds(90),
                heartbeatTimeout: TimeSpan.FromMilliseconds(270));

            await using var manager = new P2PConnectionManager(options, registry);
            await manager.ConnectAsync();
            await Task.Delay(20);
            await manager.DisconnectAsync();
        }).ToArray();

        await Task.WhenAll(tasks);
        await host.StopAsync();
    }

    [Test]
    public async Task ChaosTcpAdapter_ControlDisconnects_TriggersAutomaticReconnect()
    {
        var hostOptions = new P2PConnectionHostOptions
        {
            NodeId = "chaos-reconnect-host",
            TransportListenerOptions = new()
            {
                Address = IPAddress.Loopback,
                Port = 0,
                SessionPairTimeout = TimeSpan.FromSeconds(5)
            }
        };

        var acceptedCount = 0;
        await using var host = new P2PConnectionHost(hostOptions);
        host.ConnectionAccepted += context =>
        {
            Interlocked.Increment(ref acceptedCount);

            return Task.CompletedTask;
        };
        await host.StartAsync();
        var endPoint = (IPEndPoint?)host.LocalEndPoint ?? throw new InvalidOperationException();

        var disconnects = 0;
        var controlChaos = new ChaosStreamOptions
        {
            DisconnectMessage = "control-drop",
            ShouldDisconnect = context =>
            {
                if (context.Operation != ChaosOperation.Read)
                    return false;

                if (Volatile.Read(ref disconnects) >= 2)
                    return false;

                if (context.OperationNumber < 6)
                    return false;

                return Interlocked.Increment(ref disconnects) <= 2;
            }
        };

        var registry = new P2PTransportAdapterRegistry(
            [(P2PTransportNames.Tcp, (IP2PTransportAdapter)new ChaosTcpTransportAdapter(null, controlChaos))]);

        var options = new P2PConnectionOptions(
            "chaos-reconnect-client",
            [new("127.0.0.1", endPoint.Port, transport: P2PTransportNames.Tcp)],
            TimeSpan.FromSeconds(5),
            TimeSpan.FromMilliseconds(120),
            heartbeatInterval: TimeSpan.FromMilliseconds(100),
            heartbeatTimeout: TimeSpan.FromMilliseconds(300));

        await using var manager = new P2PConnectionManager(options, registry);
        var initialHandle = await manager.ConnectAsync();
        Assert.IsTrue(
            await WaitForConditionAsync(() => Volatile.Read(ref acceptedCount) >= 1, TimeSpan.FromSeconds(2)),
            "Хост не подтвердил первичное подключение.");

        Assert.IsTrue(
            await WaitForConditionAsync(() => Volatile.Read(ref acceptedCount) >= 2, TimeSpan.FromSeconds(15)),
            "Хост не принял повторное соединение.");

        Assert.IsTrue(
            await WaitForConditionAsync(
                () =>
                {
                    var current = manager.CurrentConnection;

                    return current is not null
                           && !string.Equals(current.RemoteSessionId, initialHandle.RemoteSessionId, StringComparison.Ordinal);
                },
                TimeSpan.FromSeconds(10)),
            "Менеджер не переключился на новую сессию.");

        Assert.IsTrue(
            await WaitForConditionAsync(() => manager.State == P2PConnectionState.Established, TimeSpan.FromSeconds(5)),
            "Менеджер не вернулся в состояние Established.");
        Assert.IsTrue(
            await WaitForFreshHeartbeatAsync(manager, options.HeartbeatTimeout, TimeSpan.FromSeconds(5)),
            "После реконнекта не возобновились heartbeat.");

        Assert.IsTrue(Volatile.Read(ref disconnects) >= 1, "Хаос-инжектор не сработал.");

        await manager.DisconnectAsync();
        await host.StopAsync();
    }

    [Test]
    public async Task ChaosTcpAdapter_ReconnectUpdatesMetrics()
    {
        var hostOptions = new P2PConnectionHostOptions
        {
            NodeId = "chaos-metrics-host",
            TransportListenerOptions = new()
            {
                Address = IPAddress.Loopback,
                Port = 0,
                SessionPairTimeout = TimeSpan.FromSeconds(5)
            }
        };

        var acceptedCount = 0;
        await using var host = new P2PConnectionHost(hostOptions);
        host.ConnectionAccepted += context =>
        {
            Interlocked.Increment(ref acceptedCount);

            return Task.CompletedTask;
        };
        await host.StartAsync();
        var endPoint = (IPEndPoint?)host.LocalEndPoint ?? throw new InvalidOperationException();

        var disconnectTriggered = 0;
        var controlChaos = new ChaosStreamOptions
        {
            DisconnectMessage = "metrics-control-drop",
            ShouldDisconnect = context =>
            {
                if (context.Operation != ChaosOperation.Read)
                    return false;

                if (context.OperationNumber < 6)
                    return false;

                return Interlocked.CompareExchange(ref disconnectTriggered, 1, 0) == 0;
            }
        };

        var registry = new P2PTransportAdapterRegistry(
            [(P2PTransportNames.Tcp, (IP2PTransportAdapter)new ChaosTcpTransportAdapter(null, controlChaos))]);

        var options = new P2PConnectionOptions(
            "chaos-metrics-client",
            [new("127.0.0.1", endPoint.Port, transport: P2PTransportNames.Tcp)],
            TimeSpan.FromSeconds(5),
            TimeSpan.FromMilliseconds(120),
            heartbeatInterval: TimeSpan.FromMilliseconds(100),
            heartbeatTimeout: TimeSpan.FromMilliseconds(300));

        await using var manager = new P2PConnectionManager(options, registry);
        var firstHandle = await manager.ConnectAsync();

        Assert.IsTrue(
            await WaitForConditionAsync(() => Volatile.Read(ref acceptedCount) >= 1, TimeSpan.FromSeconds(2)),
            "Хост не подтвердил первичное подключение.");
        Assert.IsTrue(
            await WaitForFreshHeartbeatAsync(manager, options.HeartbeatTimeout, TimeSpan.FromSeconds(5)),
            "Heartbeat не активировался после первого подключения.");

        var metricsBefore = manager.GetMetrics();

        Assert.IsTrue(
            await WaitForConditionAsync(() => Volatile.Read(ref disconnectTriggered) == 1, TimeSpan.FromSeconds(10)),
            "Хаос-инжектор не отключил управляющий канал.");
        Assert.IsTrue(
            await WaitForConditionAsync(() => Volatile.Read(ref acceptedCount) >= 2, TimeSpan.FromSeconds(15)),
            "Хост не принял повторное соединение.");
        Assert.IsTrue(
            await WaitForConditionAsync(
                () =>
                {
                    var current = manager.CurrentConnection;

                    return current is not null
                           && !string.Equals(current.RemoteSessionId, firstHandle.RemoteSessionId, StringComparison.Ordinal);
                },
                TimeSpan.FromSeconds(10)),
            "Менеджер не переключился на новое соединение.");
        Assert.IsTrue(
            await WaitForConditionAsync(() => manager.State == P2PConnectionState.Established, TimeSpan.FromSeconds(10)),
            "Менеджер не вернулся в состояние Established после реконнекта.");
        Assert.IsTrue(
            await WaitForFreshHeartbeatAsync(manager, options.HeartbeatTimeout, TimeSpan.FromSeconds(5)),
            "Heartbeat не восстановился после реконнекта.");

        var metricsAfter = manager.GetMetrics();

        Assert.IsTrue(metricsAfter.ReconnectSuccessRatio > metricsBefore.ReconnectSuccessRatio);
        Assert.IsTrue(metricsAfter.LastSwitchDuration > TimeSpan.Zero);
        Assert.AreNotEqual(DateTimeOffset.MinValue, metricsAfter.LastHeartbeatAckUtc);
        await manager.DisconnectAsync();
        await host.StopAsync();
    }

    [Test]
    public async Task ChaosTcpAdapter_HeavyTailJitterDoesNotTriggerFalsePositive()
    {
        var hostOptions = new P2PConnectionHostOptions
        {
            NodeId = "chaos-jitter-host",
            TransportListenerOptions = new()
            {
                Address = IPAddress.Loopback,
                Port = 0,
                SessionPairTimeout = TimeSpan.FromSeconds(5)
            }
        };

        var acceptedCount = 0;
        await using var host = new P2PConnectionHost(hostOptions);
        host.ConnectionAccepted += context =>
        {
            Interlocked.Increment(ref acceptedCount);

            return Task.CompletedTask;
        };
        await host.StartAsync();
        var endPoint = (IPEndPoint?)host.LocalEndPoint ?? throw new InvalidOperationException();

        using var jitterRandom =
            new ThreadLocal<Random>(() => new(unchecked(Environment.TickCount * 397) ^ Thread.CurrentThread.ManagedThreadId));

        var dataChaos = new ChaosStreamOptions
        {
            Random = new(1234),
            ReadDelayProbability = 1,
            MinReadDelay = TimeSpan.FromMilliseconds(20),
            MaxReadDelay = TimeSpan.FromMilliseconds(80),
            WriteDelayProbability = 1,
            MinWriteDelay = TimeSpan.FromMilliseconds(20),
            MaxWriteDelay = TimeSpan.FromMilliseconds(80),
            ShouldApplyReadNoise = _ => true,
            ShouldApplyWriteNoise = _ => true,
            ReadPerByteDelay = TimeSpan.FromMilliseconds(0.05),
            WritePerByteDelay = TimeSpan.FromMilliseconds(0.05),
            StallDurationProvider = _ =>
            {
                var rnd = jitterRandom.Value!;
                var roll = rnd.NextDouble();

                if (roll < 0.12)
                    return TimeSpan.FromMilliseconds(280 + rnd.Next(0, 120));
                if (roll < 0.45)
                    return TimeSpan.FromMilliseconds(120 + rnd.Next(0, 100));

                return TimeSpan.Zero;
            }
        };

        var controlChaos = new ChaosStreamOptions
        {
            Random = new(5678),
            ReadDelayProbability = 1,
            MinReadDelay = TimeSpan.FromMilliseconds(25),
            MaxReadDelay = TimeSpan.FromMilliseconds(100),
            WriteDelayProbability = 1,
            MinWriteDelay = TimeSpan.FromMilliseconds(25),
            MaxWriteDelay = TimeSpan.FromMilliseconds(100),
            ShouldApplyReadNoise = _ => true,
            ShouldApplyWriteNoise = _ => true,
            StallDurationProvider = _ =>
            {
                var rnd = jitterRandom.Value!;
                var roll = rnd.NextDouble();

                if (roll < 0.08)
                    return TimeSpan.FromMilliseconds(200 + rnd.Next(0, 100));
                if (roll < 0.35)
                    return TimeSpan.FromMilliseconds(110 + rnd.Next(0, 80));

                return TimeSpan.Zero;
            }
        };

        var registry = new P2PTransportAdapterRegistry(
            [(P2PTransportNames.Tcp, (IP2PTransportAdapter)new ChaosTcpTransportAdapter(dataChaos, controlChaos))]);

        var options = new P2PConnectionOptions(
            "chaos-jitter-client",
            [new("127.0.0.1", endPoint.Port, transport: P2PTransportNames.Tcp)],
            TimeSpan.FromSeconds(6),
            TimeSpan.FromMilliseconds(150),
            heartbeatInterval: TimeSpan.FromMilliseconds(1800),
            heartbeatTimeout: TimeSpan.FromSeconds(60));

        await using var manager = new P2PConnectionManager(options, registry);

        var unexpectedStateTransitions = 0;
        var stateHistory = new ConcurrentQueue<string>();
        manager.ConnectionStateChanged += (_, args) =>
        {
            stateHistory.Enqueue($"{args.PreviousState}->{args.CurrentState}:{args.Cause}:{args.Evidence}");
            if (args.CurrentState is P2PConnectionState.Reconnecting or P2PConnectionState.Failed)
                Interlocked.Increment(ref unexpectedStateTransitions);
        };

        await manager.ConnectAsync();

        Assert.IsTrue(
            await WaitForConditionAsync(() => Volatile.Read(ref acceptedCount) >= 1, TimeSpan.FromSeconds(2)),
            "Хост не подтвердил первичное подключение.");

        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(6);
        while (DateTimeOffset.UtcNow < deadline)
        {
            Assert.IsTrue(
                await WaitForFreshHeartbeatAsync(manager, options.HeartbeatTimeout, TimeSpan.FromSeconds(2)),
                "Heartbeat не подтверждается в heavy-tail профиле.");
            await Task.Delay(100);
        }

        Assert.IsTrue(
            Volatile.Read(ref unexpectedStateTransitions) == 0,
            $"Обнаружены неожиданныe переходы: {string.Join(" | ", stateHistory)}");
        Assert.AreEqual(P2PConnectionState.Established, manager.State);

        await manager.DisconnectAsync();
        await host.StopAsync();
    }

    [Test]
    public async Task ChaosTcpAdapter_DataChannelDisconnectTriggersReconnect()
    {
        var hostOptions = new P2PConnectionHostOptions
        {
            NodeId = "chaos-data-drop-host",
            TransportListenerOptions = new()
            {
                Address = IPAddress.Loopback,
                Port = 0,
                SessionPairTimeout = TimeSpan.FromSeconds(5)
            }
        };

        var acceptedCount = 0;
        await using var host = new P2PConnectionHost(hostOptions);
        host.ConnectionAccepted += context =>
        {
            Interlocked.Increment(ref acceptedCount);

            return Task.CompletedTask;
        };
        await host.StartAsync();
        var endPoint = (IPEndPoint?)host.LocalEndPoint ?? throw new InvalidOperationException();

        var dataDisconnectTriggered = 0;

        var dataChaos = new ChaosStreamOptions
        {
            DisconnectMessage = "data-drop",
            ShouldDisconnect = context =>
            {
                if (context.Operation != ChaosOperation.Write)
                    return false;

                if (Volatile.Read(ref dataDisconnectTriggered) == 1)
                    return false;

                return Interlocked.CompareExchange(ref dataDisconnectTriggered, 1, 0) == 0;
            },
            ShouldApplyWriteNoise = context => context.OperationNumber == 2
        };

        var controlChaos = new ChaosStreamOptions
        {
            DisconnectMessage = "control-drop-blocked",
            ShouldDisconnect = _ => false
        };

        var registry = new P2PTransportAdapterRegistry(
            [(P2PTransportNames.Tcp, (IP2PTransportAdapter)new ChaosTcpTransportAdapter(dataChaos, controlChaos))]);

        var options = new P2PConnectionOptions(
            "chaos-data-drop-client",
            [new("127.0.0.1", endPoint.Port, transport: P2PTransportNames.Tcp)],
            TimeSpan.FromSeconds(6),
            TimeSpan.FromMilliseconds(100),
            heartbeatInterval: TimeSpan.FromMilliseconds(200),
            heartbeatTimeout: TimeSpan.FromMilliseconds(600));

        await using var manager = new P2PConnectionManager(options, registry);

        await manager.ConnectAsync();

        Assert.IsTrue(
            await WaitForConditionAsync(() => Volatile.Read(ref acceptedCount) >= 1, TimeSpan.FromSeconds(2)),
            "Хост не подтвердил первичное подключение.");

        var handle = manager.CurrentConnection ?? throw new InvalidOperationException();
        var payload = new byte[2048];
        Random.Shared.NextBytes(payload);

        var writeException =
            Assert.ThrowsAsync<IOException>(async () => { await handle.DataStream.WriteAsync(payload, 0, payload.Length); });
        Assert.That(writeException.Message, Does.Contain("data-drop").IgnoreCase);
        Assert.AreEqual(1, Volatile.Read(ref dataDisconnectTriggered));

        Assert.IsTrue(
            await WaitForConditionAsync(() => Volatile.Read(ref acceptedCount) >= 2, TimeSpan.FromSeconds(10)),
            "Хост не принял повторное соединение после потери data.");

        Assert.IsTrue(
            await WaitForConditionAsync(
                () =>
                {
                    var current = manager.CurrentConnection;

                    return current is not null && !string.Equals(current.RemoteSessionId, handle.RemoteSessionId, StringComparison.Ordinal);
                },
                TimeSpan.FromSeconds(10)),
            "Менеджер не переключился на новое соединение после обрыва data.");

        Assert.IsTrue(
            await WaitForConditionAsync(() => manager.State == P2PConnectionState.Established, TimeSpan.FromSeconds(5)),
            "Менеджер не вернулся в Established после реконнекта.");

        Assert.IsTrue(
            await WaitForFreshHeartbeatAsync(manager, options.HeartbeatTimeout, TimeSpan.FromSeconds(5)),
            "Heartbeat не восстанавливается после переключения.");

        await manager.DisconnectAsync();
        await host.StopAsync();
    }

    [Test]
    public async Task ChaosTcpAdapter_ControlAndDataDisconnectWithDelay_ReconnectsCleanly()
    {
        var hostOptions = new P2PConnectionHostOptions
        {
            NodeId = "chaos-both-drop-host",
            TransportListenerOptions = new()
            {
                Address = IPAddress.Loopback,
                Port = 0,
                SessionPairTimeout = TimeSpan.FromSeconds(5)
            }
        };

        var acceptedCount = 0;
        await using var host = new P2PConnectionHost(hostOptions);
        host.ConnectionAccepted += context =>
        {
            Interlocked.Increment(ref acceptedCount);

            return Task.CompletedTask;
        };
        await host.StartAsync();
        var endPoint = (IPEndPoint?)host.LocalEndPoint ?? throw new InvalidOperationException();

        var dataDisconnectTriggered = 0;
        var controlDisconnectTriggered = 0;
        var stateHistory = new ConcurrentQueue<P2PConnectionState>();

        var dataChaos = new ChaosStreamOptions
        {
            DisconnectMessage = "data-drop",
            ShouldDisconnect = context =>
            {
                if (context.Operation != ChaosOperation.Write)
                    return false;

                if (Volatile.Read(ref dataDisconnectTriggered) == 1)
                    return false;

                if (Volatile.Read(ref acceptedCount) != 1)
                    return false;

                return context.OperationNumber >= 2 && Interlocked.CompareExchange(ref dataDisconnectTriggered, 1, 0) == 0;
            }
        };

        var controlChaos = new ChaosStreamOptions
        {
            DisconnectMessage = "control-drop",
            ShouldDisconnect = context =>
            {
                if (context.Operation != ChaosOperation.Read)
                    return false;

                if (Volatile.Read(ref controlDisconnectTriggered) == 1)
                    return false;

                if (Volatile.Read(ref dataDisconnectTriggered) == 0)
                    return false;

                if (Volatile.Read(ref acceptedCount) != 1)
                    return false;

                return Interlocked.CompareExchange(ref controlDisconnectTriggered, 1, 0) == 0;
            }
        };

        var registry = new P2PTransportAdapterRegistry(
            [(P2PTransportNames.Tcp, (IP2PTransportAdapter)new ChaosTcpTransportAdapter(dataChaos, controlChaos))]);

        var options = new P2PConnectionOptions(
            "chaos-both-drop-client",
            [new("127.0.0.1", endPoint.Port, transport: P2PTransportNames.Tcp)],
            TimeSpan.FromSeconds(6),
            TimeSpan.FromMilliseconds(150),
            heartbeatInterval: TimeSpan.FromMilliseconds(250),
            heartbeatTimeout: TimeSpan.FromMilliseconds(750));

        await using var manager = new P2PConnectionManager(options, registry);

        manager.ConnectionStateChanged += (_, args) => { stateHistory.Enqueue(args.CurrentState); };

        await manager.ConnectAsync();

        Assert.IsTrue(
            await WaitForConditionAsync(() => Volatile.Read(ref acceptedCount) >= 1, TimeSpan.FromSeconds(2)),
            "Хост не подтвердил первичное подключение.");

        var initialHandle = manager.CurrentConnection ?? throw new InvalidOperationException();
        var payload = new byte[1024];
        Random.Shared.NextBytes(payload);

        var observedDataException = false;
        var attempts = 0;
        while (Volatile.Read(ref dataDisconnectTriggered) == 0 && attempts++ < 16)
        {
            try
            {
                await initialHandle.DataStream.WriteAsync(payload, 0, payload.Length);
            }
            catch (IOException ex)
            {
                if (ex.Message.Contains("data-drop", StringComparison.OrdinalIgnoreCase))
                    observedDataException = true;

                break;
            }

            await Task.Delay(10);
        }

        Assert.IsTrue(observedDataException, "Запись в data-канал не завершилась ожидаемым исключением.");
        Assert.AreEqual(1, Volatile.Read(ref dataDisconnectTriggered));

        Assert.IsTrue(
            await WaitForConditionAsync(() => Volatile.Read(ref acceptedCount) >= 2, TimeSpan.FromSeconds(15)),
            "Хост не принял повторное соединение после двойного обрыва.");

        Assert.IsTrue(
            await WaitForConditionAsync(
                () =>
                {
                    var current = manager.CurrentConnection;

                    return current is not null
                           && !string.Equals(current.RemoteSessionId, initialHandle.RemoteSessionId, StringComparison.Ordinal);
                },
                TimeSpan.FromSeconds(10)),
            "Менеджер не переключился на новое соединение после двойного обрыва.");

        Assert.IsTrue(
            await WaitForConditionAsync(() => manager.State == P2PConnectionState.Established, TimeSpan.FromSeconds(10)),
            $"Менеджер не вернулся в Established после реконнекта. Текущее состояние: {manager.State}. История: {string.Join("->", stateHistory)}");

        var metricsAfter = manager.GetMetrics();
        Assert.IsTrue(metricsAfter.ReconnectSuccessRatio > 0);
        Assert.IsTrue(metricsAfter.LastSwitchDuration > TimeSpan.Zero);

        await manager.DisconnectAsync();
        await host.StopAsync();
    }

    [Test]
    public async Task ChaosTcpAdapter_FrequentDisconnectsMaintainStateMachine()
    {
        const int reconnectCycles = 5;

        var hostOptions = new P2PConnectionHostOptions
        {
            NodeId = "chaos-multi-drop-host",
            TransportListenerOptions = new()
            {
                Address = IPAddress.Loopback,
                Port = 0,
                SessionPairTimeout = TimeSpan.FromSeconds(5)
            }
        };

        var acceptedCount = 0;
        await using var host = new P2PConnectionHost(hostOptions);
        host.ConnectionAccepted += context =>
        {
            Interlocked.Increment(ref acceptedCount);

            return Task.CompletedTask;
        };
        await host.StartAsync();
        var endPoint = (IPEndPoint?)host.LocalEndPoint ?? throw new InvalidOperationException();

        var dataDrops = 0;
        var controlDrops = 0;
        var stateHistory = new ConcurrentQueue<string>();

        var dataChaos = new ChaosStreamOptions
        {
            DisconnectMessage = "multi-data-drop",
            ShouldDisconnect = context =>
            {
                if (context.Operation != ChaosOperation.Write)
                    return false;

                var current = Volatile.Read(ref dataDrops);

                if (current >= reconnectCycles)
                    return false;

                // Каждые два обращения Write пытаемся обрушить data-канал
                if (context.OperationNumber % 2 == 0 && Interlocked.CompareExchange(ref dataDrops, current + 1, current) == current)
                    return true;

                return false;
            }
        };

        var controlChaos = new ChaosStreamOptions
        {
            DisconnectMessage = "multi-control-drop",
            ShouldDisconnect = context =>
            {
                if (context.Operation != ChaosOperation.Read)
                    return false;

                var current = Volatile.Read(ref controlDrops);
                var expected = Volatile.Read(ref dataDrops);

                if (current >= reconnectCycles || current >= expected)
                    return false;

                return Interlocked.CompareExchange(ref controlDrops, current + 1, current) == current;
            }
        };

        var registry = new P2PTransportAdapterRegistry(
            [(P2PTransportNames.Tcp, (IP2PTransportAdapter)new ChaosTcpTransportAdapter(dataChaos, controlChaos))]);

        var options = new P2PConnectionOptions(
            "chaos-multi-drop-client",
            [new("127.0.0.1", endPoint.Port, transport: P2PTransportNames.Tcp)],
            TimeSpan.FromSeconds(6),
            TimeSpan.FromMilliseconds(120),
            heartbeatInterval: TimeSpan.FromMilliseconds(200),
            heartbeatTimeout: TimeSpan.FromMilliseconds(800));

        await using var manager = new P2PConnectionManager(options, registry);

        manager.ConnectionStateChanged += (_, args) =>
        {
            stateHistory.Enqueue($"{args.PreviousState}->{args.CurrentState}:{args.Cause}:{args.Evidence}");
        };

        await manager.ConnectAsync();

        Assert.IsTrue(
            await WaitForConditionAsync(() => Volatile.Read(ref acceptedCount) >= 1, TimeSpan.FromSeconds(2)),
            "Хост не подтвердил первичное подключение.");

        var payload = new byte[1024];
        Random.Shared.NextBytes(payload);
        var previousSessionId = manager.CurrentConnection?.RemoteSessionId ?? throw new InvalidOperationException();

        for (var i = 0; i < reconnectCycles; i++)
        {
            var targetDrop = i + 1;
            var dropDeadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(5);
            while (Volatile.Read(ref dataDrops) < targetDrop && DateTimeOffset.UtcNow < dropDeadline)
            {
                try
                {
                    await manager.CurrentConnection!.DataStream.WriteAsync(payload, 0, payload.Length);
                }
                catch (IOException ex)
                {
                    Assert.That(ex.Message, Does.Contain("multi-data-drop").IgnoreCase);

                    break;
                }

                await Task.Delay(10);
            }

            Assert.IsTrue(
                await WaitForConditionAsync(
                    () => Volatile.Read(ref acceptedCount) >= targetDrop + 1,
                    TimeSpan.FromSeconds(15)),
                $"Хост не зарегистрировал повторное подключение #{targetDrop}. Состояния: {string.Join(" | ", stateHistory)}");

            Assert.IsTrue(
                await WaitForConditionAsync(
                    () =>
                    {
                        var current = manager.CurrentConnection;

                        return current is not null && !string.Equals(current.RemoteSessionId, previousSessionId, StringComparison.Ordinal);
                    },
                    TimeSpan.FromSeconds(10)),
                $"Менеджер не сменил сессию после реконнекта #{targetDrop}. Состояния: {string.Join(" | ", stateHistory)}");

            previousSessionId = manager.CurrentConnection!.RemoteSessionId;

            Assert.IsTrue(
                await WaitForConditionAsync(() => manager.State == P2PConnectionState.Established, TimeSpan.FromSeconds(5)),
                $"Менеджер не вернулся в Established после реконнекта #{targetDrop}.");

            var metrics = manager.GetMetrics();
            Assert.IsTrue(metrics.LastSwitchDuration > TimeSpan.Zero);
            Assert.IsTrue(metrics.ReconnectSuccessRatio > 0);

            Assert.IsTrue(
                await WaitForFreshHeartbeatAsync(manager, options.HeartbeatTimeout, TimeSpan.FromSeconds(5)),
                $"Heartbeat не восстановился после реконнекта #{targetDrop}.");
        }

        await manager.DisconnectAsync();
        await host.StopAsync();
    }

    private static P2PTransportConnection CreateStubTransportConnection(
        P2PTransportEndpoint endpoint,
        CancellationToken cancellationToken,
        string remoteNodeId)
    {
        var controlChannel = new StubControlChannel(remoteNodeId);
        var dataStream = new MemoryStream();

        async ValueTask DisposeControlAsync()
        {
            controlChannel.Complete();
            await Task.CompletedTask;
        }

        return new(
            dataStream,
            controlChannel,
            new DnsEndPoint(endpoint.Host, endpoint.Port),
            null,
            DisposeControlAsync)
        {
            TransportCancellationToken = cancellationToken
        };
    }

    private static async Task<bool> WaitForConditionAsync(
        Func<bool> predicate,
        TimeSpan timeout,
        TimeSpan? pollInterval = null)
    {
        var interval = pollInterval ?? TimeSpan.FromMilliseconds(20);
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow <= deadline)
        {
            if (predicate())
                return true;

            await Task.Delay(interval);
        }

        return false;
    }

    private static async Task<bool> WaitForFreshHeartbeatAsync(
        P2PConnectionManager manager,
        TimeSpan maxStaleness,
        TimeSpan timeout)
    {
        var pollInterval = TimeSpan.FromMilliseconds(25);
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow <= deadline)
        {
            var metrics = manager.GetMetrics();

            if (metrics.LastHeartbeatAckUtc != DateTimeOffset.MinValue
                && DateTimeOffset.UtcNow - metrics.LastHeartbeatAckUtc <= maxStaleness)
                return true;

            await Task.Delay(pollInterval);
        }

        return false;
    }

    private static void ForceRst(P2PTransportConnection connection)
    {
        if (connection.DataStream is IAsyncDisposable asyncDisposable)
            asyncDisposable.DisposeAsync().AsTask().GetAwaiter().GetResult();
        else
            connection.DataStream.Dispose();

        if (connection.ControlChannel is IAsyncDisposable controlDisposable)
            controlDisposable.DisposeAsync().AsTask().GetAwaiter().GetResult();

        if (connection.UnderlyingSocket is { } socket)
        {
            try
            {
                socket.LingerState = new(true, 0);
            }
            catch { }

            try
            {
                socket.Close(0);
            }
            catch { }
        }
    }

    private static async Task InvokePrivateAsync(object target, MethodInfo method, params object?[] parameters)
    {
        var result = method.Invoke(target, parameters);
        if (result is Task task)
            await task.ConfigureAwait(false);
    }


    private sealed class StubTransportAdapter : IP2PTransportAdapter
    {
        private readonly string _remoteNodeId;

        public StubTransportAdapter(string remoteNodeId) => this._remoteNodeId = remoteNodeId;

        public Task<P2PTransportConnection> ConnectAsync(
            P2PTransportEndpoint endpoint,
            P2PConnectionOptions options,
            CancellationToken cancellationToken)
        {
            var controlChannel = new StubControlChannel(this._remoteNodeId);
            var dataStream = new MemoryStream();

            async ValueTask DisposeControlAsync()
            {
                controlChannel.Complete();
                await Task.CompletedTask;
            }

            var transport = new P2PTransportConnection(
                dataStream,
                controlChannel,
                new DnsEndPoint(endpoint.Host, endpoint.Port),
                null,
                DisposeControlAsync)
            {
                TransportCancellationToken = cancellationToken
            };

            return Task.FromResult(transport);
        }
    }

    private sealed class ProgrammableControlChannel : IP2PControlChannel
    {
        private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly Func<P2PHandshakeEnvelope, P2PHandshakeEnvelope> _handshakeResponder;
        private readonly string _remoteNodeId;

        private readonly Channel<P2PControlFrame> _responses = Channel.CreateUnbounded<P2PControlFrame>(
            new()
            {
                SingleReader = true,
                SingleWriter = false
            });

        private int _handshakeCompleted;

        public ProgrammableControlChannel(string remoteNodeId, Func<P2PHandshakeEnvelope, P2PHandshakeEnvelope> handshakeResponder)
        {
            this._remoteNodeId = remoteNodeId;
            this._handshakeResponder = handshakeResponder ?? throw new ArgumentNullException(nameof(handshakeResponder));
        }

        public List<P2PControlFrame> SentDiagnostics { get; } = [];

        public ValueTask SendAsync(P2PControlFrame frame, CancellationToken cancellationToken)
        {
            if (frame.Type == P2PControlFrameType.Diagnostics)
            {
                if (this._handshakeCompleted == 0)
                {
                    var envelope = JsonSerializer.Deserialize<P2PHandshakeEnvelope>(frame.Payload.Span, SerializerOptions);
                    if (envelope is not null)
                    {
                        var response = this._handshakeResponder(envelope)
                                       ?? throw new InvalidOperationException("Handshake responder returned null.");
                        response.NodeId ??= this._remoteNodeId;
                        response.TimestampTicks = response.TimestampTicks == 0 ? DateTimeOffset.UtcNow.UtcTicks : response.TimestampTicks;
                        response.SessionId ??= $"session-{Guid.NewGuid():N}";

                        var payload = JsonSerializer.SerializeToUtf8Bytes(response, SerializerOptions);
                        var responseFrame = new P2PControlFrame(
                            P2PControlFrameType.Diagnostics,
                            DateTimeOffset.UtcNow.UtcTicks,
                            frame.Sequence,
                            payload);
                        this._responses.Writer.TryWrite(responseFrame);
                        this._handshakeCompleted = 1;

                        return ValueTask.CompletedTask;
                    }
                }
                else
                {
                    this.SentDiagnostics.Add(frame);
                }
            }
            else if (frame.Type == P2PControlFrameType.Ping)
            {
                var pong = P2PControlFrame.Pong(frame.TimestampUtcTicks, frame.Sequence, ReadOnlyMemory<byte>.Empty);
                this._responses.Writer.TryWrite(pong);
            }

            return ValueTask.CompletedTask;
        }

        public async IAsyncEnumerable<P2PControlFrame> ReadFramesAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            while (await this._responses.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            while (this._responses.Reader.TryRead(out var frame))
                yield return frame;
        }

        public ValueTask FlushAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public void Complete() => this._responses.Writer.TryComplete();

        public void InjectDiagnostics(byte[] payload)
        {
            var frame = new P2PControlFrame(
                P2PControlFrameType.Diagnostics,
                DateTimeOffset.UtcNow.UtcTicks,
                0,
                payload);
            this._responses.Writer.TryWrite(frame);
        }
    }

    private sealed class TrackingStream : MemoryStream
    {
        private readonly TaskCompletionSource<bool> _disposed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TrackingStream(string name) => this.Name = name;

        public string Name { get; }

        public bool IsDisposed => this._disposed.Task.IsCompleted;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                this._disposed.TrySetResult(true);

            base.Dispose(disposing);
        }

        public override ValueTask DisposeAsync()
        {
            this.Dispose(true);

            return ValueTask.CompletedTask;
        }

        public Task WaitDisposedAsync(TimeSpan timeout) => this._disposed.Task.WaitAsync(timeout);
    }

    private sealed class ProgrammableTransportAdapter : IP2PTransportAdapter
    {
        private readonly Func<P2PTransportEndpoint, P2PConnectionOptions, CancellationToken, Task<P2PTransportConnection>> _connectAsync;
        private readonly Action<P2PTransportEndpoint>? _onAttempt;

        public ProgrammableTransportAdapter(
            Action<P2PTransportEndpoint>? onAttempt,
            Func<P2PTransportEndpoint, P2PConnectionOptions, CancellationToken, Task<P2PTransportConnection>> connectAsync)
        {
            this._onAttempt = onAttempt;
            this._connectAsync = connectAsync ?? throw new ArgumentNullException(nameof(connectAsync));
        }

        public Task<P2PTransportConnection> ConnectAsync(
            P2PTransportEndpoint endpoint,
            P2PConnectionOptions options,
            CancellationToken cancellationToken)
        {
            this._onAttempt?.Invoke(endpoint);

            return this._connectAsync(endpoint, options, cancellationToken);
        }
    }

    private sealed class StubControlChannel : IP2PControlChannel
    {
        private const int ServerHelloType = 1;

        private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly Channel<P2PControlFrame> _inbound =
            Channel.CreateUnbounded<P2PControlFrame>(
                new()
                {
                    SingleReader = true,
                    SingleWriter = false
                });

        public StubControlChannel(string remoteNodeId)
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(
                new HandshakeResponse
                {
                    Type = ServerHelloType,
                    NodeId = remoteNodeId,
                    SessionId = $"remote-{Guid.NewGuid():N}",
                    SessionEpoch = 0,
                    ResumeToken = null,
                    TimestampTicks = DateTimeOffset.UtcNow.UtcTicks,
                    Metadata = null
                },
                SerializerOptions);

            var handshakeFrame = new P2PControlFrame(
                P2PControlFrameType.Diagnostics,
                DateTimeOffset.UtcNow.UtcTicks,
                1,
                payload);

            this._inbound.Writer.TryWrite(handshakeFrame);
        }

        public ValueTask SendAsync(P2PControlFrame frame, CancellationToken cancellationToken)
        {
            if (frame.Type == P2PControlFrameType.Ping)
            {
                var pong = P2PControlFrame.Pong(frame.TimestampUtcTicks, frame.Sequence, ReadOnlyMemory<byte>.Empty);
                this._inbound.Writer.TryWrite(pong);
            }

            return ValueTask.CompletedTask;
        }

        public async IAsyncEnumerable<P2PControlFrame> ReadFramesAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            while (await this._inbound.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            while (this._inbound.Reader.TryRead(out var frame))
                yield return frame;
        }

        public ValueTask FlushAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public void Complete() => this._inbound.Writer.TryComplete();

        private sealed class HandshakeResponse
        {
            public int Type { get; set; }
            public string? NodeId { get; set; }
            public string? SessionId { get; set; }
            public long SessionEpoch { get; set; }
            public string? ResumeToken { get; set; }
            public long TimestampTicks { get; set; }
            public IReadOnlyDictionary<string, string>? Metadata { get; set; }
        }
    }

    private sealed class FailingHandshakeTransportAdapter : IP2PTransportAdapter
    {
        public Task<P2PTransportConnection> ConnectAsync(
            P2PTransportEndpoint endpoint,
            P2PConnectionOptions options,
            CancellationToken cancellationToken)
        {
            var controlChannel = new FailingHandshakeControlChannel();
            var dataStream = new MemoryStream();
            var transport = new P2PTransportConnection(
                dataStream,
                controlChannel,
                new DnsEndPoint(endpoint.Host, endpoint.Port),
                null,
                null);

            return Task.FromResult(transport);
        }
    }

    private sealed class FailingHandshakeControlChannel : IP2PControlChannel
    {
        private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly Channel<P2PControlFrame> _frames = Channel.CreateUnbounded<P2PControlFrame>();
        private int _handshakeSent;

        public async IAsyncEnumerable<P2PControlFrame> ReadFramesAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            while (await this._frames.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            while (this._frames.Reader.TryRead(out var frame))
                yield return frame;
        }

        public ValueTask SendAsync(P2PControlFrame frame, CancellationToken cancellationToken)
        {
            if (this._handshakeSent == 0 && frame.Type == P2PControlFrameType.Diagnostics)
            {
                this._handshakeSent = 1;
                var payload = JsonSerializer.SerializeToUtf8Bytes(
                    new
                    {
                        Type = 4,
                        NodeId = (string?)null,
                        SessionId = (string?)null,
                        SessionEpoch = 0L,
                        ResumeToken = (string?)null,
                        TimestampTicks = DateTimeOffset.UtcNow.UtcTicks,
                        Message = "mock-error",
                        Metadata = (IReadOnlyDictionary<string, string>?)null
                    },
                    Options);
                var response = new P2PControlFrame(
                    P2PControlFrameType.Diagnostics,
                    DateTimeOffset.UtcNow.UtcTicks,
                    frame.Sequence + 1,
                    payload);
                this._frames.Writer.TryWrite(response);
            }

            return ValueTask.CompletedTask;
        }

        public ValueTask FlushAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    private sealed class ChaosTcpTransportAdapter : IP2PTransportAdapter
    {
        private readonly ChaosStreamOptions? _controlOptions;
        private readonly ChaosStreamOptions? _dataOptions;

        public ChaosTcpTransportAdapter(ChaosStreamOptions? dataOptions, ChaosStreamOptions? controlOptions)
        {
            this._dataOptions = dataOptions?.Clone();
            this._controlOptions = controlOptions?.Clone();
        }

        public async Task<P2PTransportConnection> ConnectAsync(
            P2PTransportEndpoint endpoint,
            P2PConnectionOptions options,
            CancellationToken cancellationToken)
        {
            var sessionId = Guid.NewGuid();

            var dataSocket = CreateSocket();
            await dataSocket.ConnectAsync(new DnsEndPoint(endpoint.Host, endpoint.Port), cancellationToken).ConfigureAwait(false);
            await SendChannelHeaderAsync(dataSocket, sessionId, 0, cancellationToken).ConfigureAwait(false);
            var baseDataStream = new NetworkStream(dataSocket, false);
            Stream effectiveDataStream = this._dataOptions is null
                ? baseDataStream
                : new ChaosStream(baseDataStream, this._dataOptions, false);

            var controlSocket = CreateSocket();
            await controlSocket.ConnectAsync(new DnsEndPoint(endpoint.Host, endpoint.Port), cancellationToken).ConfigureAwait(false);
            await SendChannelHeaderAsync(controlSocket, sessionId, 1, cancellationToken).ConfigureAwait(false);
            var baseControlStream = new NetworkStream(controlSocket, false);
            Stream effectiveControlStream = this._controlOptions is null
                ? baseControlStream
                : new ChaosStream(baseControlStream, this._controlOptions, false);

            var controlChannel = new TestStreamControlChannel(effectiveControlStream);

            async ValueTask OnDisposeAsync()
            {
                if (effectiveControlStream is IAsyncDisposable asyncDisposable)
                    await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                else
                    effectiveControlStream.Dispose();

                CloseSocket(controlSocket);
            }

            return new(
                effectiveDataStream,
                controlChannel,
                dataSocket.RemoteEndPoint,
                dataSocket,
                OnDisposeAsync)
            {
                TransportCancellationToken = cancellationToken
            };
        }

        private static Socket CreateSocket()
        {
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
            {
                NoDelay = true
            };

            return socket;
        }

        private static void CloseSocket(Socket socket)
        {
            try
            {
                socket.Shutdown(SocketShutdown.Both);
            }
            catch
            {
                // ignore
            }

            socket.Dispose();
        }
    }

    private sealed class TestStreamControlChannel : IP2PControlChannel
    {
        private const int HeaderSize = sizeof(byte) + sizeof(long) + sizeof(long);
        private readonly Stream _stream;
        private readonly SemaphoreSlim _writeLock = new(1, 1);

        public TestStreamControlChannel(Stream stream) => this._stream = stream ?? throw new ArgumentNullException(nameof(stream));

        public async ValueTask SendAsync(P2PControlFrame frame, CancellationToken cancellationToken)
        {
            await this._writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var payloadLength = frame.Payload.Length;
                var headerBuffer = ArrayPool<byte>.Shared.Rent(HeaderSize + 5);
                try
                {
                    var span = headerBuffer.AsSpan();
                    span[0] = (byte)frame.Type;
                    BitConverter.TryWriteBytes(span.Slice(1), frame.TimestampUtcTicks);
                    BitConverter.TryWriteBytes(span.Slice(1 + sizeof(long)), frame.Sequence);
                    var written = WriteVarInt(span.Slice(HeaderSize), payloadLength);

                    await this._stream.WriteAsync(headerBuffer.AsMemory(0, HeaderSize + written), cancellationToken).ConfigureAwait(false);
                    if (payloadLength > 0)
                        await this._stream.WriteAsync(frame.Payload, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(headerBuffer);
                }
            }
            finally
            {
                this._writeLock.Release();
            }
        }

        public async IAsyncEnumerable<P2PControlFrame> ReadFramesAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var header = new byte[HeaderSize];
            while (true)
            {
                await this._stream.ReadExactlyAsync(header.AsMemory(), cancellationToken).ConfigureAwait(false);
                var type = (P2PControlFrameType)header[0];
                var timestamp = BitConverter.ToInt64(header, 1);
                var sequence = BitConverter.ToInt64(header, 1 + sizeof(long));

                var length = await ReadVarIntAsync(this._stream, cancellationToken).ConfigureAwait(false);
                var payload = ReadOnlyMemory<byte>.Empty;
                if (length > 0)
                {
                    var payloadBuffer = new byte[length];
                    await this._stream.ReadExactlyAsync(payloadBuffer.AsMemory(), cancellationToken).ConfigureAwait(false);
                    payload = payloadBuffer;
                }

                yield return new(type, timestamp, sequence, payload);
            }
        }

        public async ValueTask FlushAsync(CancellationToken cancellationToken)
        {
            await this._writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await this._stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                this._writeLock.Release();
            }
        }

        private static int WriteVarInt(Span<byte> destination, int value)
        {
            var index = 0;
            var current = (uint)value;
            while (current >= 0x80)
            {
                destination[index++] = (byte)(current | 0x80);
                current >>= 7;
            }

            destination[index++] = (byte)current;

            return index;
        }

        private static async ValueTask<int> ReadVarIntAsync(Stream stream, CancellationToken cancellationToken)
        {
            var result = 0;
            var shift = 0;
            var buffer = new byte[1];
            while (true)
            {
                await stream.ReadExactlyAsync(buffer, cancellationToken).ConfigureAwait(false);
                var b = buffer[0];
                result |= (b & 0x7F) << shift;

                if (b < 0x80)
                    return result;

                shift += 7;

                if (shift > 35)
                    throw new InvalidOperationException("Varint length is too large.");
            }
        }
    }

    private sealed class FakeNetworkEventSource : IP2PNetworkEventSource
    {
        public event EventHandler? NetworkAvailabilityLost;
        public event EventHandler? NetworkAddressChanged;

        public void Dispose() { }

        public void RaiseAvailabilityLost() => this.NetworkAvailabilityLost?.Invoke(this, EventArgs.Empty);

        public void RaiseAddressChanged() => this.NetworkAddressChanged?.Invoke(this, EventArgs.Empty);
    }
}