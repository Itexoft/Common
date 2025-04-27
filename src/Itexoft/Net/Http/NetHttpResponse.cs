// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Buffers;
using System.Text;
using Itexoft.Core;
using Itexoft.IO;
using Itexoft.Threading;

namespace Itexoft.Net.Http;

public sealed class NetHttpResponse : IAsyncDisposable
{
    private Disposed disposed;

    internal NetHttpResponse(INetHttpRequestInfo request, NetHttpVersion version, NetHttpStatus status, NetHttpHeaders headers, IStreamRa body)
    {
        this.Request = request;
        this.Version = version;
        this.Status = status;
        this.Headers = headers;
        this.Body = body;
    }

    public INetHttpRequestInfo Request { get; }
    public NetHttpVersion Version { get; }
    public NetHttpStatus Status { get; }
    public NetHttpStatusClass StatusClass => this.Status.GetClass();
    public NetHttpHeaders Headers { get; }
    public IStreamRa Body { get; }
    public bool IsSuccess => this.StatusClass == NetHttpStatusClass.Success;

    public ValueTask DisposeAsync()
    {
        if (this.disposed.Enter())
            return ValueTask.CompletedTask;

        return this.Body.DisposeAsync();
    }

    public ValueTask<string> ReadAsStringAsync(CancelToken cancelToken = default) => this.ReadAsStringAsync(Encoding.UTF8, cancelToken);

    public async ValueTask<string> ReadAsStringAsync(Encoding? encoding, CancelToken cancelToken = default)
    {
        var bytes = await this.ReadAsBytes(cancelToken).ConfigureAwait(false);
        encoding ??= ResolveEncoding(this.Headers);

        return encoding.GetString(bytes);
    }

    public async ValueTask<ReadOnlyMemory<byte>> ReadAsMemory(CancelToken cancelToken = default) =>
        await this.ReadAsBytes(cancelToken).ConfigureAwait(false);

    public async ValueTask<byte[]> ReadAsBytes(CancelToken cancelToken = default)
    {
        var timeout = this.Request.Timeout ?? this.Request.RequestTimeout;
        cancelToken = NetHttpClient.ApplyTimeout(cancelToken, timeout, false);
        var poolBuffer = ArrayPool<byte>.Shared.Rent(16 * 1024);
        var writer = new ArrayBufferWriter<byte>(16 * 1024);
        var expectedLength = this.Headers.ContentLength;
        var contentEncoding = this.Headers.ContentEncoding;
        var total = 0L;

        try
        {
            while (true)
            {
                var read = await this.Body.ReadAsync(poolBuffer, cancelToken).ConfigureAwait(false);

                if (read == 0)
                    break;

                writer.Write(poolBuffer.AsSpan(0, read));
                total += read;
            }

            return writer.WrittenSpan.ToArray();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(poolBuffer);
            await this.Body.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static Encoding ResolveEncoding(NetHttpHeaders headers)
    {
        var charset = headers.ContentType.Charset;

        if (!string.IsNullOrEmpty(charset))
        {
            try
            {
                return Encoding.GetEncoding(charset);
            }
            catch
            {
                // ignored
            }
        }

        return Encoding.UTF8;
    }

    public void EnsureSuccess()
    {
        if (this.IsSuccess)
            return;

        throw new HttpProtocolException((int)this.Status, "HTTP Error", null);
    }
}
