// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Threading;

namespace Itexoft.IO;

public sealed class RamStream : StreamBase, IStreamSlrw
{
    private readonly MemoryStream stream;

    public RamStream() : base(new MemoryStream()) => this.stream = (MemoryStream)(Stream)this;

    public RamStream(byte[] ints) : base(new MemoryStream(ints)) => this.stream = (MemoryStream)(Stream)this;

    public long Length => this.stream.Length;
    public long Position => this.stream.Position;
    public long Seek(long offset, SeekOrigin origin) => this.stream.Seek(offset, origin);

    public void SetLength(long value) => this.stream.SetLength(value);

    public int Read(Span<byte> buffer) => this.stream.Read(buffer);

    public void Flush() => this.stream.Flush();

    public void Write(ReadOnlySpan<byte> buffer) => this.stream.Write(buffer);

    protected override ValueTask DisposeAny() => this.stream.DisposeAsync();
}

public sealed class RamAsyncStream : StreamBase, IStreamSlrwa
{
    private readonly MemoryStream stream;

    public RamAsyncStream() : base(new MemoryStream()) => this.stream = (MemoryStream)(Stream)this;

    public RamAsyncStream(byte[] ints) : base(new MemoryStream(ints)) => this.stream = (MemoryStream)(Stream)this;

    public long Length => this.stream.Length;
    public long Position => this.stream.Position;
    public long Seek(long offset, SeekOrigin origin) => this.stream.Seek(offset, origin);

    public void SetLength(long value) => this.stream.SetLength(value);

    public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancelToken cancelToken = default)
    {
        using (cancelToken.Bridge(out var token))
            return await this.stream.ReadAsync(buffer, token);
    }

    public async ValueTask FlushAsync(CancelToken cancelToken = default)
    {
        using (cancelToken.Bridge(out var token))
            await this.stream.FlushAsync(token);
    }

    public async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancelToken cancelToken = default)
    {
        using (cancelToken.Bridge(out var token))
            await this.stream.WriteAsync(buffer, token);
    }

    protected override ValueTask DisposeAny() => this.stream.DisposeAsync();
}
