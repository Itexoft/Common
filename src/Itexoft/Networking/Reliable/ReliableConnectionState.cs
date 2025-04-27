// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Networking.Reliable;

/// <summary>
/// Represents the lifecycle state of a reliable connection.
/// </summary>
public enum ReliableConnectionState
{
    /// <summary>Connection is closed and idle.</summary>
    Disconnected,

    /// <summary>Dial attempts are in progress.</summary>
    Connecting,

    /// <summary>Handshake exchange is in progress.</summary>
    Handshake,

    /// <summary>Connection established and healthy.</summary>
    Established,

    /// <summary>Heartbeat detected issues but recovery is possible.</summary>
    Degraded,

    /// <summary>Make-before-break sequence is running.</summary>
    Reconnecting,

    /// <summary>Switching to a new transport handle.</summary>
    Switching,

    /// <summary>Connection failed and will require manual recovery.</summary>
    Failed,

    /// <summary>Client disposed and no further activity should occur.</summary>
    Disposed
}