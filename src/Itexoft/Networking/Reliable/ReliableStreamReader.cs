// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Networking.Reliable;

/// <summary>
/// Read-only stream that consumes payload blocks and automatically sends acknowledgements.
/// </summary>
public sealed class ReliableStreamReader : Stream
{
    private readonly bool _sendAcknowledgements;
    private readonly IReliableStreamTransport _transport;
    private bool _disposed;

    /// <summary>
    /// Creates a new stream reader over the provided transport.
    /// </summary>
    /// <param name="transport">Transport delivering payload blocks.</param>
    /// <param name="sendAcknowledgements">Whether reader should automatically acknowledge received blocks.</param>
    public ReliableStreamReader(IReliableStreamTransport transport, bool sendAcknowledgements = true)
    {
        this._transport = transport ?? throw new ArgumentNullException(nameof(transport));
        this._sendAcknowledgements = sendAcknowledgements;
    }

    /// <inheritdoc />
    public override bool CanRead => true;

    /// <inheritdoc />
    public override bool CanSeek => false;

    /// <inheritdoc />
    public override bool CanWrite => false;

    /// <inheritdoc />
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    /// <inheritdoc />
    public override void Flush() { }

    /// <inheritdoc />
    public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count) =>
        this.ReadAsync(buffer.AsMemory(offset, count), CancellationToken.None).GetAwaiter().GetResult();

    /// <inheritdoc />
    public async override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        this.ThrowIfDisposed();

        var block = await this._transport.ReceiveAsync(cancellationToken).ConfigureAwait(false);

        if (block.IsAcknowledgement)

            // Skip acknowledgements; wait for payload.
            return await this.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);

        var length = Math.Min(block.Payload.Length, buffer.Length);
        block.Payload.Span.Slice(0, length).CopyTo(buffer.Span);

        if (this._sendAcknowledgements)
            await this._transport.AcknowledgeAsync(block.SequenceNumber, cancellationToken).ConfigureAwait(false);

        return length;
    }

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    /// <inheritdoc />
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing && !this._disposed)
            this._disposed = true;

        base.Dispose(disposing);
    }

    /// <inheritdoc />
    public async override ValueTask DisposeAsync()
    {
        if (!this._disposed)
            this._disposed = true;

        await this._transport.DisposeAsync().ConfigureAwait(false);
        await base.DisposeAsync().ConfigureAwait(false);
    }

    private void ThrowIfDisposed()
    {
        if (this._disposed)
            throw new ObjectDisposedException(nameof(ReliableStreamReader));
    }
}