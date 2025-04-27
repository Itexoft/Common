// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Networking.P2P;

/// <summary>
/// High-level manager that establishes, supervises and switches P2P connections.
/// </summary>
public interface IP2PConnectionManager : IAsyncDisposable
{
    P2PConnectionHandle? CurrentConnection { get; }

    P2PConnectionState State { get; }
    event EventHandler<P2PConnectionStateChangedEventArgs>? ConnectionStateChanged;

    Task<P2PConnectionHandle> ConnectAsync(CancellationToken cancellationToken = default);

    Task DisconnectAsync(DisconnectReason reason = DisconnectReason.Manual, CancellationToken cancellationToken = default);

    P2PConnectionMetrics GetMetrics();
}

public enum DisconnectReason
{
    Manual,
    Shutdown,
    FatalError
}