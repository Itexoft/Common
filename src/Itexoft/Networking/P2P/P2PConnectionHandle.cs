// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Networking.Core;

namespace Itexoft.Networking.P2P;

/// <summary>
/// Represents the active channel to the remote peer.
/// </summary>
public sealed class P2PConnectionHandle(
    Stream dataStream,
    IP2PControlChannel controlChannel,
    string remoteNodeId,
    string remoteSessionId,
    string localSessionId,
    long sessionEpoch,
    P2PTransportEndpoint endpoint)
{
    public Stream DataStream { get; } = dataStream ?? throw new ArgumentNullException(nameof(dataStream));

    public IP2PControlChannel ControlChannel { get; } = controlChannel ?? throw new ArgumentNullException(nameof(controlChannel));

    public string RemoteNodeId { get; } = remoteNodeId ?? throw new ArgumentNullException(nameof(remoteNodeId));

    public string RemoteSessionId { get; } = remoteSessionId ?? throw new ArgumentNullException(nameof(remoteSessionId));

    public string LocalSessionId { get; } = localSessionId ?? throw new ArgumentNullException(nameof(localSessionId));

    public long SessionEpoch { get; } = sessionEpoch;

    public P2PTransportEndpoint Endpoint { get; } = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
}

/// <summary>
/// Snapshot of metrics collected by the P2P connection manager.
/// </summary>
public sealed class P2PConnectionMetrics
{
    public double SrttMilliseconds { get; init; }
    public double RttVarianceMilliseconds { get; init; }
    public double Phi { get; init; }
    public int ConsecutiveFailures { get; init; }
    public double ReconnectSuccessRatio { get; init; }
    public double ResumeSuccessRatio { get; init; }
    public TimeSpan Uptime { get; init; }
    public TimeSpan Downtime { get; init; }
    public TimeSpan LastSwitchDuration { get; init; }
    public DateTimeOffset LastHeartbeatSentUtc { get; init; }
    public DateTimeOffset LastHeartbeatAckUtc { get; init; }
    public string? LastEndpointLabel { get; init; }
    public double HeartbeatP50Milliseconds { get; init; }
    public double HeartbeatP95Milliseconds { get; init; }
    public DateTimeOffset LastDataActivityUtc { get; init; }
    public int TotalDialAttemptsLastMinute { get; init; }
    public int BlacklistedEndpointCount { get; init; }
    public IReadOnlyList<P2PDialRateMetric> DialRates { get; init; } = [];
    public FailureSeverity LastFailureSeverity { get; init; }
}

public sealed class P2PDialRateMetric
{
    public string EndpointKey { get; init; } = string.Empty;
    public int AttemptsLastMinute { get; init; }
    public DateTimeOffset BlacklistedUntilUtc { get; init; }
}