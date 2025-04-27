// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Networking.Core;
using Itexoft.Networking.P2P;

namespace Itexoft.Networking.Reliable;

/// <summary>
/// Snapshot of telemetry collected by <see cref="ReliableTcpClient" />.
/// </summary>
public sealed class ReliableConnectionMetrics
{
    internal ReliableConnectionMetrics(P2PConnectionMetrics metrics)
    {
        this.SrttMilliseconds = metrics.SrttMilliseconds;
        this.RttVarianceMilliseconds = metrics.RttVarianceMilliseconds;
        this.Phi = metrics.Phi;
        this.ConsecutiveFailures = metrics.ConsecutiveFailures;
        this.ReconnectSuccessRatio = metrics.ReconnectSuccessRatio;
        this.ResumeSuccessRatio = metrics.ResumeSuccessRatio;
        this.Uptime = metrics.Uptime;
        this.Downtime = metrics.Downtime;
        this.LastSwitchDuration = metrics.LastSwitchDuration;
        this.LastHeartbeatSentUtc = metrics.LastHeartbeatSentUtc;
        this.LastHeartbeatAckUtc = metrics.LastHeartbeatAckUtc;
        this.LastEndpointLabel = metrics.LastEndpointLabel;
        this.HeartbeatP50Milliseconds = metrics.HeartbeatP50Milliseconds;
        this.HeartbeatP95Milliseconds = metrics.HeartbeatP95Milliseconds;
        this.LastDataActivityUtc = metrics.LastDataActivityUtc;
        this.TotalDialAttemptsLastMinute = metrics.TotalDialAttemptsLastMinute;
        this.BlacklistedEndpointCount = metrics.BlacklistedEndpointCount;
        this.LastFailureSeverity = metrics.LastFailureSeverity;

        var dialRates = new List<ReliableDialRateMetric>(metrics.DialRates.Count);
        foreach (var rate in metrics.DialRates)
            dialRates.Add(new(rate.EndpointKey, rate.AttemptsLastMinute, rate.BlacklistedUntilUtc));

        this.DialRates = dialRates;
    }

    /// <summary>
    /// Smoothed round-trip time estimate in milliseconds.
    /// </summary>
    public double SrttMilliseconds { get; }

    /// <summary>
    /// Variance of the RTT estimator.
    /// </summary>
    public double RttVarianceMilliseconds { get; }

    /// <summary>
    /// Current φ value produced by the accrual detector.
    /// </summary>
    public double Phi { get; }

    /// <summary>
    /// Number of consecutive failures since the last successful reconnect.
    /// </summary>
    public int ConsecutiveFailures { get; }

    /// <summary>
    /// Rolling ratio of successful reconnect attempts.
    /// </summary>
    public double ReconnectSuccessRatio { get; }

    /// <summary>
    /// Rolling ratio of successful session resumes.
    /// </summary>
    public double ResumeSuccessRatio { get; }

    /// <summary>
    /// Total time the connection has been established.
    /// </summary>
    public TimeSpan Uptime { get; }

    /// <summary>
    /// Total time spent disconnected.
    /// </summary>
    public TimeSpan Downtime { get; }

    /// <summary>
    /// Duration of the last successful switch (make-before-break).
    /// </summary>
    public TimeSpan LastSwitchDuration { get; }

    /// <summary>
    /// Timestamp of the last heartbeat sent by the client.
    /// </summary>
    public DateTimeOffset LastHeartbeatSentUtc { get; }

    /// <summary>
    /// Timestamp of the last heartbeat acknowledged by the server.
    /// </summary>
    public DateTimeOffset LastHeartbeatAckUtc { get; }

    /// <summary>
    /// Label of the endpoint currently in use.
    /// </summary>
    public string? LastEndpointLabel { get; }

    /// <summary>
    /// Median heartbeat latency in milliseconds.
    /// </summary>
    public double HeartbeatP50Milliseconds { get; }

    /// <summary>
    /// 95th percentile heartbeat latency in milliseconds.
    /// </summary>
    public double HeartbeatP95Milliseconds { get; }

    /// <summary>
    /// Timestamp of the last observed application payload.
    /// </summary>
    public DateTimeOffset LastDataActivityUtc { get; }

    /// <summary>
    /// Total number of dial attempts in the last rolling minute across all endpoints.
    /// </summary>
    public int TotalDialAttemptsLastMinute { get; }

    /// <summary>
    /// Number of endpoints currently blacklisted.
    /// </summary>
    public int BlacklistedEndpointCount { get; }

    /// <summary>
    /// Severity of the last recorded failure.
    /// </summary>
    public FailureSeverity LastFailureSeverity { get; }

    /// <summary>
    /// Endpoint-level dial statistics.
    /// </summary>
    public IReadOnlyList<ReliableDialRateMetric> DialRates { get; }
}

public readonly struct ReliableDialRateMetric
{
    public ReliableDialRateMetric(string endpointKey, int attemptsLastMinute, DateTimeOffset blacklistedUntilUtc)
    {
        this.EndpointKey = endpointKey;
        this.AttemptsLastMinute = attemptsLastMinute;
        this.BlacklistedUntilUtc = blacklistedUntilUtc;
    }

    /// <summary>
    /// Endpoint identifier in the format transport:host:port.
    /// </summary>
    public string EndpointKey { get; }

    /// <summary>
    /// Number of dial attempts recorded within the last rolling minute.
    /// </summary>
    public int AttemptsLastMinute { get; }

    /// <summary>
    /// Expiration timestamp of the current blacklist penalty.
    /// </summary>
    public DateTimeOffset BlacklistedUntilUtc { get; }
}