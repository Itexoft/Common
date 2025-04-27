// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Networking.P2P;

/// <summary>
/// Plug-in responsible for establishing the physical transport connection to the peer.
/// </summary>
public interface IP2PTransportAdapter
{
    Task<P2PTransportConnection> ConnectAsync(
        P2PTransportEndpoint endpoint,
        P2PConnectionOptions options,
        CancellationToken cancellationToken);
}