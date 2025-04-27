// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

#nullable enable
using System.Text;
using Itexoft.IO;
using Itexoft.Net.Core;
using Itexoft.Threading;

namespace Itexoft.Net.Proxies;

public class NetHttpProxy(NetEndpoint endpoint, NetCredential credential = default) : INetProxy
{
    public NetHttpProxy(NetDnsHost host, NetPort port, NetCredential credentials = default) : this(new(host, port), credentials) { }

    public NetEndpoint Endpoint { get; } = endpoint;
    public NetCredential Credential { get; } = credential;

    public virtual async ValueTask<TStream> ConnectAsync<TStream>(NetEndpoint endpoint, TStream stream, CancelToken cancelToken = default)
        where TStream : class, INetStream
    {
        await this.SendConnectAsync(endpoint, stream, cancelToken).ConfigureAwait(false);
        await this.ValidateResponseAsync(stream, cancelToken).ConfigureAwait(false);

        return stream;
    }

    public static async ValueTask<bool> CanHandle(IStreamRa stream, CancelToken cancelToken = default)
    {
        var probe = await stream.ReadBytesAsync(7, cancelToken);
        var hasSpace = false;

        for (var i = 0; i < probe.Length; i++)
        {
            var b = probe.Span[i];

            if (b == (byte)' ')
            {
                hasSpace = true;

                break;
            }

            if (!IsAsciiLetter(b))
                return false;
        }

        return hasSpace || probe.Length >= 3;
    }

    private async ValueTask SendConnectAsync(NetEndpoint endpoint, INetStream stream, CancelToken cancelToken)
    {
        var builder = new StringBuilder();
        builder.Append("CONNECT ").Append(endpoint.Host).Append(':').Append(endpoint.Port).Append(" HTTP/1.1\r\n");
        builder.Append("Host: ").Append(endpoint.Host).Append("\r\n");

        if (this.Credential != default)
        {
            var user = this.Credential.UserName ?? string.Empty;
            var pass = this.Credential.Password ?? string.Empty;
            var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{user}:{pass}"));
            builder.Append("Proxy-Authorization: Basic ").Append(token).Append("\r\n");
        }

        builder.Append("\r\n");

        var request = builder.ToString();
        var bytes = Encoding.ASCII.GetBytes(request);
        await stream.WriteAsync(bytes, cancelToken).ConfigureAwait(false);
    }

    private async Task ValidateResponseAsync(INetStream stream, CancelToken cancelToken)
    {
        var statusLine = await ReadStatusLineAsync(stream, cancelToken).ConfigureAwait(false);
        var status = ParseStatusCode(statusLine);

        if (status is < 200 or >= 300)
            throw new IOException($"HTTP proxy CONNECT failed with status {status}.");

        await DrainHeadersAsync(stream, cancelToken).ConfigureAwait(false);
    }

    private static async ValueTask<string?> ReadStatusLineAsync(INetStream stream, CancelToken cancelToken)
    {
        using var ms = new MemoryStream();
        var buffer = new byte[1];
        var lastWasCr = false;

        while (true)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, 1), cancelToken).ConfigureAwait(false);

            if (read == 0)
                return null;

            var b = buffer[0];
            ms.WriteByte(b);

            if (b == '\r')
            {
                lastWasCr = true;

                continue;
            }

            if (lastWasCr && b == '\n')
                break;

            lastWasCr = false;

            if (ms.Length > 4096)
                throw new IOException("Proxy response too large.");
        }

        return Encoding.ASCII.GetString(ms.GetBuffer(), 0, (int)ms.Length - 2);
    }

    private static int ParseStatusCode(string? statusLine)
    {
        if (statusLine is null || statusLine.Length < 12 || !statusLine.StartsWith("HTTP/", StringComparison.OrdinalIgnoreCase))
            throw new IOException("Invalid proxy response.");

        var statusStart = statusLine.IndexOf(' ');
        var statusEnd = statusStart >= 0 ? statusLine.IndexOf(' ', statusStart + 1) : -1;

        if (statusStart < 0 || statusEnd < 0 || !int.TryParse(statusLine.AsSpan(statusStart + 1, statusEnd - statusStart - 1), out var statusCode))
            throw new IOException($"Invalid proxy status line: {statusLine}");

        return statusCode;
    }

    private static async ValueTask DrainHeadersAsync(INetStream stream, CancelToken cancelToken)
    {
        var buffer = new byte[1];
        var lineLength = 0;
        var lastWasCr = false;

        while (true)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, 1), cancelToken).ConfigureAwait(false);

            if (read == 0)
                throw new IOException("Unexpected end of proxy response.");

            var b = buffer[0];

            if (lastWasCr)
            {
                if (b == '\n')
                {
                    if (lineLength == 0)
                        return;

                    lineLength = 0;
                    lastWasCr = false;

                    continue;
                }

                lastWasCr = false;
            }

            if (b == '\r')
            {
                lastWasCr = true;

                continue;
            }

            lineLength++;
        }
    }

    private static bool IsAsciiLetter(byte b) =>
        b is >= (byte)'A' and <= (byte)'Z' || b is >= (byte)'a' and <= (byte)'z';
}
