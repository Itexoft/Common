// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Extensions;
using Itexoft.Net.Core;
using Itexoft.Threading;

namespace Itexoft.IO;

internal sealed class FromBclStreamWrapper(Stream stream) : StreamBase(stream), IStreamBcl, INetStream
{
    private readonly Stream stream = stream;

    public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancelToken cancelToken = default)
    {
        using (cancelToken.Bridge(out var token))
            return await this.stream.ReadAsync(buffer, token);
    }

    public int Read(Span<byte> buffer) => this.stream.Read(buffer);

    public TimeSpan ReadTimeout
    {
        get => TimeSpan.FromMilliseconds(this.stream.ReadTimeout);
        set => this.stream.ReadTimeout = value.TimeoutMilliseconds;
    }

    public TimeSpan WriteTimeout
    {
        get => TimeSpan.FromMilliseconds(this.stream.WriteTimeout);
        set => this.stream.WriteTimeout = value.TimeoutMilliseconds;
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

    public void Flush() => this.stream.Flush();

    public void Write(ReadOnlySpan<byte> buffer) => this.stream.Write(buffer);
    public long Length => this.stream.Length;
    public long Seek(long offset, SeekOrigin origin) => this.stream.Seek(offset, origin);
    public void SetLength(long value) => this.stream.SetLength(value);
    public bool CanRead => this.stream.CanRead;
    public bool CanSeek => this.stream.CanSeek;
    public bool CanWrite => this.stream.CanWrite;
    public bool CanTimeout => this.stream.CanTimeout;

    protected override ValueTask DisposeAny()
    {
        this.stream.Dispose();

        return this.stream.DisposeAsync();
    }
}
