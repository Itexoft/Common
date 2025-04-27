// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Net.NetworkInformation;

namespace Itexoft.Networking.P2P;

/// <summary>
/// Configuration for establishing and supervising a peer-to-peer connection.
/// </summary>
public sealed class P2PConnectionOptions
{
    public static readonly TimeSpan DefaultHappyEyeballsDelay = TimeSpan.FromMilliseconds(200);
    public static readonly TimeSpan DefaultHappyEyeballsGroupDelay = TimeSpan.FromMilliseconds(150);
    public static readonly TimeSpan DefaultHeartbeatInterval = TimeSpan.FromMilliseconds(750);
    public static readonly TimeSpan DefaultHeartbeatTimeout = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan DefaultConnectTimeout = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan DefaultSoftFailureInitialBackoff = TimeSpan.FromMilliseconds(500);
    public static readonly TimeSpan DefaultSoftFailureBackoffCap = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan DefaultHardFailureInitialBackoff = TimeSpan.FromMilliseconds(150);
    public static readonly TimeSpan DefaultHardFailureBackoffCap = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan DefaultRetryBudgetWindow = TimeSpan.FromSeconds(60);

    public P2PConnectionOptions(
        string nodeId,
        IReadOnlyList<P2PTransportEndpoint> endpoints,
        TimeSpan? connectTimeout = null,
        TimeSpan? happyEyeballsDelay = null,
        TimeSpan? happyEyeballsGroupDelay = null,
        TimeSpan? heartbeatInterval = null,
        TimeSpan? heartbeatTimeout = null,
        int maxConcurrentDials = 2)
    {
        this.NodeId = string.IsNullOrWhiteSpace(nodeId)
            ? throw new ArgumentException("NodeId is required", nameof(nodeId))
            : nodeId;

        if (endpoints is null || endpoints.Count == 0)
            throw new ArgumentException("At least one endpoint must be specified", nameof(endpoints));

        this.Endpoints = endpoints;

        this.ConnectTimeout = connectTimeout is { Ticks: > 0 } ? connectTimeout.Value : DefaultConnectTimeout;
        this.HappyEyeballsDelay = happyEyeballsDelay is { Ticks: > 0 } ? happyEyeballsDelay.Value : DefaultHappyEyeballsDelay;
        this.HappyEyeballsGroupDelay =
            happyEyeballsGroupDelay is { Ticks: > 0 } ? happyEyeballsGroupDelay.Value : DefaultHappyEyeballsGroupDelay;
        this.HeartbeatInterval = heartbeatInterval is { Ticks: > 0 } ? heartbeatInterval.Value : DefaultHeartbeatInterval;
        this.HeartbeatTimeout = heartbeatTimeout is { Ticks: > 0 } ? heartbeatTimeout.Value : DefaultHeartbeatTimeout;

        if (maxConcurrentDials <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxConcurrentDials));

        this.MaxConcurrentDials = maxConcurrentDials;
    }

    /// <summary>
    /// Identifier of the current node; transmitted during handshake for diagnostics and tie-breaking.
    /// </summary>
    public string NodeId { get; }

    /// <summary>
    /// Ordered list of candidate endpoints.
    /// </summary>
    public IReadOnlyList<P2PTransportEndpoint> Endpoints { get; }

    /// <summary>
    /// Maximum time allowed for the first successful dial.
    /// </summary>
    public TimeSpan ConnectTimeout { get; }

    /// <summary>
    /// Delay between launching the next dial attempt in the Happy-Eyeballs hedged dialing routine.
    /// </summary>
    public TimeSpan HappyEyeballsDelay { get; }

    /// <summary>
    /// Additional delay applied between endpoint groups that share the same priority bucket.
    /// </summary>
    public TimeSpan HappyEyeballsGroupDelay { get; }

    /// <summary>
    /// Interval between heartbeats when no piggyback opportunities exist.
    /// </summary>
    public TimeSpan HeartbeatInterval { get; }

    /// <summary>
    /// Duration without response after which a heartbeat failure is suspected.
    /// </summary>
    public TimeSpan HeartbeatTimeout { get; }

    /// <summary>
    /// Maximum number of concurrent dial attempts.
    /// </summary>
    public int MaxConcurrentDials { get; }

    /// <summary>
    /// Optional resume token used for session resumption across transports.
    /// </summary>
    public string? ResumeToken { get; init; }

    /// <summary>
    /// Optional application-defined metadata propagated via handshake.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }

    /// <summary>
    /// Enables subscription to <see cref="NetworkChange" /> notifications.
    /// </summary>
    public bool EnableNetworkMonitoring { get; init; } = true;

    /// <summary>
    /// Custom network event source; when <c>null</c> the built-in <see cref="NetworkChange" /> is used.
    /// </summary>
    public IP2PNetworkEventSource? NetworkEventSource { get; init; }

    /// <summary>
    /// Minimum number of heartbeat samples required before φ-accrual becomes active.
    /// </summary>
    public int PhiMinSamples { get; init; } = 8;

    /// <summary>
    /// φ threshold that marks the connection as suspect.
    /// </summary>
    public double PhiSuspectThreshold { get; init; } = 5.0;

    /// <summary>
    /// φ threshold that marks the connection as failed.
    /// </summary>
    public double PhiFailureThreshold { get; init; } = 8.0;

    /// <summary>
    /// Idle time after which a dedicated heartbeat is sent when no payload traffic exists.
    /// </summary>
    public TimeSpan HeartbeatPiggybackGrace { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Maximum number of dial attempts per endpoint per minute.
    /// </summary>
    public int MaxDialsPerMinutePerEndpoint { get; init; } = 30;

    /// <summary>
    /// Duration for which an endpoint remains blacklisted after a failure.
    /// </summary>
    public TimeSpan EndpointBlacklistDuration { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Enables session resumption when a resume token is supplied.
    /// </summary>
    public bool EnableSessionResume { get; init; } = true;

    /// <summary>
    /// Initial backoff applied for soft failures.
    /// </summary>
    public TimeSpan SoftFailureInitialBackoff { get; init; } = DefaultSoftFailureInitialBackoff;

    /// <summary>
    /// Maximum backoff applied for soft failures.
    /// </summary>
    public TimeSpan SoftFailureBackoffCap { get; init; } = DefaultSoftFailureBackoffCap;

    /// <summary>
    /// Initial backoff applied for hard failures.
    /// </summary>
    public TimeSpan HardFailureInitialBackoff { get; init; } = DefaultHardFailureInitialBackoff;

    /// <summary>
    /// Maximum backoff applied for hard failures.
    /// </summary>
    public TimeSpan HardFailureBackoffCap { get; init; } = DefaultHardFailureBackoffCap;

    /// <summary>
    /// Maximum number of dial attempts allowed within the retry budget window.
    /// </summary>
    public int RetryBudgetCapacity { get; init; } = 20;

    /// <summary>
    /// Sliding window duration used by the retry budget.
    /// </summary>
    public TimeSpan RetryBudgetWindow { get; init; } = DefaultRetryBudgetWindow;
}