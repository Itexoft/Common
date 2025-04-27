// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Itexoft.Networking.Core;

namespace Itexoft.Networking.P2P;

/// <summary>
/// Implementation of <see cref="IP2PConnectionManager" /> that performs hedged dials, manages the JSON handshake
/// and switches connections using a make-before-break sequence.
/// </summary>
public sealed class P2PConnectionManager : IP2PConnectionManager
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly IP2PTransportAdapterResolver _adapterResolver;
    private readonly IDisposable _blacklistSubscription;
    private readonly ConcurrentDictionary<string, EndpointDialTracker> _dialTrackers = new();
    private readonly RetryBudget _globalRetryBudget;
    private readonly object _heartbeatStatsLock = new();
    private readonly double[] _heartbeatWindow = new double[256];
    private readonly Guid _instanceId = Guid.NewGuid();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastBroadcastTimes = new();
    private readonly IP2PNetworkEventSource? _networkEventSource;

    private readonly P2PConnectionOptions _options;
    private readonly Random _random = new();
    private readonly SemaphoreSlim _reconnectGate = new(1, 1);

    private readonly object _stateLock = new();
    private int _consecutiveFailures;
    private Task? _controlReaderTask;
    private long _controlSequence;
    private P2PConnectionHandle? _currentHandle;
    private bool _disposed;
    private double _heartbeatM2;
    private double _heartbeatMean;
    private long _heartbeatSamples;
    private Task? _heartbeatTask;
    private int _heartbeatWindowCount;
    private int _heartbeatWindowIndex;
    private DateTimeOffset _lastDataActivity;
    private FailureSeverity _lastFailureSeverity = FailureSeverity.Soft;
    private DateTimeOffset _lastHeartbeatAck;
    private DateTimeOffset _lastHeartbeatSent;
    private TimeSpan _lastSwitchDuration;
    private CancellationTokenSource? _livenessCts;

    private string _localSessionId;
    private double _phi;
    private double _reconnectSuccess;
    private double _resumeSuccess;
    private string? _resumeToken;
    private double _rttvar;
    private long _sessionEpoch;
    private double _srtt;

    private P2PConnectionState _state = P2PConnectionState.Disconnected;
    private TimeSpan _totalDowntime;
    private P2PTransportConnection? _transportConnection;
    private DateTimeOffset _uptimeStart;
    private int _writeProbeInFlight;

    public P2PConnectionManager(P2PConnectionOptions options, IP2PTransportAdapter transportAdapter, string? sessionId = null)
        : this(options, new SingleTransportAdapterResolver(transportAdapter), sessionId) { }

    public P2PConnectionManager(P2PConnectionOptions options, IP2PTransportAdapterResolver adapterResolver, string? sessionId = null)
    {
        this._options = options ?? throw new ArgumentNullException(nameof(options));
        this._adapterResolver = adapterResolver ?? throw new ArgumentNullException(nameof(adapterResolver));
        this._localSessionId = sessionId ?? Guid.NewGuid().ToString("N");
        this._uptimeStart = DateTimeOffset.UtcNow;
        this._lastHeartbeatSent = DateTimeOffset.MinValue;
        this._lastHeartbeatAck = DateTimeOffset.MinValue;
        this._lastDataActivity = DateTimeOffset.UtcNow;
        this._resumeToken = this._options.ResumeToken;
        this._globalRetryBudget = new(Math.Max(1, this._options.RetryBudgetCapacity), this._options.RetryBudgetWindow);

        if (this._options.EnableNetworkMonitoring)
        {
            this._networkEventSource = this._options.NetworkEventSource ?? new NetworkChangeEventSource();
            this._networkEventSource.NetworkAvailabilityLost += this.OnNetworkAvailabilityLost;
            this._networkEventSource.NetworkAddressChanged += this.OnNetworkAddressChanged;
        }

        this._blacklistSubscription = SharedBlacklistRegistry.Subscribe(this.OnSharedBlacklistUpdate);
    }

    public event EventHandler<P2PConnectionStateChangedEventArgs>? ConnectionStateChanged;

    public P2PConnectionHandle? CurrentConnection => this._currentHandle;

    public P2PConnectionState State => this._state;

    public async Task<P2PConnectionHandle> ConnectAsync(CancellationToken cancellationToken = default)
    {
        this.ThrowIfDisposed();

        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        connectCts.CancelAfter(this._options.ConnectTimeout);

        this.ChangeState(P2PConnectionState.Connecting, P2PConnectionTransitionCause.DialStarted, "dial started");
        var connection = await this.DialAsync(connectCts.Token).ConfigureAwait(false)
                         ?? throw new InvalidOperationException("Failed to connect to any configured endpoint.");

        this.ChangeState(
            P2PConnectionState.Handshake,
            P2PConnectionTransitionCause.HandshakeStarted,
            connection.RemoteEndPoint?.ToString());
        HandshakeResult handshake;
        try
        {
            handshake = await this.PerformHandshakeAsync(connection.ControlChannel, connectCts.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var severity = ClassifyFailure(ex);
            this._lastFailureSeverity = severity;
            this.ChangeState(P2PConnectionState.Failed, P2PConnectionTransitionCause.TransportError, ex.Message);

            throw;
        }

        var handle = await this.SwitchConnectionAsync(connection, handshake, connectCts.Token, TokenSourceKind.InitialDial)
            .ConfigureAwait(false);
        this.ChangeState(P2PConnectionState.Established, P2PConnectionTransitionCause.HandshakeCompleted, handshake.RemoteNodeId);

        return handle;
    }

    public async Task DisconnectAsync(DisconnectReason reason = DisconnectReason.Manual, CancellationToken cancellationToken = default)
    {
        if (this._disposed)
            return;

        CancellationTokenSource? ctsToDispose;
        P2PTransportConnection? transportToDispose;
        P2PConnectionHandle? handleToDispose;

        lock (this._stateLock)
        {
            if (this._disposed)
                return;

            this.ChangeState(P2PConnectionState.Disconnected, P2PConnectionTransitionCause.Manual, reason.ToString());
            ctsToDispose = this._livenessCts;
            this._livenessCts = null;
            transportToDispose = this._transportConnection;
            this._transportConnection = null;
            handleToDispose = this._currentHandle;
            this._currentHandle = null;
        }

        ctsToDispose?.Cancel();
        await Task.WhenAll(this._heartbeatTask.IgnoreExceptions(), this._controlReaderTask.IgnoreExceptions()).ConfigureAwait(false);

        if (handleToDispose is not null && transportToDispose is not null)
            await transportToDispose.DisposeAsync().ConfigureAwait(false);
    }

    public P2PConnectionMetrics GetMetrics()
    {
        var now = DateTimeOffset.UtcNow;
        List<P2PDialRateMetric>? dialRates = null;
        if (!this._dialTrackers.IsEmpty)
        {
            dialRates = new(this._dialTrackers.Count);
            foreach (var entry in this._dialTrackers)
            {
                var snapshot = entry.Value.GetSnapshot(now);
                dialRates.Add(
                    new()
                    {
                        EndpointKey = entry.Key,
                        AttemptsLastMinute = snapshot.AttemptsLastMinute,
                        BlacklistedUntilUtc = snapshot.BlacklistedUntil
                    });
            }
        }

        var totalAttempts = 0;
        var blacklisted = 0;
        if (dialRates is { Count: > 0 })
            foreach (var rate in dialRates)
            {
                totalAttempts += rate.AttemptsLastMinute;
                if (rate.BlacklistedUntilUtc > now)
                    blacklisted++;
            }

        return new()
        {
            SrttMilliseconds = this._srtt,
            RttVarianceMilliseconds = this._rttvar,
            Phi = this._phi,
            ConsecutiveFailures = this._consecutiveFailures,
            ReconnectSuccessRatio = this._reconnectSuccess,
            ResumeSuccessRatio = this._resumeSuccess,
            Uptime = now - this._uptimeStart,
            Downtime = this._totalDowntime,
            LastSwitchDuration = this._lastSwitchDuration,
            LastHeartbeatSentUtc = this._lastHeartbeatSent,
            LastHeartbeatAckUtc = this._lastHeartbeatAck,
            LastEndpointLabel = this._currentHandle?.Endpoint.Label,
            HeartbeatP50Milliseconds = this.GetHeartbeatPercentile(50),
            HeartbeatP95Milliseconds = this.GetHeartbeatPercentile(95),
            LastDataActivityUtc = this._lastDataActivity,
            TotalDialAttemptsLastMinute = totalAttempts,
            BlacklistedEndpointCount = blacklisted,
            DialRates = dialRates is null ? Array.Empty<P2PDialRateMetric>() : dialRates,
            LastFailureSeverity = this._lastFailureSeverity
        };
    }

    public async ValueTask DisposeAsync()
    {
        if (this._disposed)
            return;

        this._disposed = true;
        this.ChangeState(P2PConnectionState.Disposed, P2PConnectionTransitionCause.Disposal, null);
        this._blacklistSubscription.Dispose();
        if (this._networkEventSource is not null)
        {
            this._networkEventSource.NetworkAvailabilityLost -= this.OnNetworkAvailabilityLost;
            this._networkEventSource.NetworkAddressChanged -= this.OnNetworkAddressChanged;
            this._networkEventSource.Dispose();
        }

        await this.DisconnectAsync(DisconnectReason.Shutdown).ConfigureAwait(false);
    }

    public async Task<P2PConnectionHandle> ReconnectAsync(CancellationToken cancellationToken = default)
    {
        this.ThrowIfDisposed();

        if (this._currentHandle is null)
            return await this.ConnectAsync(cancellationToken).ConfigureAwait(false);

        using var reconnectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        reconnectCts.CancelAfter(this._options.ConnectTimeout);

        await this._reconnectGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            this.ChangeState(P2PConnectionState.Reconnecting, P2PConnectionTransitionCause.Manual, "manual reconnect requested");

            var connection = await this.DialAsync(reconnectCts.Token).ConfigureAwait(false)
                             ?? throw new InvalidOperationException("Failed to connect to any configured endpoint.");

            this.ChangeState(
                P2PConnectionState.Switching,
                P2PConnectionTransitionCause.SwitchStarted,
                connection.RemoteEndPoint?.ToString());
            try
            {
                var handshake = await this.PerformHandshakeAsync(connection.ControlChannel, reconnectCts.Token).ConfigureAwait(false);
                var handle = await this.SwitchConnectionAsync(connection, handshake, reconnectCts.Token, TokenSourceKind.Manual)
                    .ConfigureAwait(false);
                this.ChangeState(P2PConnectionState.Established, P2PConnectionTransitionCause.SwitchCompleted, handshake.RemoteNodeId);
                this._consecutiveFailures = 0;

                return handle;
            }
            catch
            {
                await connection.DisposeAsync().ConfigureAwait(false);

                throw;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            this._consecutiveFailures++;
            var severity = ClassifyFailure(ex);
            this._lastFailureSeverity = severity;
            this.ChangeState(P2PConnectionState.Failed, P2PConnectionTransitionCause.TransportError, ex.Message);

            throw;
        }
        finally
        {
            this._reconnectGate.Release();
        }
    }

    private async Task<P2PTransportConnection?> DialAsync(CancellationToken cancellationToken)
    {
        SharedBlacklistRegistry.PruneExpiredEntries(DateTimeOffset.UtcNow);

        var baseEndpoints = this._options.Endpoints;

        if (baseEndpoints.Count == 0)
            return null;

        var endpoints = baseEndpoints;
        if (baseEndpoints.Count > 1)
        {
            var hasPriority = false;
            for (var i = 0; i < baseEndpoints.Count; i++)
                if (baseEndpoints[i].Priority != 0)
                {
                    hasPriority = true;

                    break;
                }

            if (hasPriority)
                endpoints = baseEndpoints
                    .OrderBy(e => e.Priority)
                    .ThenBy(e => e.Label ?? e.Host, StringComparer.Ordinal)
                    .ToArray();
        }

        var lowestPriority = endpoints[0].Priority;
        for (var i = 1; i < endpoints.Count; i++)
            if (endpoints[i].Priority < lowestPriority)
                lowestPriority = endpoints[i].Priority;

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var tasks = new List<Task<P2PTransportConnection?>>();
        var scheduledCount = 0;
        var nextIndex = 0;
        TimeSpan? nextBlacklistDelay = null;

        bool TryScheduleNext()
        {
            while (nextIndex < endpoints.Count)
            {
                var endpoint = endpoints[nextIndex++];
                var endpointKey = GetEndpointKey(endpoint);
                var tracker = this._dialTrackers.GetOrAdd(endpointKey, _ => new());
                var now = DateTimeOffset.UtcNow;

                if (SharedBlacklistRegistry.TryGetPenalty(endpointKey, now, out var sharedPenalty))
                {
                    var shouldRespect = sharedPenalty.SourceId != this._instanceId || sharedPenalty.FromRetryAfter;
                    if (shouldRespect)
                    {
                        var remaining = sharedPenalty.ExpiresAt - now;
                        if (remaining > TimeSpan.Zero)
                        {
                            nextBlacklistDelay = nextBlacklistDelay.HasValue
                                ? remaining < nextBlacklistDelay.Value ? remaining : nextBlacklistDelay
                                : remaining;
                            tracker.RecordFailure(now, remaining);
                        }

                        continue;
                    }
                }

                if (!tracker.TryStart(now, this._options.MaxDialsPerMinutePerEndpoint, this._options.EndpointBlacklistDuration))
                {
                    var blacklistDelay = tracker.GetBlacklistDelay(now);
                    if (blacklistDelay is { } d && d > TimeSpan.Zero)
                        nextBlacklistDelay = nextBlacklistDelay.HasValue
                            ? d < nextBlacklistDelay.Value ? d : nextBlacklistDelay
                            : d;

                    continue;
                }

                var hedgedIndex = scheduledCount++;
                var hedgedDelay = hedgedIndex == 0
                    ? TimeSpan.Zero
                    : TimeSpan.FromMilliseconds(hedgedIndex * this._options.HappyEyeballsDelay.TotalMilliseconds);

                var priorityOffset = endpoint.Priority - lowestPriority;
                if (priorityOffset < 0)
                    priorityOffset = 0;

                var priorityDelayTicks = priorityOffset == 0
                    ? 0
                    : this._options.HappyEyeballsGroupDelay.Ticks * (long)priorityOffset;
                var priorityDelay = priorityDelayTicks > 0 ? TimeSpan.FromTicks(priorityDelayTicks) : TimeSpan.Zero;

                var delay = hedgedDelay + priorityDelay;
                tasks.Add(this.DialEndpointAsync(endpoint, tracker, delay, linkedCts.Token));

                return true;
            }

            return false;
        }

        while (true)
        {
            while (tasks.Count < this._options.MaxConcurrentDials && TryScheduleNext()) { }

            if (tasks.Count == 0)
            {
                if (nextBlacklistDelay.HasValue)
                {
                    await Task.Delay(nextBlacklistDelay.Value, cancellationToken).ConfigureAwait(false);
                    nextBlacklistDelay = null;
                    nextIndex = 0;
                    scheduledCount = 0;

                    continue;
                }

                return null;
            }

            var completed = await Task.WhenAny(tasks).ConfigureAwait(false);
            tasks.Remove(completed);

            var result = await completed.ConfigureAwait(false);
            if (result is not null)
            {
                linkedCts.Cancel();

                return result;
            }
        }
    }

    private async Task<P2PTransportConnection?> DialEndpointAsync(
        P2PTransportEndpoint endpoint,
        EndpointDialTracker tracker,
        TimeSpan delay,
        CancellationToken cancellationToken)
    {
        if (delay > TimeSpan.Zero)
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);

        while (true)
            try
            {
                var adapter = this._adapterResolver.Resolve(endpoint);

                if (!this._globalRetryBudget.TryConsume(DateTimeOffset.UtcNow))
                {
                    await Task.Delay(this._options.SoftFailureInitialBackoff, cancellationToken).ConfigureAwait(false);

                    continue;
                }

                var connection = await adapter.ConnectAsync(endpoint, this._options, cancellationToken).ConfigureAwait(false);
                tracker.RecordSuccess();
                this._globalRetryBudget.Refund(DateTimeOffset.UtcNow);
                this.ChangeState(this._state, P2PConnectionTransitionCause.DialSucceeded, connection.RemoteEndPoint?.ToString());

                return connection;
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (RetryAfterException retryAfter)
            {
                tracker.RecordFailure(DateTimeOffset.UtcNow, retryAfter.Delay);
                this.ChangeState(this._state, P2PConnectionTransitionCause.DialFailed, $"{endpoint}: retry-after {retryAfter.Delay}");
                this.BroadcastEndpointPenalty(GetEndpointKey(endpoint), FailureSeverity.Soft, retryAfter.Delay, true);
                await Task.Delay(retryAfter.Delay, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                tracker.RecordFailure(DateTimeOffset.UtcNow, this._options.EndpointBlacklistDuration);
                this.ChangeState(this._state, P2PConnectionTransitionCause.DialFailed, $"{endpoint}: {ex.Message}");
                this.BroadcastEndpointPenalty(
                    GetEndpointKey(endpoint),
                    ClassifyFailure(ex),
                    this._options.EndpointBlacklistDuration,
                    false);

                return null;
            }
    }

    private async Task<HandshakeResult> PerformHandshakeAsync(IP2PControlChannel controlChannel, CancellationToken cancellationToken)
    {
        var resumeToken = this._options.EnableSessionResume ? this._resumeToken ?? this._options.ResumeToken : null;
        var requestType = resumeToken is { Length: > 0 } ? P2PHandshakeMessageType.ResumeRequest : P2PHandshakeMessageType.ClientHello;

        var request = new P2PHandshakeEnvelope
        {
            Type = requestType,
            NodeId = this._options.NodeId,
            SessionId = this._localSessionId,
            SessionEpoch = this._sessionEpoch,
            ResumeToken = resumeToken,
            TimestampTicks = DateTimeOffset.UtcNow.UtcTicks,
            Metadata = this._options.Metadata
        };

        var payload = JsonSerializer.SerializeToUtf8Bytes(request, JsonOptions);
        var frame = new P2PControlFrame(
            P2PControlFrameType.Diagnostics,
            DateTimeOffset.UtcNow.UtcTicks,
            Interlocked.Increment(ref this._controlSequence),
            payload);

        await controlChannel.SendAsync(frame, cancellationToken).ConfigureAwait(false);
        await controlChannel.FlushAsync(cancellationToken).ConfigureAwait(false);

        await foreach (var responseFrame in controlChannel.ReadFramesAsync(cancellationToken))
        {
            if (responseFrame.Type != P2PControlFrameType.Diagnostics)
                continue;

            var response = JsonSerializer.Deserialize<P2PHandshakeEnvelope>(responseFrame.Payload.Span, JsonOptions);

            if (response is null)
                continue;

            switch (response.Type)
            {
                case P2PHandshakeMessageType.ServerHello:
                case P2PHandshakeMessageType.ResumeAcknowledge:
                    if (string.IsNullOrWhiteSpace(response.SessionId))
                        throw new InvalidOperationException("Server handshake response does not contain the sessionId.");

                    return new(
                        response.NodeId ?? "unknown",
                        response.SessionId,
                        response.SessionEpoch,
                        response.ResumeToken,
                        response.Type == P2PHandshakeMessageType.ResumeAcknowledge);
                case P2PHandshakeMessageType.Error:
                    throw new P2PHandshakeException(FailureSeverity.Fatal, response.Message ?? "Handshake rejected by remote peer.");
            }
        }

        throw new P2PHandshakeException(FailureSeverity.Hard, "Remote peer did not respond to handshake.");
    }

    private Task<P2PConnectionHandle> SwitchConnectionAsync(
        P2PTransportConnection connection,
        HandshakeResult handshake,
        CancellationToken cancellationToken,
        TokenSourceKind tokenKind)
    {
        var stopwatch = ValueStopwatch.StartNew();

        var monitoredDataStream = new FaultAwareStream(
            connection.DataStream,
            ex => this.SignalDataStreamFailureAsync(ex),
            () => this._lastDataActivity = DateTimeOffset.UtcNow);

        var newHandle = new P2PConnectionHandle(
            monitoredDataStream,
            connection.ControlChannel,
            handshake.RemoteNodeId,
            handshake.RemoteSessionId,
            this._localSessionId,
            handshake.RemoteEpoch,
            this.SelectEndpoint(connection.RemoteEndPoint));

        CancellationTokenSource newSupervisionCts;
        P2PTransportConnection? previousTransport = null;
        P2PConnectionHandle? previousHandle = null;
        CancellationTokenSource? previousCts = null;

        lock (this._stateLock)
        {
            if (this._disposed)
                throw new ObjectDisposedException(nameof(P2PConnectionManager));

            newSupervisionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            previousTransport = this._transportConnection;
            previousHandle = this._currentHandle;
            previousCts = this._livenessCts;

            this._transportConnection = connection;
            this._currentHandle = newHandle;
            this._livenessCts = newSupervisionCts;
            this._sessionEpoch = Math.Max(this._sessionEpoch + 1, handshake.RemoteEpoch);
        }

        previousCts?.Cancel();
        _ = DisposeTransportAsync(previousTransport, previousHandle);

        this.StartSupervision(newHandle, newSupervisionCts.Token);
        this._lastHeartbeatAck = DateTimeOffset.UtcNow;
        this._lastHeartbeatSent = this._lastHeartbeatAck;

        if (handshake.ResumeAccepted)
        {
            this._resumeSuccess = this._resumeSuccess * 0.8 + 0.2;
            if (this._options.EnableSessionResume && !string.IsNullOrEmpty(handshake.ResumeToken))
                this._resumeToken = handshake.ResumeToken;
        }
        else if (this._options.EnableSessionResume && !string.IsNullOrEmpty(handshake.ResumeToken))
        {
            this._resumeToken = handshake.ResumeToken;
        }

        this._reconnectSuccess = this._reconnectSuccess * 0.8 + 0.2;
        this._lastSwitchDuration = stopwatch.Elapsed;

        if (tokenKind == TokenSourceKind.InitialDial)
            this._uptimeStart = DateTimeOffset.UtcNow;

        return Task.FromResult(newHandle);
    }

    private Task SignalDataStreamFailureAsync(Exception exception)
    {
        this.ChangeState(P2PConnectionState.Degraded, P2PConnectionTransitionCause.TransportError, exception.Message);
        this.BroadcastCurrentEndpointPenalty(FailureSeverity.Hard, this._options.EndpointBlacklistDuration, false);

        return this.TriggerReconnectAsync("data stream failure", FailureSeverity.Hard, CancellationToken.None);
    }

    private void StartSupervision(P2PConnectionHandle handle, CancellationToken cancellationToken)
    {
        this._heartbeatTask = Task.Run(() => this.HeartbeatLoopAsync(handle.ControlChannel, cancellationToken), cancellationToken);
        this._controlReaderTask = Task.Run(() => this.ControlReaderLoopAsync(handle, cancellationToken), cancellationToken);
    }

    private async Task HeartbeatLoopAsync(IP2PControlChannel controlChannel, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var now = DateTimeOffset.UtcNow;
            var elapsedSinceData = now - this._lastDataActivity;
            if (elapsedSinceData >= this._options.HeartbeatPiggybackGrace)
            {
                this._lastHeartbeatSent = now;
                var heartbeat = P2PControlFrame.Ping(now.UtcTicks, Interlocked.Increment(ref this._controlSequence));
                await controlChannel.SendAsync(heartbeat, cancellationToken).ConfigureAwait(false);
                await controlChannel.FlushAsync(cancellationToken).ConfigureAwait(false);
                this._lastDataActivity = now;
            }

            var interval = this.ComputeHeartbeatInterval();
            try
            {
                await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            this.CheckHeartbeatTimeout(now);
        }
    }

    private async Task ControlReaderLoopAsync(P2PConnectionHandle handle, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var frame in handle.ControlChannel.ReadFramesAsync(cancellationToken))
                switch (frame.Type)
                {
                    case P2PControlFrameType.Ping:
                        await handle.ControlChannel.SendAsync(
                            P2PControlFrame.Pong(frame.TimestampUtcTicks, frame.Sequence, ReadOnlyMemory<byte>.Empty),
                            cancellationToken).ConfigureAwait(false);
                        await handle.ControlChannel.FlushAsync(cancellationToken).ConfigureAwait(false);

                        break;

                    case P2PControlFrameType.Pong:
                        var ackTime = DateTimeOffset.UtcNow;
                        var interval = this._lastHeartbeatAck == DateTimeOffset.MinValue
                            ? TimeSpan.Zero
                            : ackTime - this._lastHeartbeatAck;
                        this._lastHeartbeatAck = ackTime;
                        if (interval > TimeSpan.Zero)
                            this.RegisterHeartbeatSample(interval);

                        this.RegisterRttSample(TimeSpan.FromTicks(Math.Max(0, ackTime.UtcTicks - frame.TimestampUtcTicks)));
                        this._phi = this.ComputePhi(ackTime);
                        this._consecutiveFailures = 0;
                        this.ChangeState(P2PConnectionState.Established, P2PConnectionTransitionCause.HeartbeatRecovered, null);

                        break;

                    case P2PControlFrameType.Diagnostics:
                        this.ProcessDiagnosticsFrame(frame.Payload.Span);

                        break;

                    case P2PControlFrameType.Shutdown:
                        await this.DisconnectAsync(DisconnectReason.FatalError, cancellationToken).ConfigureAwait(false);

                        return;
                }
        }
        catch (OperationCanceledException)
        {
            // normal termination path
        }
        catch (Exception ex)
        {
            this.ChangeState(P2PConnectionState.Degraded, P2PConnectionTransitionCause.TransportError, ex.Message);
            _ = this.TriggerReconnectAsync("control channel failure", FailureSeverity.Hard, CancellationToken.None);
        }
    }

    private void RegisterRttSample(TimeSpan rtt)
    {
        var sample = Math.Max(rtt.TotalMilliseconds, 1.0);
        if (this._srtt <= 0)
        {
            this._srtt = sample;
            this._rttvar = sample / 2;
        }
        else
        {
            const double alpha = 1.0 / 8.0;
            const double beta = 1.0 / 4.0;
            this._rttvar = (1 - beta) * this._rttvar + beta * Math.Abs(this._srtt - sample);
            this._srtt = (1 - alpha) * this._srtt + alpha * sample;
        }
    }

    private void RegisterHeartbeatSample(TimeSpan interval)
    {
        var sample = Math.Max(interval.TotalMilliseconds, 1.0);
        lock (this._heartbeatStatsLock)
        {
            this._heartbeatSamples++;
            var delta = sample - this._heartbeatMean;
            this._heartbeatMean += delta / this._heartbeatSamples;
            this._heartbeatM2 += delta * (sample - this._heartbeatMean);

            var index = this._heartbeatWindowIndex++ % this._heartbeatWindow.Length;
            this._heartbeatWindow[index] = sample;
            if (this._heartbeatWindowCount < this._heartbeatWindow.Length)
                this._heartbeatWindowCount++;
        }
    }

    private TimeSpan ComputeHeartbeatInterval()
    {
        if (this._srtt <= 0)
            return this._options.HeartbeatInterval;

        var interval = Math.Clamp(this._srtt * 1.2, 250, this._options.HeartbeatInterval.TotalMilliseconds);
        var jitter = 0.1 * interval;
        var randomized = interval + (this._random.NextDouble() * 2 - 1) * jitter;

        return TimeSpan.FromMilliseconds(Math.Max(100, randomized));
    }

    private double ComputePhi(DateTimeOffset now)
    {
        if (this._lastHeartbeatAck == DateTimeOffset.MinValue)
            return 0;

        var elapsed = (now - this._lastHeartbeatAck).TotalMilliseconds;
        double meanInterval;
        double variance;
        long samples;
        lock (this._heartbeatStatsLock)
        {
            samples = this._heartbeatSamples;
            meanInterval = this._heartbeatMean;
            variance = this._heartbeatM2;
        }

        if (samples < this._options.PhiMinSamples)
        {
            var meanFallback = Math.Max(this._srtt, 1.0);

            return elapsed / meanFallback;
        }

        meanInterval = Math.Max(meanInterval, 1.0);
        variance = Math.Max(variance / (samples - 1), 1.0);
        var stdDev = Math.Sqrt(variance);
        var deviation = (elapsed - meanInterval) / stdDev;
        var cdf = 0.5 * (1.0 + Erf(deviation / Math.Sqrt(2.0)));
        var survival = Math.Max(1e-12, 1.0 - cdf);

        return -Math.Log10(survival);
    }

    private static double Erf(double x)
    {
        var sign = Math.Sign(x);
        x = Math.Abs(x);

        var a1 = 0.254829592;
        var a2 = -0.284496736;
        var a3 = 1.421413741;
        var a4 = -1.453152027;
        var a5 = 1.061405429;
        var p = 0.3275911;

        var t = 1.0 / (1.0 + p * x);
        var y = 1.0 - ((((a5 * t + a4) * t + a3) * t + a2) * t + a1) * t * Math.Exp(-x * x);

        return sign * y;
    }

    private void CheckHeartbeatTimeout(DateTimeOffset sentAt)
    {
        var delta = DateTimeOffset.UtcNow - this._lastHeartbeatAck;
        if (this._lastHeartbeatAck != DateTimeOffset.MinValue && delta > this._options.HeartbeatTimeout)
        {
            this.ScheduleWriteProbe("heartbeat timeout");
            this.ChangeState(P2PConnectionState.Degraded, P2PConnectionTransitionCause.HeartbeatMiss, $"Missed heartbeat for {delta}.");
            this.BroadcastCurrentEndpointPenalty(FailureSeverity.Soft, this._options.EndpointBlacklistDuration, false);
            _ = this.TriggerReconnectAsync("heartbeat timeout", FailureSeverity.Soft, CancellationToken.None);
        }
        else if (this._heartbeatSamples >= this._options.PhiMinSamples)
        {
            var phi = this.ComputePhi(DateTimeOffset.UtcNow);
            if (phi >= this._options.PhiFailureThreshold)
            {
                this.ScheduleWriteProbe("phi failure threshold exceeded");
                this.ChangeState(
                    P2PConnectionState.Degraded,
                    P2PConnectionTransitionCause.TransportError,
                    "phi failure threshold exceeded");
                this.BroadcastCurrentEndpointPenalty(FailureSeverity.Hard, this._options.EndpointBlacklistDuration, false);
                _ = this.TriggerReconnectAsync("phi failure threshold exceeded", FailureSeverity.Hard, CancellationToken.None);
            }
            else if (phi >= this._options.PhiSuspectThreshold)
            {
                this.ScheduleWriteProbe("phi threshold exceeded");
                this.BroadcastCurrentEndpointPenalty(FailureSeverity.Soft, this._options.EndpointBlacklistDuration, false);
                _ = this.TriggerReconnectAsync("phi threshold exceeded", FailureSeverity.Soft, CancellationToken.None);
            }
        }
    }

    private async Task TriggerReconnectAsync(string evidence, FailureSeverity severity, CancellationToken cancellationToken)
    {
        if (!await this._reconnectGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            return;

        var shouldRetry = false;
        try
        {
            this._lastFailureSeverity = severity;

            if (severity == FailureSeverity.Fatal)
            {
                this.ChangeState(P2PConnectionState.Failed, P2PConnectionTransitionCause.TransportError, evidence);

                return;
            }

            this.ChangeState(P2PConnectionState.Reconnecting, P2PConnectionTransitionCause.TransportError, evidence);
            this._consecutiveFailures++;

            var backoff = this.ComputeBackoff(this._consecutiveFailures, severity);
            if (backoff > TimeSpan.Zero)
                await Task.Delay(backoff, cancellationToken).ConfigureAwait(false);

            var connection = await this.DialAsync(cancellationToken).ConfigureAwait(false);
            if (connection is null)
            {
                this.ChangeState(P2PConnectionState.Failed, P2PConnectionTransitionCause.DialFailed, "all endpoints exhausted");

                return;
            }

            this.ChangeState(
                P2PConnectionState.Switching,
                P2PConnectionTransitionCause.SwitchStarted,
                connection.RemoteEndPoint?.ToString());
            var handshake = await this.PerformHandshakeAsync(connection.ControlChannel, cancellationToken).ConfigureAwait(false);
            await this.SwitchConnectionAsync(connection, handshake, cancellationToken, TokenSourceKind.Reconnect);
            this.ChangeState(
                P2PConnectionState.Established,
                P2PConnectionTransitionCause.SwitchCompleted,
                connection.RemoteEndPoint?.ToString());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            this.ChangeState(P2PConnectionState.Failed, P2PConnectionTransitionCause.TransportError, ex.Message);
            shouldRetry = !cancellationToken.IsCancellationRequested;
        }
        finally
        {
            this._reconnectGate.Release();
        }

        if (shouldRetry)
            _ = this.TriggerReconnectAsync("retry after failure", severity, CancellationToken.None);
    }

    private TimeSpan ComputeBackoff(int attempt, FailureSeverity severity)
    {
        if (severity == FailureSeverity.Hard)
        {
            var cap = this._options.HardFailureBackoffCap.TotalMilliseconds;
            var baseDelay = this._options.HardFailureInitialBackoff.TotalMilliseconds;
            var value = Math.Min(cap, baseDelay * Math.Pow(1.5, Math.Max(0, attempt - 1)));

            return TimeSpan.FromMilliseconds(Math.Max(0, value));
        }

        var softCap = this._options.SoftFailureBackoffCap.TotalMilliseconds;
        var softBase = this._options.SoftFailureInitialBackoff.TotalMilliseconds;
        var pow = Math.Min(softCap, softBase * Math.Pow(2, Math.Max(0, attempt - 1)));
        var jittered = pow / 2 + this._random.NextDouble() * pow / 2;

        return TimeSpan.FromMilliseconds(Math.Min(jittered, softCap));
    }

    private double GetHeartbeatPercentile(double percentile)
    {
        lock (this._heartbeatStatsLock)
        {
            var count = this._heartbeatWindowCount;

            if (count == 0)
                return 0;

            var buffer = new double[count];
            Array.Copy(this._heartbeatWindow, buffer, count);
            Array.Sort(buffer);

            var rank = percentile / 100.0 * (count - 1);
            var lower = (int)Math.Floor(rank);
            var upper = (int)Math.Ceiling(rank);

            if (lower == upper)
                return buffer[lower];

            var weight = rank - lower;

            return buffer[lower] + weight * (buffer[upper] - buffer[lower]);
        }
    }

    private void ScheduleWriteProbe(string reason)
    {
        var socket = this._transportConnection?.UnderlyingSocket;

        if (socket is null)
            return;

        if (Interlocked.CompareExchange(ref this._writeProbeInFlight, 1, 0) != 0)
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                await this.ProbeSocketAsync(socket).ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Exchange(ref this._writeProbeInFlight, 0);
            }
        });
    }

    private async Task ProbeSocketAsync(Socket socket)
    {
        try
        {
            if (!socket.Connected)
                throw new IOException("socket not connected");

            await socket.SendAsync(ReadOnlyMemory<byte>.Empty, SocketFlags.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is SocketException or IOException or ObjectDisposedException)
        {
            this.ChangeState(P2PConnectionState.Degraded, P2PConnectionTransitionCause.TransportError, ex.Message);
            this.BroadcastCurrentEndpointPenalty(FailureSeverity.Hard, this._options.EndpointBlacklistDuration, false);
            _ = this.TriggerReconnectAsync($"write probe failure: {ex.Message}", FailureSeverity.Hard, CancellationToken.None);
        }
    }

    private void ChangeState(P2PConnectionState newState, P2PConnectionTransitionCause cause, string? evidence)
    {
        P2PConnectionState previous;
        lock (this._stateLock)
        {
            if (this._state == P2PConnectionState.Disposed)
                return;

            previous = this._state;

            if (previous == newState)
                return;

            if (newState == P2PConnectionState.Established)
            {
                this._uptimeStart = DateTimeOffset.UtcNow;
                this._totalDowntime = TimeSpan.Zero;
            }
            else if (newState is P2PConnectionState.Disconnected or P2PConnectionState.Failed)
            {
                this._totalDowntime += DateTimeOffset.UtcNow - this._uptimeStart;
            }

            this._state = newState;
        }

        this.ConnectionStateChanged?.Invoke(this, new(previous, newState, cause, evidence));
    }

    private P2PTransportEndpoint SelectEndpoint(EndPoint? endPoint)
    {
        if (endPoint is null)
            return this._options.Endpoints[0];

        if (endPoint is IPEndPoint ip)
        {
            var match = this._options.Endpoints.FirstOrDefault(e =>
                string.Equals(e.Host, ip.Address.ToString(), StringComparison.OrdinalIgnoreCase) && e.Port == ip.Port);

            return match ?? new P2PTransportEndpoint(ip.Address.ToString(), ip.Port);
        }

        if (endPoint is DnsEndPoint dns)
        {
            var match = this._options.Endpoints.FirstOrDefault(e =>
                string.Equals(e.Host, dns.Host, StringComparison.OrdinalIgnoreCase) && e.Port == dns.Port);

            return match ?? new P2PTransportEndpoint(dns.Host, dns.Port);
        }

        return this._options.Endpoints[0];
    }

    private void BroadcastCurrentEndpointPenalty(FailureSeverity severity, TimeSpan duration, bool fromRetryAfter)
    {
        var endpoint = this._currentHandle?.Endpoint;

        if (endpoint is null)
            return;

        this.BroadcastEndpointPenalty(GetEndpointKey(endpoint), severity, duration, fromRetryAfter);
    }

    private void BroadcastEndpointPenalty(string endpointKey, FailureSeverity severity, TimeSpan duration, bool fromRetryAfter)
    {
        var updated = SharedBlacklistRegistry.SetPenalty(endpointKey, severity, duration, this._instanceId, fromRetryAfter);

        var shouldPropagate = fromRetryAfter || this._options.Endpoints.Count > 1;

        if (!shouldPropagate || !updated)
            return;

        var handle = this._currentHandle;

        if (handle is null)
            return;

        var now = DateTimeOffset.UtcNow;

        if (this._lastBroadcastTimes.TryGetValue(endpointKey, out var last) && now - last < TimeSpan.FromMilliseconds(200))
            return;

        this._lastBroadcastTimes[endpointKey] = now;

        var ttlMs = (int)Math.Clamp(duration.TotalMilliseconds, 50, int.MaxValue);
        var keyBytes = Encoding.UTF8.GetBytes(endpointKey);

        if (keyBytes.Length > ushort.MaxValue)
            return;

        var payloadLength = 8 + keyBytes.Length;
        var buffer = new byte[payloadLength];
        buffer[0] = 0xB1; // diagnostics binary message v1
        buffer[1] = (byte)(((int)severity & 0x03) | (fromRetryAfter ? 0x04 : 0x00));
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(2, 4), ttlMs);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(6, 2), (ushort)keyBytes.Length);
        keyBytes.CopyTo(buffer.AsSpan(8));

        var frame = new P2PControlFrame(
            P2PControlFrameType.Diagnostics,
            DateTimeOffset.UtcNow.UtcTicks,
            Interlocked.Increment(ref this._controlSequence),
            buffer);

        _ = SendDiagnosticsAsync(handle.ControlChannel, frame);
    }

    private void ThrowIfDisposed()
    {
        if (this._disposed)
            throw new ObjectDisposedException(nameof(P2PConnectionManager));
    }

    private void OnNetworkAvailabilityLost(object? sender, EventArgs e) => this.ScheduleWriteProbe("network availability lost");

    private void OnNetworkAddressChanged(object? sender, EventArgs e) => this.ScheduleWriteProbe("network address changed");

    private void OnSharedBlacklistUpdate(SharedBlacklistRegistry.SharedBlacklistNotification notification)
    {
        if (this._disposed)
            return;

        if (notification.SourceId == this._instanceId)
            return;

        var now = DateTimeOffset.UtcNow;
        var remaining = notification.ExpiresAt - now;
        if (remaining <= TimeSpan.Zero)
            remaining = TimeSpan.FromMilliseconds(1);

        var tracker = this._dialTrackers.GetOrAdd(notification.Key, _ => new());
        tracker.RecordFailure(now, remaining);

        if (notification.Severity > this._lastFailureSeverity)
            this._lastFailureSeverity = notification.Severity;
    }

    private static string GetEndpointKey(P2PTransportEndpoint endpoint) =>
        $"{endpoint.Transport}:{endpoint.Host}:{endpoint.Port}";

    private static async Task DisposeTransportAsync(P2PTransportConnection? transport, P2PConnectionHandle? handle)
    {
        if (transport is null)
            return;

        try
        {
            await transport.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
            // ignore disposal errors to avoid masking the original issue
        }
    }

    private static FailureSeverity ClassifyFailure(Exception exception)
    {
        if (exception is P2PHandshakeException handshake)
            return handshake.Severity;

        return exception switch
        {
            RetryAfterException => FailureSeverity.Soft,
            TimeoutException => FailureSeverity.Hard,
            SocketException => FailureSeverity.Hard,
            IOException => FailureSeverity.Hard,
            ObjectDisposedException => FailureSeverity.Hard,
            _ => FailureSeverity.Soft
        };
    }

    private static async Task SendDiagnosticsAsync(IP2PControlChannel controlChannel, P2PControlFrame frame)
    {
        try
        {
            await controlChannel.SendAsync(frame, CancellationToken.None).ConfigureAwait(false);
            await controlChannel.FlushAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // ignore send errors during diagnostics broadcast
        }
    }

    private void ProcessDiagnosticsFrame(ReadOnlySpan<byte> payload)
    {
        if (payload.IsEmpty)
            return;

        if (payload[0] == 0xB1)
        {
            if (payload.Length < 8)
                return;

            var flags = payload[1];
            var severity = (FailureSeverity)(flags & 0x03);
            var fromRetryAfter = (flags & 0x04) != 0;
            var ttlMs = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(2, 4));
            var keyLength = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(6, 2));

            if (keyLength == 0 || payload.Length < 8 + keyLength)
                return;

            var key = Encoding.UTF8.GetString(payload.Slice(8, keyLength));
            var tracker = this._dialTrackers.GetOrAdd(key, _ => new());
            var duration = TimeSpan.FromMilliseconds(Math.Max(0, ttlMs));
            tracker.RecordFailure(DateTimeOffset.UtcNow, duration);
            SharedBlacklistRegistry.SetPenalty(key, severity, duration, Guid.Empty, fromRetryAfter);
            this._lastFailureSeverity = severity;

            return;
        }

        // legacy JSON diagnostics: ignore
    }

    private sealed class EndpointDialTracker
    {
        private readonly ConcurrentQueue<DateTimeOffset> _attempts = new();
        private readonly object _sync = new();
        private DateTimeOffset _blacklistedUntil;

        public bool TryStart(DateTimeOffset now, int maxPerMinute, TimeSpan blacklistDuration)
        {
            lock (this._sync)
            {
                this.Trim(now);

                if (this._blacklistedUntil > now)
                    return false;

                if (maxPerMinute > 0 && this.CountAttempts(now) >= maxPerMinute)
                {
                    this._blacklistedUntil = now + blacklistDuration;

                    return false;
                }

                this._attempts.Enqueue(now);

                return true;
            }
        }

        public void RecordSuccess()
        {
            lock (this._sync)
            {
                this._blacklistedUntil = DateTimeOffset.MinValue;
            }
        }

        public void RecordFailure(DateTimeOffset now, TimeSpan blacklistDuration)
        {
            lock (this._sync)
            {
                this._blacklistedUntil = now + blacklistDuration;
            }
        }

        private void Trim(DateTimeOffset now)
        {
            while (this._attempts.TryPeek(out var ts) && now - ts > TimeSpan.FromMinutes(1))
                this._attempts.TryDequeue(out _);
        }

        private int CountAttempts(DateTimeOffset now)
        {
            this.Trim(now);

            return this._attempts.Count;
        }

        public EndpointDialSnapshot GetSnapshot(DateTimeOffset now)
        {
            lock (this._sync)
            {
                this.Trim(now);

                return new(this._attempts.Count, this._blacklistedUntil);
            }
        }

        public TimeSpan? GetBlacklistDelay(DateTimeOffset now)
        {
            lock (this._sync)
            {
                this.Trim(now);

                if (this._blacklistedUntil <= now)
                    return null;

                return this._blacklistedUntil - now;
            }
        }
    }

    private readonly record struct EndpointDialSnapshot(int AttemptsLastMinute, DateTimeOffset BlacklistedUntil);

    private enum TokenSourceKind
    {
        InitialDial,
        Reconnect,
        Manual
    }

    private sealed record HandshakeResult(
        string RemoteNodeId,
        string RemoteSessionId,
        long RemoteEpoch,
        string? ResumeToken,
        bool ResumeAccepted);
}

