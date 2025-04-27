// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Collections;
using Itexoft.Core;
using Itexoft.Extensions;

namespace Itexoft.Net.Core;

public class NetTransportRegistry : NetTransportRegistry<INetConnector>
{
    public NetTransportRegistry(params (LString, INetConnector)[] connectors) : base(connectors) { }
    public NetTransportRegistry(IEnumerable<(LString, INetConnector)>? connectors) : base(connectors) { }
}

public class NetTransportRegistry<TTransportConnector> where TTransportConnector : class, INetConnector
{
    private readonly AtomicDictionary<LString, TTransportConnector> connectors = [];

    protected NetTransportRegistry(params (LString, TTransportConnector)[] connectors) : this(connectors?.AsEnumerable()) { }

    protected NetTransportRegistry(IEnumerable<(LString, TTransportConnector)>? connectors)
    {
        if (connectors is null)
            return;

        foreach (var (transport, connector) in connectors)
            this.Register(transport, connector);
    }

    public NetTransportRegistry(IDictionary<LString, TTransportConnector>? connectors)
    {
        if (connectors is null)
            return;

        foreach (var (transport, connector) in connectors)
            this.Register(transport, connector);
    }

    public TTransportConnector Resolve(LString transport)
    {
        if (this.connectors.TryGetValue(transport, out var connector))
            return connector;

        throw new InvalidOperationException($"Transport adapter '{transport}' is not registered.");
    }

    public bool Register(LString transport, TTransportConnector connector)
    {
        connector.Required();

        return this.connectors.TryAdd(transport, connector);
    }

    /*public Task<TTransportConnector> ConnectAsync(
        NetTransportEndpoint endpoint,
        NetConnectionOptions options,
        CancelToken cancelToken)
    {
        return this.Resolve(endpoint).ConnectAsync(endpoint, options, cancelToken);
    }*/
}
