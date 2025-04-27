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

public sealed class NetSocks4Proxy(NetEndpoint endpoint, NetCredential credential = default) : INetProxy
{
    public NetSocks4Proxy(NetDnsHost host, NetPort port, NetCredential credential = default) : this(new(host, port), credential) { }

    public NetEndpoint Endpoint { get; } = endpoint;
    public NetCredential Credential { get; } = credential;

    public async ValueTask<TStream> ConnectAsync<TStream>(NetEndpoint endpoint, TStream stream, CancelToken cancelToken = default)
        where TStream : class, INetStream
    {
        await this.SendConnectAsync(endpoint, stream, cancelToken).ConfigureAwait(false);
        await this.ReadResponseAsync(stream, cancelToken).ConfigureAwait(false);

        return stream;
    }

    private async ValueTask SendConnectAsync<TStream>(NetEndpoint endpoint, TStream stream, CancelToken cancelToken) where TStream : class, INetStream
    {
        var user = this.Credential.UserName ?? string.Empty;
        var userLength = user.Length == 0 ? 0 : Encoding.ASCII.GetByteCount(user);

        int port;
        byte[] addressBytes;
        byte[]? hostBytes = null;
        var useSocks4A = false;

        if (endpoint.TryCreate(out var ipep) && ipep.AddressFamily == AddressFamily.InterNetwork)
        {
            port = ipep.Port;
            addressBytes = ipep.IpAddress.GetBytes();
        }
        else
        {
            port = endpoint.Port;
            useSocks4A = true;
            addressBytes = new byte[4]; // 0.0.0.1 marker will be set
            hostBytes = Encoding.ASCII.GetBytes(endpoint.Host);
        }

        var payloadLength = 8 + userLength + 1 + (useSocks4A && hostBytes is not null ? hostBytes.Length + 1 : 0);
        var buffer = ArrayPool<byte>.Shared.Rent(payloadLength);

        try
        {
            buffer[0] = 0x04;
            buffer[1] = 0x01; // CONNECT
            BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(2, 2), (ushort)port);

            if (useSocks4A)
            {
                buffer[4] = 0x00;
                buffer[5] = 0x00;
                buffer[6] = 0x00;
                buffer[7] = 0x01; // SOCKS4a marker
            }
            else
                addressBytes.CopyTo(buffer.AsSpan(4, 4));

            var offset = 8;

            if (userLength > 0)
            {
                Encoding.ASCII.GetBytes(user, buffer.AsSpan(offset, userLength));
                offset += userLength;
            }

            buffer[offset++] = 0x00; // user terminator

            if (useSocks4A && hostBytes is { Length: > 0 })
            {
                hostBytes.CopyTo(buffer.AsSpan(offset));
                offset += hostBytes.Length;
                buffer[offset++] = 0x00; // host terminator
            }

            await stream.WriteAsync(buffer.AsMemory(0, offset), cancelToken).ConfigureAwait(false);
            await stream.FlushAsync(cancelToken).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private async ValueTask ReadResponseAsync<TStream>(TStream stream, CancelToken cancelToken) where TStream : class, INetStream
    {
        var response = ArrayPool<byte>.Shared.Rent(8);

        try
        {
            await stream.ReadExactAsync(response.AsMemory(0, 8), cancelToken).ConfigureAwait(false);

            if (response[0] != 0x00 || response[1] != 0x5A)
                throw new IOException($"SOCKS4 connect failed with code 0x{response[1]:X2}.");
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(response);
        }
    }
}