file sealed class FaultAwareStream : Stream
{
    private readonly Action? _activityCallback;
    private readonly Func<Exception, Task> _faultCallback;
    private readonly Stream _inner;
    private int _faultSignaled;

    public FaultAwareStream(Stream inner, Func<Exception, Task> faultCallback, Action? activityCallback = null)
    {
        this._inner = inner ?? throw new ArgumentNullException(nameof(inner));
        this._faultCallback = faultCallback ?? throw new ArgumentNullException(nameof(faultCallback));
        this._activityCallback = activityCallback;
    }

    public override bool CanRead => this._inner.CanRead;
    public override bool CanSeek => this._inner.CanSeek;
    public override bool CanWrite => this._inner.CanWrite;
    public override long Length => this._inner.Length;

    public override long Position
    {
        get => this._inner.Position;
        set => this._inner.Position = value;
    }

    public override void Flush()
    {
        try
        {
            this._inner.Flush();
            this._activityCallback?.Invoke();
        }
        catch (Exception ex) when (this.HandleFault(ex))
        {
            throw;
        }
    }

    public async override Task FlushAsync(CancellationToken cancellationToken)
    {
        try
        {
            await this._inner.FlushAsync(cancellationToken).ConfigureAwait(false);
            this._activityCallback?.Invoke();
        }
        catch (Exception ex) when (this.HandleFault(ex))
        {
            throw;
        }
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        try
        {
            var read = this._inner.Read(buffer, offset, count);
            if (read > 0)
                this._activityCallback?.Invoke();

            return read;
        }
        catch (Exception ex) when (this.HandleFault(ex))
        {
            throw;
        }
    }

    public override int Read(Span<byte> buffer)
    {
        try
        {
            var read = this._inner.Read(buffer);
            if (read > 0)
                this._activityCallback?.Invoke();

            return read;
        }
        catch (Exception ex) when (this.HandleFault(ex))
        {
            throw;
        }
    }

    public async override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        try
        {
            var read = await this._inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read > 0)
                this._activityCallback?.Invoke();

            return read;
        }
        catch (Exception ex) when (this.HandleFault(ex))
        {
            throw;
        }
    }

    public async override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        try
        {
            var read = await this._inner.ReadAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
            if (read > 0)
                this._activityCallback?.Invoke();

            return read;
        }
        catch (Exception ex) when (this.HandleFault(ex))
        {
            throw;
        }
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        try
        {
            this._inner.Write(buffer, offset, count);
            this._activityCallback?.Invoke();
        }
        catch (Exception ex) when (this.HandleFault(ex))
        {
            throw;
        }
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        try
        {
            this._inner.Write(buffer);
            this._activityCallback?.Invoke();
        }
        catch (Exception ex) when (this.HandleFault(ex))
        {
            throw;
        }
    }

    public async override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        try
        {
            await this._inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
            this._activityCallback?.Invoke();
        }
        catch (Exception ex) when (this.HandleFault(ex))
        {
            throw;
        }
    }

    public async override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        try
        {
            await this._inner.WriteAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
            this._activityCallback?.Invoke();
        }
        catch (Exception ex) when (this.HandleFault(ex))
        {
            throw;
        }
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        try
        {
            return this._inner.Seek(offset, origin);
        }
        catch (Exception ex) when (this.HandleFault(ex))
        {
            throw;
        }
    }

    public override void SetLength(long value)
    {
        try
        {
            this._inner.SetLength(value);
        }
        catch (Exception ex) when (this.HandleFault(ex))
        {
            throw;
        }
    }

    protected override void Dispose(bool disposing)
    {
        // Do not dispose inner stream — P2PTransportConnection owns the underlying resources.
    }

    public override ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private bool HandleFault(Exception exception)
    {
        if (exception is IOException or ObjectDisposedException)
        {
            if (Interlocked.Exchange(ref this._faultSignaled, 1) == 0)
                _ = this._faultCallback(exception);

            return true;
        }

        return false;
    }
}

file static class TaskExtensions
{
    public static async Task IgnoreExceptions(this Task? task)
    {
        if (task is null)
            return;

        try
        {
            await task.ConfigureAwait(false);
        }
        catch
        {
            // suppress task cancellation exceptions when stopping background loops
        }
    }
}

file readonly struct ValueStopwatch
{
    private static readonly double TickFrequency = (double)TimeSpan.TicksPerSecond / Stopwatch.Frequency;
    private readonly long _startTimestamp;

    private ValueStopwatch(long startTimestamp) => this._startTimestamp = startTimestamp;

    public static ValueStopwatch StartNew() => new(Stopwatch.GetTimestamp());

    public TimeSpan Elapsed
    {
        get
        {
            var delta = Stopwatch.GetTimestamp() - this._startTimestamp;

            return TimeSpan.FromTicks((long)(delta * TickFrequency));
        }
    }
}