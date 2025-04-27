// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.IO;
using Itexoft.Threading;

namespace Itexoft.Net.Http.Internal;

internal sealed class NetHttpReadOnlyStream(IStreamRa inner, bool leaveOpen) : Stream
{
    private readonly IStreamRa inner = inner ?? throw new ArgumentNullException(nameof(inner));

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        this.inner.ReadAsync(buffer.AsMemory(offset, count), CancelToken.None).GetAwaiter().GetResult();

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        this.inner.ReadAsync(buffer, new(cancellationToken));

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        this.inner.ReadAsync(buffer.AsMemory(offset, count), new(cancellationToken)).AsTask();

    public override void Flush() { }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing && !leaveOpen)
        {
            var task = this.inner.DisposeAsync();

            if (!task.IsCompletedSuccessfully)
                task.AsTask().GetAwaiter().GetResult();
        }

        base.Dispose(disposing);
    }
}

internal sealed class NetHttpStreamAdapter(Stream inner, bool leaveOpen) : StreamBase, IStreamRa
{
    private readonly Stream inner = inner ?? throw new ArgumentNullException(nameof(inner));

    public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancelToken cancelToken = default)
    {
        using (cancelToken.Bridge(out var token))
            return await this.inner.ReadAsync(buffer, token);
    }

    protected async override ValueTask DisposeAny()
    {
        if (leaveOpen)
            return;

        if (this.inner is IAsyncDisposable asyncDisposable)
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
        else
            await this.inner.DisposeAsync();
    }
}
