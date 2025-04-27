// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Networking.P2P;

namespace Itexoft.Networking.Reliable;

/// <summary>
/// Configuration for <see cref="ReliableTcpClient" />.
/// </summary>
public sealed class ReliableTcpClientOptions
{
    public static readonly TimeSpan DefaultHeartbeatInterval = P2PConnectionOptions.DefaultHeartbeatInterval;
    public static readonly TimeSpan DefaultHeartbeatTimeout = P2PConnectionOptions.DefaultHeartbeatTimeout;
    public static readonly TimeSpan DefaultConnectTimeout = P2PConnectionOptions.DefaultConnectTimeout;

    public ReliableTcpClientOptions(string clientId, IReadOnlyList<ReliableTcpEndpoint> endpoints)
    {
        this.ClientId = string.IsNullOrWhiteSpace(clientId)
            ? throw new ArgumentException("Client id must be specified.", nameof(clientId))
            : clientId;

        if (endpoints is null || endpoints.Count == 0)
            throw new ArgumentException("At least one endpoint must be provided.", nameof(endpoints));

        this.Endpoints = endpoints;
    }

    /// <summary>
    /// Identifier that will be sent during the handshake; defaults to machine name if not specified.
    /// </summary>
    public string ClientId { get; }

    /// <summary>
    /// List of endpoints used during hedged dial attempts.
    /// </summary>
    public IReadOnlyList<ReliableTcpEndpoint> Endpoints { get; }

    /// <summary>
    /// Maximum time allowed for establishing the first successful connection attempt.
    /// </summary>
    public TimeSpan ConnectTimeout { get; init; } = DefaultConnectTimeout;

    /// <summary>
    /// Interval between heartbeat pings when no piggyback opportunities exist.
    /// </summary>
    public TimeSpan HeartbeatInterval { get; init; } = DefaultHeartbeatInterval;

    /// <summary>
    /// Duration without a heartbeat response before the link is considered degraded.
    /// </summary>
    public TimeSpan HeartbeatTimeout { get; init; } = DefaultHeartbeatTimeout;

    /// <summary>
    /// Delay between hedged dial attempts (Happy-Eyeballs).
    /// </summary>
    public TimeSpan? HappyEyeballsDelay { get; init; }

    /// <summary>
    /// Additional delay applied between endpoint groups with different priorities.
    /// </summary>
    public TimeSpan? HappyEyeballsGroupDelay { get; init; }

    /// <summary>
    /// Enables subscription to OS network change notifications.
    /// </summary>
    public bool EnableNetworkMonitoring { get; init; } = true;

    /// <summary>
    /// Enables session resumption when the server supports it.
    /// </summary>
    public bool EnableSessionResume { get; init; } = true;

    /// <summary>
    /// Optional token supplied by the application to resume previous sessions.
    /// </summary>
    public string? ResumeToken { get; init; }

    /// <summary>
    /// Duration for which an endpoint is blacklisted after a failure.
    /// </summary>
    public TimeSpan EndpointBlacklistDuration { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Maximum number of concurrent dial attempts.
    /// </summary>
    public int MaxConcurrentDials { get; init; } = 2;

    /// <summary>
    /// Maximum allowed dial attempts per endpoint per minute.
    /// </summary>
    public int MaxDialsPerMinutePerEndpoint { get; init; } = 20;

    /// <summary>
    /// Number of tokens in the retry budget.
    /// </summary>
    public int RetryBudgetCapacity { get; init; } = 20;

    /// <summary>
    /// Duration of the retry budget window.
    /// </summary>
    public TimeSpan RetryBudgetWindow { get; init; } = P2PConnectionOptions.DefaultRetryBudgetWindow;

    /// <summary>
    /// Idle time after which an explicit heartbeat is emitted.
    /// </summary>
    public TimeSpan HeartbeatPiggybackGrace { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Minimum number of heartbeat samples before φ-accrual is enabled.
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
    /// Initial backoff used for soft failures.
    /// </summary>
    public TimeSpan SoftFailureInitialBackoff { get; init; } = P2PConnectionOptions.DefaultSoftFailureInitialBackoff;

    /// <summary>
    /// Maximum backoff used for soft failures.
    /// </summary>
    public TimeSpan SoftFailureBackoffCap { get; init; } = P2PConnectionOptions.DefaultSoftFailureBackoffCap;

    /// <summary>
    /// Initial backoff used for hard failures.
    /// </summary>
    public TimeSpan HardFailureInitialBackoff { get; init; } = P2PConnectionOptions.DefaultHardFailureInitialBackoff;

    /// <summary>
    /// Maximum backoff used for hard failures.
    /// </summary>
    public TimeSpan HardFailureBackoffCap { get; init; } = P2PConnectionOptions.DefaultHardFailureBackoffCap;

    /// <summary>
    /// Optional metadata propagated during the handshake.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }

    internal P2PConnectionOptions ToP2POptions()
    {
        var converted = new List<P2PTransportEndpoint>(this.Endpoints.Count);
        foreach (var endpoint in this.Endpoints)
            converted.Add(endpoint.ToP2PEndpoint());

        var options = new P2PConnectionOptions(
            this.ClientId,
            converted,
            this.ConnectTimeout,
            this.HappyEyeballsDelay ?? P2PConnectionOptions.DefaultHappyEyeballsDelay,
            this.HappyEyeballsGroupDelay ?? P2PConnectionOptions.DefaultHappyEyeballsGroupDelay,
            this.HeartbeatInterval,
            this.HeartbeatTimeout,
            this.MaxConcurrentDials)
        {
            EnableNetworkMonitoring = this.EnableNetworkMonitoring,
            EnableSessionResume = this.EnableSessionResume,
            ResumeToken = this.ResumeToken,
            EndpointBlacklistDuration = this.EndpointBlacklistDuration,
            MaxDialsPerMinutePerEndpoint = this.MaxDialsPerMinutePerEndpoint,
            RetryBudgetCapacity = this.RetryBudgetCapacity,
            RetryBudgetWindow = this.RetryBudgetWindow,
            HeartbeatPiggybackGrace = this.HeartbeatPiggybackGrace,
            PhiMinSamples = this.PhiMinSamples,
            PhiSuspectThreshold = this.PhiSuspectThreshold,
            PhiFailureThreshold = this.PhiFailureThreshold,
            SoftFailureInitialBackoff = this.SoftFailureInitialBackoff,
            SoftFailureBackoffCap = this.SoftFailureBackoffCap,
            HardFailureInitialBackoff = this.HardFailureInitialBackoff,
            HardFailureBackoffCap = this.HardFailureBackoffCap,
            Metadata = this.Metadata
        };

        return options;
    }
}