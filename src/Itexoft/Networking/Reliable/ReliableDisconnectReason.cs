// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Networking.Reliable;

/// <summary>
/// Describes why a reliable connection is being terminated.
/// </summary>
public enum ReliableDisconnectReason
{
    /// <summary>Disconnect requested by the caller.</summary>
    Manual,

    /// <summary>Application is shutting down.</summary>
    Shutdown,

    /// <summary>Connection terminated due to unrecoverable network error.</summary>
    NetworkError
}