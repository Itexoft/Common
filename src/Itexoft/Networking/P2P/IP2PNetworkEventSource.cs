// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Networking.P2P;

/// <summary>
/// Abstraction over network-change notifications used to inform the connection manager about system level events.
/// </summary>
public interface IP2PNetworkEventSource : IDisposable
{
    event EventHandler? NetworkAvailabilityLost;
    event EventHandler? NetworkAddressChanged;
}