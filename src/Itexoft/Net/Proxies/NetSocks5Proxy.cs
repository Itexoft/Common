// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Buffers;
using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;
using Itexoft.IO;
using Itexoft.Net.Core;
using Itexoft.Threading;

namespace Itexoft.Net.Proxies;

public readonly struct NetSocks5Proxy(NetEndpoint endpoint, NetCredential credential = default) : INetProxy
{
    public NetSocks5Proxy(NetDnsHost host, NetPort port, NetCredential credential = default) : this(new(host, port), credential) { }

    private const byte authVersion = 0x01;
    private static readonly byte[] greetingCredential = [0x05, 0x02, 0x00, 0x02];
    private static readonly byte[] greeting = [0x05, 0x01, 0x00];

    public NetEndpoint Endpoint { get; } = endpoint;
    public NetCredential Credential { get; } = credential;

    public async ValueTask<TStream> ConnectAsync<TStream>(NetEndpoint endpoint, TStream stream, CancelToken cancelToken = default)
        where TStream : class, INetStream
    {
        await this.PerformHandshakeAsync(stream, cancelToken).ConfigureAwait(false);
        await this.SendConnectAsync(endpoint, stream, cancelToken).ConfigureAwait(false);

        return stream;
    }

    private async ValueTask PerformHandshakeAsync(INetStream stream, CancelToken cancelToken)
    {
        var credential = this.Credential;
        await stream.WriteAsync(credential == default ? greeting : greetingCredential, cancelToken).ConfigureAwait(false);
        await stream.FlushAsync(cancelToken).ConfigureAwait(false);

        var response = ArrayPool<byte>.Shared.Rent(2);

        try
        {
            await stream.ReadExactAsync(response.AsMemory(0, 2), cancelToken).ConfigureAwait(false);

            if (response[0] != 0x05)
                throw new IOException("SOCKS5 handshake failed.");

            var method = response[1];

            if (method == 0x02)
            {
                if (credential == default)
                    throw new IOException("SOCKS5 proxy requires authentication.");

                await this.AuthenticateAsync(stream, credential, cancelToken).ConfigureAwait(false);
            }
            else if (method != 0x00)
                throw new IOException($"SOCKS5 authentication method not accepted: 0x{method:X2}.");
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(response);
        }
    }

    private async ValueTask AuthenticateAsync(INetStream stream, NetCredential credential, CancelToken cancelToken)
    {
        var userBytes = Encoding.ASCII.GetBytes(credential.UserName ?? string.Empty);
        var passBytes = Encoding.ASCII.GetBytes(credential.Password is null ? string.Empty : credential.Password?.ToString() ?? string.Empty);

        if (userBytes.Length > byte.MaxValue || passBytes.Length > byte.MaxValue)
            throw new IOException("SOCKS5 credentials are too long.");

        var length = 3 + userBytes.Length + passBytes.Length;
        var payload = ArrayPool<byte>.Shared.Rent(length);
        payload[0] = authVersion;
        payload[1] = (byte)userBytes.Length;
        userBytes.CopyTo(payload.AsSpan(2));
        payload[2 + userBytes.Length] = (byte)passBytes.Length;
        passBytes.CopyTo(payload.AsSpan(3 + userBytes.Length));

        try
        {
            await stream.WriteAsync(payload.AsMemory(0, length), cancelToken).ConfigureAwait(false);
            await stream.FlushAsync(cancelToken).ConfigureAwait(false);

            var response = ArrayPool<byte>.Shared.Rent(2);

            try
            {
                await stream.ReadExactAsync(response.AsMemory(0, 2), cancelToken).ConfigureAwait(false);

                if (response[0] != authVersion || response[1] != 0x00)
                    throw new IOException("SOCKS5 authentication failed.");
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(response);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(payload);
        }
    }

    private (byte attrType, byte[] data, int port) GetAttrDataPort(NetEndpoint endpoint)
    {
        if (endpoint.TryCreate(out var ip))
        {
            switch (ip.AddressFamily)
            {
                case AddressFamily.InterNetwork:
                    return ((byte)0x01, ip.IpAddress.GetBytes(), ip.Port);
                case AddressFamily.InterNetworkV6:
                    return ((byte)0x04, ip.IpAddress.GetBytes(), ip.Port);
            }
        }

        var hostBytes = Encoding.ASCII.GetBytes(endpoint.Host);
        var data = new byte[1 + hostBytes.Length];
        data[0] = (byte)hostBytes.Length;
        hostBytes.CopyTo(data, 1);

        return ((byte)0x03, data, endpoint.Port);
    }

    private async ValueTask SendConnectAsync(NetEndpoint endpoint, INetStream stream, CancelToken cancelToken)
    {
        var (attrType, data, port) = this.GetAttrDataPort(endpoint);
        var length = 4 + data.Length + 2;
        var buffer = ArrayPool<byte>.Shared.Rent(length);
        buffer[0] = 0x05;
        buffer[1] = 0x01;
        buffer[2] = 0x00;
        buffer[3] = attrType;
        data.CopyTo(buffer, 4);
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(4 + data.Length), (ushort)port);

        try
        {
            await stream.WriteAsync(buffer.AsMemory(0, length), cancelToken).ConfigureAwait(false);
            await stream.FlushAsync(cancelToken).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        var head = new byte[4];
        await stream.ReadExactAsync(head, cancelToken).ConfigureAwait(false);

        if (head[0] != 0x05)
            throw new IOException("Invalid SOCKS5 response.");

        if (head[1] != 0x00)
            throw new IOException($"SOCKS5 connect failed with code 0x{head[1]:X2}.");

        var atyp = head[3];

        var toSkip = atyp switch
        {
            0x01 => 4,
            0x04 => 16,
            0x03 => await readLengthAsync(stream, cancelToken).ConfigureAwait(false),
            _ => 0,
        };

        if (toSkip > 0)
        {
            var skip = ArrayPool<byte>.Shared.Rent(toSkip + 2);

            try
            {
                await stream.ReadExactAsync(skip.AsMemory(0, toSkip + 2), cancelToken).ConfigureAwait(false);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(skip);
            }
        }

        return;

        static async Task<int> readLengthAsync(INetStream stream, CancelToken cancelToken)
        {
            var buffer = new byte[1];
            await stream.ReadExactAsync(buffer, cancelToken).ConfigureAwait(false);

            return buffer[0];
        }
    }
}
