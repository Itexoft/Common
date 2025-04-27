// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Networking.P2P;

/// <summary>
/// Resolves transport adapters based on endpoint metadata.
/// </summary>
public interface IP2PTransportAdapterResolver
{
    IP2PTransportAdapter Resolve(P2PTransportEndpoint endpoint);
}

/// <summary>
/// Registry that maps transport identifiers to adapter instances.
/// </summary>
public sealed class P2PTransportAdapterRegistry : IP2PTransportAdapterResolver
{
    private readonly Dictionary<string, IP2PTransportAdapter> _adapters;

    public P2PTransportAdapterRegistry(IEnumerable<(string transport, IP2PTransportAdapter adapter)>? adapters = null)
    {
        this._adapters = new(StringComparer.OrdinalIgnoreCase);

        if (adapters is null)
            return;

        foreach (var (transport, adapter) in adapters)
            this.Register(transport, adapter);
    }

    public IP2PTransportAdapter Resolve(P2PTransportEndpoint endpoint)
    {
        if (endpoint is null)
            throw new ArgumentNullException(nameof(endpoint));

        if (this._adapters.TryGetValue(endpoint.Transport, out var adapter))
            return adapter;

        throw new InvalidOperationException($"Transport adapter '{endpoint.Transport}' is not registered.");
    }

    public void Register(string transport, IP2PTransportAdapter adapter)
    {
        if (string.IsNullOrWhiteSpace(transport))
            throw new ArgumentException("Transport name is required.", nameof(transport));

        this._adapters[transport] = adapter ?? throw new ArgumentNullException(nameof(adapter));
    }
}

internal sealed class SingleTransportAdapterResolver : IP2PTransportAdapterResolver
{
    private readonly IP2PTransportAdapter _adapter;

    public SingleTransportAdapterResolver(IP2PTransportAdapter adapter) =>
        this._adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));

    public IP2PTransportAdapter Resolve(P2PTransportEndpoint endpoint) => this._adapter;
}