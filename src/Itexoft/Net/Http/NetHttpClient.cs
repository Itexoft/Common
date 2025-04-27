// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Buffers;
using System.Net.Security;
using System.Text;
using Itexoft.Extensions;
using Itexoft.IO;
using Itexoft.Net.Core;
using Itexoft.Net.Http.Internal;
using Itexoft.Net.Proxies;
using Itexoft.Threading;

namespace Itexoft.Net.Http;

public sealed class NetHttpClient(NetEndpoint endpoint, NetConnector connector) : IAsyncDisposable
{
    public delegate bool CookieHandlerDelegate(NetCookie cookie);

    private const int maxHeaderBytes = 64 * 1024;
    private const int maxLineBytes = 16 * 1024;
    private static readonly NetHttpMethod headMethod = new("HEAD");
    private static readonly object timeoutSource = new();

    private NetHttpConnection? connection;

    public NetHttpClient(NetEndpoint endpoint, params INetProxy[] proxies) : this(endpoint, new NetTcpConnector(proxies)) { }

    public NetHttpClient(Uri uri, params INetProxy[] proxies) : this(new NetEndpoint(uri.DnsSafeHost, uri.Port), proxies)
    {
        if (uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            this.TlsOptions ??= new SslClientAuthenticationOptions { TargetHost = uri.DnsSafeHost };
    }

    internal INetConnector Connector { get; } = connector.Required();

    public NetEndpoint Endpoint { get; } = endpoint;

    public NetHttpHeaders DefaultHeaders { get; } = new();
    public NetCookieContainer Cookies { get; } = new();

    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(10);
    public SslClientAuthenticationOptions? TlsOptions { get; init; } = CreateDefaultTlsOptions(endpoint);
    public CookieHandlerDelegate? CookieHandler { get; init; }

    public ValueTask DisposeAsync()
    {
        var current = Interlocked.Exchange(ref this.connection, null);

        return current is null ? ValueTask.CompletedTask : current.DisposeAsync();
    }

    public ValueTask<NetHttpResponse> GetAsync(NetHttpPathQuery pathAndQuery, CancelToken cancelToken = default) =>
        this.SendAsync(new(NetHttpMethod.Get, pathAndQuery), cancelToken);

    public ValueTask<NetHttpResponse> PostAsync(
        NetHttpPathQuery pathAndQuery,
        IStreamRal? content = null,
        NetHttpHeaders? headers = null,
        CancelToken cancelToken = default) =>
        this.SendAsync(new(NetHttpMethod.Post, pathAndQuery) { Content = content, Headers = headers ?? new NetHttpHeaders() }, cancelToken);

    public ValueTask<NetHttpResponse> PostAsync(
        NetHttpPathQuery pathAndQuery,
        ReadOnlyMemory<byte> content,
        string? contentType = null,
        CancelToken cancelToken = default)
    {
        var headers = new NetHttpHeaders();

        if (!string.IsNullOrEmpty(contentType))
            headers.ContentType = contentType;

        return this.PostAsync(pathAndQuery, new NetHttpMemoryStream(content), headers, cancelToken);
    }

    public ValueTask<NetHttpResponse> PostAsync(
        NetHttpPathQuery pathAndQuery,
        string content,
        Encoding? encoding = null,
        string? contentType = null,
        CancelToken cancelToken = default)
    {
        encoding ??= Encoding.UTF8;
        var bytes = encoding.GetBytes(content ?? string.Empty);
        contentType ??= $"text/plain; charset={encoding.WebName}";

        return this.PostAsync(pathAndQuery, bytes, contentType, cancelToken);
    }

    public ValueTask<NetHttpResponse> SendAsync(NetHttpRequest request, CancelToken cancelToken = default) =>
        this.SendCoreAsync(request.Required(), cancelToken);

    private async ValueTask<NetHttpResponse> SendCoreAsync(NetHttpRequest request, CancelToken cancelToken)
    {
        var timeout = request.Timeout ?? request.RequestTimeout;
        cancelToken = ApplyTimeout(cancelToken, timeout, true);

        var lease = await this.GetConnection().AcquireAsync(request, cancelToken).ConfigureAwait(false);

        try
        {
            var stream = lease.Stream;
            stream.ReadTimeout = timeout;
            stream.WriteTimeout = timeout;

            var contentLength = GetContentLength(request.Content);

            if (contentLength is null && request.Method == NetHttpMethod.Post)
                contentLength = 0;

            var cookieHeader = this.GetCookieHeader(request);
            var secure = this.TlsOptions is not null;

            await NetHttpRequestWriter.WriteHeadersAsync(
                stream,
                request,
                this.DefaultHeaders,
                this.Endpoint,
                cookieHeader,
                contentLength,
                secure,
                cancelToken).ConfigureAwait(false);

            if (request.Content is not null && contentLength.GetValueOrDefault() > 0)
                await this.SendBodyAsync(stream, request.Content, request.SendBufferSize, cancelToken).ConfigureAwait(false);

            var headerBlock = await lease.Reader.ReadHeadersAsync(maxHeaderBytes, cancelToken).ConfigureAwait(false);
            NetHttpResponseParser.Parse(headerBlock.Span, out var version, out var status, out var headers);
            this.ProcessCookies(headers, request.PathAndQuery);

            var body = this.CreateBodyStream(lease.Reader, request, status, headers, version, out var closeAfterBody);

            if (body is not NetHttpEmptyBodyStream)
                body = NetHttpContentDecoder.Apply(headers, body);

            var responseBody = body;

            if (body is NetHttpEmptyBodyStream)
                await lease.ReleaseAsync(closeAfterBody).ConfigureAwait(false);
            else
                responseBody = new NetHttpResponseBodyStream(body, closeAfterBody, lease);

            var response = new NetHttpResponse(request, version, status, headers, responseBody);

            return response;
        }
        catch (Exception ex)
        {
            await lease.ReleaseAsync(true).ConfigureAwait(false);

            throw;
        }
    }

    private static long? GetContentLength(IStreamRal? content)
    {
        if (content is null)
            return null;

        var remaining = content.Length - content.Position;

        return remaining < 0 ? 0 : remaining;
    }

    private string? GetCookieHeader(NetHttpRequest request)
    {
        if (request.Headers.Cookie is not null)
            return null;

        var container = request.CookieContainer ?? this.Cookies;
        var secure = this.TlsOptions is not null;

        return container.GetCookieHeader(this.Endpoint, request.PathAndQuery.Path, secure);
    }

    private void ProcessCookies(NetHttpHeaders headers, NetHttpPathQuery pathAndQuery)
    {
        var values = headers.SetCookie;

        if (values.Count == 0)
            return;

        var now = DateTimeOffset.UtcNow;
        var domain = (string)this.Endpoint.Host;
        var path = GetDefaultCookiePath(pathAndQuery.Path);

        foreach (var value in values)
        {
            if (!NetCookie.TryParseSetCookie(value.AsSpan(), domain, path, now, out var cookie))
                continue;

            if (this.CookieHandler is not null && !this.CookieHandler(cookie))
                continue;

            this.Cookies.Add(cookie);
        }
    }

    private IStreamRa CreateBodyStream(
        NetHttpBufferedReader reader,
        NetHttpRequest request,
        NetHttpStatus status,
        NetHttpHeaders headers,
        NetHttpVersion version,
        out bool closeAfterBody)
    {
        closeAfterBody = false;
        var hasBody = ResponseHasBody(request, status, headers);
        var connectionClose = headers.ConnectionClose || !request.KeepAlive || request.Headers.ConnectionClose;

        if (version == NetHttpVersion.Version10 && !headers.ConnectionKeepAlive)
            connectionClose = true;

        if (request.ReceiveHeadersOnly)
        {
            closeAfterBody = hasBody || connectionClose;

            return NetHttpEmptyBodyStream.Instance;
        }

        if (!hasBody)
        {
            closeAfterBody = connectionClose;

            return NetHttpEmptyBodyStream.Instance;
        }

        if (headers.TransferEncoding is not null && NetHttpParsing.ContainsToken(headers.TransferEncoding.AsSpan(), "chunked".AsSpan()))
        {
            closeAfterBody = connectionClose;

            return new NetHttpChunkedStream(reader, maxLineBytes);
        }

        if (headers.ContentLength is long length)
        {
            closeAfterBody = connectionClose;

            return new NetHttpContentLengthStream(reader, length);
        }

        closeAfterBody = true;

        return new NetHttpUntilCloseStream(reader);
    }

    private static bool ResponseHasBody(NetHttpRequest request, NetHttpStatus status, NetHttpHeaders headers)
    {
        if (IsHead(request.Method))
            return false;

        var statusClass = status.GetClass();

        if (statusClass == NetHttpStatusClass.Informational)
            return false;

        if (status is NetHttpStatus.NoContent or NetHttpStatus.NotModified)
            return false;

        if (headers.ContentLength is 0)
            return false;

        return true;
    }

    private static SslClientAuthenticationOptions? CreateDefaultTlsOptions(NetEndpoint endpoint)
    {
        if ((int)endpoint.Port != 443)
            return null;

        return new() { TargetHost = endpoint.Host.Host };
    }

    private static bool IsHead(NetHttpMethod method) => method == headMethod;

    private async ValueTask<long> SendBodyAsync(INetStream stream, IStreamRal content, int bufferSize, CancelToken cancelToken)
    {
        var total = 0L;
        var rented = ArrayPool<byte>.Shared.Rent(Math.Max(bufferSize, 1024));

        try
        {
            while (true)
            {
                var read = await content.ReadAsync(rented.AsMemory(), cancelToken).ConfigureAwait(false);

                if (read == 0)
                    break;

                await stream.WriteAsync(rented.AsMemory(0, read), cancelToken).ConfigureAwait(false);
                total += read;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }

        return total;
    }

    private static string GetDefaultCookiePath(string path)
    {
        if (string.IsNullOrEmpty(path) || path[0] != '/')
            return "/";

        var lastSlash = path.LastIndexOf('/');

        return lastSlash <= 0 ? "/" : path[..lastSlash];
    }

    internal static CancelToken ApplyTimeout(CancelToken cancelToken, TimeSpan timeout, bool allowMutate)
    {
        if (timeout.IsInfinite || timeout <= TimeSpan.Zero)
            return cancelToken;

        return cancelToken.Branch(timeout);
    }

    private NetHttpConnection GetConnection()
    {
        var current = Volatile.Read(ref this.connection);

        if (current is not null)
            return current;

        var created = new NetHttpConnection(this);
        var existing = Interlocked.CompareExchange(ref this.connection, created, null);

        if (existing is not null)
            return existing;

        return created;
    }
}
