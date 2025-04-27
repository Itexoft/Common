// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Buffers.Binary;
using System.Net.Sockets;

namespace Itexoft.Networking.P2P.Adapters;

internal static class TcpP2PChannelHeader
{
    private const uint Magic = 0x50325031; // "P2P1"
    private const byte ProtocolVersion = 1;
    public const int Length = 22;

    public static async Task SendAsync(Socket socket, Guid sessionId, TcpP2PChannel channel, CancellationToken cancellationToken)
    {
        var buffer = new byte[Length];
        BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(0, 4), Magic);
        buffer[4] = ProtocolVersion;
        buffer[5] = (byte)channel;
        sessionId.TryWriteBytes(buffer.AsSpan(6, 16));
        await SendExactAsync(socket, buffer, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<(Guid sessionId, TcpP2PChannel channel)> ReceiveAsync(Socket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[Length];
        await ReceiveExactAsync(socket, buffer, cancellationToken).ConfigureAwait(false);

        if (BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(0, 4)) != Magic)
            throw new InvalidOperationException("Неверная метка TCP P2P канала.");

        var version = buffer[4];

        if (version != ProtocolVersion)
            throw new InvalidOperationException($"Версия канала {version} не поддерживается (ожидалась {ProtocolVersion}).");

        var channel = (TcpP2PChannel)buffer[5];
        var sessionId = new Guid(buffer.AsSpan(6, 16));

        return (sessionId, channel);
    }

    private static Task SendExactAsync(Socket socket, byte[] buffer, CancellationToken cancellationToken) =>
        socket.SendAsync(new ArraySegment<byte>(buffer), SocketFlags.None, cancellationToken).AsTask();

    private static async Task ReceiveExactAsync(Socket socket, byte[] buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var memory = new ArraySegment<byte>(buffer, offset, buffer.Length - offset);
            var received = await socket.ReceiveAsync(memory, SocketFlags.None, cancellationToken).ConfigureAwait(false);

            if (received == 0)
                throw new InvalidOperationException("Сокет закрыт во время чтения заголовка канала.");

            offset += received;
        }
    }
}

internal enum TcpP2PChannel : byte
{
    Data = 0,
    Control = 1
}

internal static class TcpP2PSocketExtensions
{
    public static void CloseGracefully(this Socket socket)
    {
        try
        {
            if (socket.Connected)
                socket.Shutdown(SocketShutdown.Both);
        }
        catch
        {
            // ignored
        }

        try
        {
            socket.Dispose();
        }
        catch
        {
            // ignored
        }
    }
}