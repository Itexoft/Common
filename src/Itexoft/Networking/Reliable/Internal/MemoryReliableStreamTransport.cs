// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Threading.Channels;

namespace Itexoft.Networking.Reliable.Internal;

/// <summary>
/// In-memory duplex transport used for tests and local flows.
/// </summary>
internal sealed class MemoryReliableStreamTransport : IReliableStreamTransport
{
    private readonly Channel<TransportBlock> _incoming;
    private readonly ChannelWriter<TransportBlock> _peerWriter;

    private MemoryReliableStreamTransport(Channel<TransportBlock> incoming, ChannelWriter<TransportBlock> peerWriter)
    {
        this._incoming = incoming;
        this._peerWriter = peerWriter;
    }

    /// <inheritdoc />
    public ValueTask SendAsync(long sequenceNumber, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default) =>
        this._peerWriter.WriteAsync(new(sequenceNumber, payload, false), cancellationToken);

    /// <inheritdoc />
    public async ValueTask<TransportBlock> ReceiveAsync(CancellationToken cancellationToken = default) =>
        await this._incoming.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public ValueTask AcknowledgeAsync(long sequenceNumber, CancellationToken cancellationToken = default) => this._peerWriter.WriteAsync(
        new(sequenceNumber, ReadOnlyMemory<byte>.Empty, true),
        cancellationToken);

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        this._peerWriter.TryComplete();

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Creates a pair of in-memory transports wired together back-to-back.
    /// </summary>
    public static (MemoryReliableStreamTransport client, MemoryReliableStreamTransport server) CreatePair()
    {
        var options = new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        };

        var channelA = Channel.CreateUnbounded<TransportBlock>(options);
        var channelB = Channel.CreateUnbounded<TransportBlock>(options);

        return (
            new(channelA, channelB.Writer),
            new(channelB, channelA.Writer));
    }
}