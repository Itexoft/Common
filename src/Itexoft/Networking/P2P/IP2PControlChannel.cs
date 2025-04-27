// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Networking.P2P;

/// <summary>
/// Abstraction over the control channel used for heartbeat, diagnostics and session recovery.
/// </summary>
public interface IP2PControlChannel
{
    ValueTask SendAsync(P2PControlFrame frame, CancellationToken cancellationToken);

    IAsyncEnumerable<P2PControlFrame> ReadFramesAsync(CancellationToken cancellationToken);

    ValueTask FlushAsync(CancellationToken cancellationToken);
}