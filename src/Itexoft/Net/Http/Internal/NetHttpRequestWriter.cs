// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Buffers;
using Itexoft.Net.Core;
using Itexoft.Threading;

namespace Itexoft.Net.Http.Internal;

internal static class NetHttpRequestWriter
{
    private static readonly string hostHeader = "Host";
    private static readonly string connectionHeader = "Connection";
    private static readonly string contentLengthHeader = "Content-Length";
    private static readonly string acceptEncodingHeader = "Accept-Encoding";
    private static readonly string cookieHeader = "Cookie";

    public static async ValueTask WriteHeadersAsync(
        INetStream stream,
        NetHttpRequest request,
        NetHttpHeaders defaultHeaders,
        NetEndpoint endpoint,
        string? cookieValue,
        long? contentLength,
        bool secure,
        CancelToken cancelToken)
    {
        var buffer = new ArrayBufferWriter<byte>(512);
        WriteRequestLine(buffer, request);

        var path = request.PathAndQuery;
        var pathValue = path.Path ?? string.Empty;
        var queryCount = path.Query?.Count ?? 0;
        var explicitCookie = request.Headers.Cookie ?? cookieValue;
        var hasAuth = request.Headers.Authorization is not null || request.Headers.ProxyAuthorization is not null;
        var hasBody = contentLength.HasValue && contentLength.Value > 0;
        var expectHeader = request.Headers.Expect;
        var transferHeader = request.Headers.TransferEncoding;

        var skipDefaults = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            hostHeader,
            connectionHeader,
            contentLengthHeader,
            acceptEncodingHeader,
        };

        foreach (var header in request.Headers)
            skipDefaults.Add(header.Key);

        if (explicitCookie is not null)
            skipDefaults.Add(cookieHeader);

        var host = request.Headers.Host ?? defaultHeaders.Host ?? BuildHost(endpoint, secure);
        WriteHeader(buffer, hostHeader, host);

        var connection = request.Headers.Connection ?? (request.KeepAlive ? "keep-alive" : "close");
        WriteHeader(buffer, connectionHeader, connection);

        if (contentLength.HasValue)
            WriteHeader(buffer, contentLengthHeader, contentLength.Value);

        var acceptEncoding = request.Headers.AcceptEncoding ?? defaultHeaders.AcceptEncoding ?? "gzip, br, deflate";
        WriteHeader(buffer, acceptEncodingHeader, acceptEncoding);

        if (explicitCookie is not null)
            WriteHeader(buffer, cookieHeader, explicitCookie);

        WriteHeaderSet(buffer, defaultHeaders, skipDefaults);

        var skipRequest = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            hostHeader,
            connectionHeader,
            contentLengthHeader,
            acceptEncodingHeader,
        };

        if (explicitCookie is not null)
            skipRequest.Add(cookieHeader);

        WriteHeaderSet(buffer, request.Headers, skipRequest);

        WriteCrlf(buffer);

        var headerBytes = buffer.WrittenCount;
        await stream.WriteAsync(buffer.WrittenMemory, cancelToken).ConfigureAwait(false);
        await stream.FlushAsync(cancelToken).ConfigureAwait(false);
    }

    private static void WriteRequestLine(ArrayBufferWriter<byte> buffer, NetHttpRequest request)
    {
        WriteAscii(buffer, request.Method.ToString().AsSpan());
        WriteByte(buffer, (byte)' ');
        request.PathAndQuery.WriteTo(buffer);
        WriteByte(buffer, (byte)' ');
        WriteAscii(buffer, request.HttpVersion == NetHttpVersion.Version10 ? "HTTP/1.0".AsSpan() : "HTTP/1.1".AsSpan());
        WriteCrlf(buffer);
    }

    private static void WriteHeaderSet(ArrayBufferWriter<byte> buffer, NetHttpHeaders headers, HashSet<string> skip)
    {
        foreach (var header in headers)
        {
            if (skip.Contains(header.Key))
                continue;

            WriteHeader(buffer, header.Key.AsSpan(), header.Value.AsSpan());
        }
    }

    private static void WriteHeader(ArrayBufferWriter<byte> buffer, string name, string? value)
    {
        if (string.IsNullOrEmpty(value))
            return;

        WriteHeader(buffer, name.AsSpan(), value.AsSpan());
    }

    private static void WriteHeader(ArrayBufferWriter<byte> buffer, string name, long value)
    {
        WriteAscii(buffer, name.AsSpan());
        WriteByte(buffer, (byte)':');
        WriteByte(buffer, (byte)' ');
        WriteInt64(buffer, value);
        WriteCrlf(buffer);
    }

    private static void WriteHeader(ArrayBufferWriter<byte> buffer, ReadOnlySpan<char> name, ReadOnlySpan<char> value)
    {
        if (value.IsEmpty)
            return;

        WriteAscii(buffer, name);
        WriteByte(buffer, (byte)':');
        WriteByte(buffer, (byte)' ');
        WriteAscii(buffer, value);
        WriteCrlf(buffer);
    }

    private static string BuildHost(NetEndpoint endpoint, bool secure)
    {
        var host = (string)endpoint.Host;
        var port = (int)endpoint.Port;
        var defaultPort = secure ? 443 : 80;

        return port == defaultPort ? host : $"{host}:{port}";
    }

    private static void WriteCrlf(ArrayBufferWriter<byte> buffer)
    {
        var span = buffer.GetSpan(2);
        span[0] = (byte)'\r';
        span[1] = (byte)'\n';
        buffer.Advance(2);
    }

    private static void WriteByte(ArrayBufferWriter<byte> buffer, byte value)
    {
        var span = buffer.GetSpan(1);
        span[0] = value;
        buffer.Advance(1);
    }

    private static void WriteAscii(ArrayBufferWriter<byte> buffer, ReadOnlySpan<char> text)
    {
        var span = buffer.GetSpan(text.Length);

        for (var i = 0; i < text.Length; i++)
            span[i] = text[i] <= 0x7F ? (byte)text[i] : (byte)'?';

        buffer.Advance(text.Length);
    }

    private static void WriteInt64(ArrayBufferWriter<byte> buffer, long value)
    {
        Span<char> chars = stackalloc char[20];
        var index = chars.Length;
        var negative = value < 0;

        if (negative)
            value = -value;

        do
        {
            var digit = (int)(value % 10);
            chars[--index] = (char)('0' + digit);
            value /= 10;
        }
        while (value != 0);

        if (negative)
            chars[--index] = '-';

        WriteAscii(buffer, chars[index..]);
    }
}
