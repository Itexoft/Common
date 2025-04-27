// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Networking.P2P;

public enum P2PConnectionState
{
    Disconnected = 0,
    Connecting = 1,
    Handshake = 2,
    Established = 3,
    Degraded = 4,
    Switching = 5,
    Reconnecting = 6,
    Failed = 7,
    Disposed = 8
}