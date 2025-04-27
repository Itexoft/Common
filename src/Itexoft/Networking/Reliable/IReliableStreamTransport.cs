// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Networking.Reliable;

/// <summary>
/// Minimal abstraction over a transport capable of sending payload blocks and receiving acknowledgements.
/// </summary>
public interface IReliableStreamTransport : IAsyncDisposable
{
    /// <summary>
    /// Sends a payload block with the provided sequence number.
    /// </summary>
    ValueTask SendAsync(long sequenceNumber, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the next payload block delivered by the remote peer.
    /// </summary>
    ValueTask<TransportBlock> ReceiveAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends an acknowledgement for the specified sequence number.
    /// </summary>
    ValueTask AcknowledgeAsync(long sequenceNumber, CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents a single block received from the transport.
/// </summary>
public readonly record struct TransportBlock(long SequenceNumber, ReadOnlyMemory<byte> Payload, bool IsAcknowledgement);