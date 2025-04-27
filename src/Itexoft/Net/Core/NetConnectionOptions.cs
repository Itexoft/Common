// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Core;
using Itexoft.Extensions;
using Itexoft.Net.Dns;

namespace Itexoft.Net.Core;

public class NetConnectionOptions
{
    private static readonly NetTransportRegistry registry = new((NetTransports.Tcp, new NetTcpConnector()));

    protected static readonly TimeSpan DefaultConnectTimeout = TimeSpan.FromSeconds(10);
    protected static readonly TimeSpan DefaultRetryBudgetWindow = TimeSpan.FromSeconds(60);
    protected static readonly TimeSpan DefaultSoftFailureInitialBackoff = TimeSpan.FromMilliseconds(500);

    protected NetConnectionOptions(INetConnector connector, IReadOnlyList<NetEndpoint> endpoints)
    {
        this.Connector = connector.Required();
        this.Endpoints = endpoints.Required();

        if (endpoints.Count == 0)
            throw new ArgumentException("At least one endpoint must be specified", nameof(endpoints));
    }

    public NetConnectionOptions(LString connector, params NetEndpoint[] endpoints) : this(registry.Resolve(connector), endpoints) { }

    public TimeSpan ConnectTimeout { get; init; } = DefaultConnectTimeout;

    public INetEventSource? NetworkEventSource { get; init; }

    public int RetryBudgetCapacity { get; init; } = 20;

    public TimeSpan RetryBudgetWindow { get; init; } = DefaultRetryBudgetWindow;

    public TimeSpan SoftFailureInitialBackoff { get; init; } = DefaultSoftFailureInitialBackoff;

    public INetConnector Connector { get; }

    public IReadOnlyList<NetEndpoint> Endpoints { get; }

    public TimeSpan EndpointBlacklistDuration { get; init; } = TimeSpan.FromSeconds(3);

    public int? Backlog { get; init; }

    public TimeSpan SessionPairTimeout { get; init; } = TimeSpan.FromSeconds(2);

    public INetDnsResolver? DnsResolver { get; init; }
}
