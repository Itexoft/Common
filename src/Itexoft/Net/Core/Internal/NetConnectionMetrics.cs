// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Net.Core.Internal;

internal class NetConnectionMetrics : INetConnectionMetrics
{
    public NetConnectionMetrics() { }

    public NetConnectionState State { get; init; }
    public TimeSpan Uptime { get; init; }
    public TimeSpan Downtime { get; init; }
    public NetFailureSeverity LastFailureSeverity { get; init; }
    public int TotalDialAttemptsLastMinute { get; init; }
    public string? LastEndpointLabel { get; init; }
    public DateTimeOffset LastDataActivityUtc { get; init; }
    public required IReadOnlyList<NetDialRateMetric> DialRates { get; init; }
}
