// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Net;
using System.Net.Sockets;
using Itexoft.Core;
using Itexoft.Extensions;
using Itexoft.Threading;

namespace Itexoft.Net.Core;

public class NetSocket : IAsyncDisposable
{
    private readonly Func<ValueTask>? dispose;
    internal readonly Socket socket;
    private Disposed disposed;

    private bool keepAliveEnabled;
    private TimeSpan? keepAliveInterval;
    private int? keepAliveRetryCount;
    private TimeSpan? keepAliveTime;

    public NetSocket(AddressFamily addressFamily, SocketType socketType, ProtocolType protocolType, Func<ValueTask>? dispose = null) : this(
        new(addressFamily, socketType, protocolType),
        dispose) { }

    public NetSocket(SocketType addressFamily, ProtocolType socketType, Func<ValueTask>? dispose = null) : this(
        new(addressFamily, socketType),
        dispose) { }

    private NetSocket(Socket socket, Func<ValueTask>? dispose = null)
    {
        this.dispose = dispose;
        this.socket = socket.Required();
    }

    public static bool OsSupportsIPv4 => Socket.OSSupportsIPv4;
    public static bool OsSupportsIPv6 => Socket.OSSupportsIPv6;

    public bool NoDelay
    {
        get => this.socket.NoDelay;
        set => this.socket.NoDelay = value;
    }

    public int SendBufferSize
    {
        get => this.socket.SendBufferSize;
        set => this.socket.SendBufferSize = value;
    }

    public int ReceiveBufferSize
    {
        get => this.socket.ReceiveBufferSize;
        set => this.socket.ReceiveBufferSize = value;
    }

    public TimeSpan ReceiveTimeout
    {
        get => TimeSpan.FromMilliseconds(this.socket.ReceiveTimeout);
        set => this.socket.ReceiveTimeout = value.TimeoutMilliseconds;
    }

    public TimeSpan SendTimeout
    {
        get => TimeSpan.FromMilliseconds(this.socket.SendTimeout);
        set => this.socket.SendTimeout = value.TimeoutMilliseconds;
    }

    public LingerOption? LingerState
    {
        get => this.socket.LingerState;
        set => this.socket.LingerState = value!;
    }

    public NetEndpoint LocalEndPoint => this.socket?.LocalEndPoint is EndPoint ep ? (NetEndpoint)ep : default;
    public NetEndpoint RemoteEndPoint => this.socket.RemoteEndPoint is EndPoint ep ? (NetEndpoint)ep : default;

    public bool KeepAliveEnabled
    {
        get => this.keepAliveEnabled;
        set
        {
            if (this.keepAliveEnabled == value)
                return;

            this.keepAliveEnabled = value;

            this.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, value);

            if (value)
                this.ApplyKeepAliveParameters();
        }
    }

    public TimeSpan? KeepAliveTime
    {
        get => this.keepAliveTime;
        set
        {
            this.keepAliveTime = value;

            if (!this.keepAliveEnabled || !value.HasValue)
                return;

            this.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, ToKeepAliveSeconds(value.Value));
        }
    }

    public TimeSpan? KeepAliveInterval
    {
        get => this.keepAliveInterval;
        set
        {
            this.keepAliveInterval = value;

            if (!this.keepAliveEnabled || !value.HasValue)
                return;

            this.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, ToKeepAliveSeconds(value.Value));
        }
    }

    public int? KeepAliveRetryCount
    {
        get => this.keepAliveRetryCount;
        set
        {
            if (value.HasValue)
                ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value.Value);

            this.keepAliveRetryCount = value;

            if (!this.keepAliveEnabled || !value.HasValue)
                return;

            this.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, value.Value);
        }
    }

    public ValueTask DisposeAsync() => this.DisposeAsync(0);

    private void ApplyKeepAliveParameters()
    {
        if (this.keepAliveTime.HasValue)
            this.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, ToKeepAliveSeconds(this.keepAliveTime.Value));

        if (this.keepAliveInterval.HasValue)
            this.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, ToKeepAliveSeconds(this.keepAliveInterval.Value));

        if (this.keepAliveRetryCount.HasValue)
            this.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, this.keepAliveRetryCount.Value);
    }

    private static int ToKeepAliveSeconds(TimeSpan value)
    {
        if (value <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(value));

        var seconds = (value.Ticks + TimeSpan.TicksPerSecond - 1) / TimeSpan.TicksPerSecond;

        if (seconds > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(value));

        return (int)seconds;
    }

    public static implicit operator NetSocket(Socket socket) => new(socket);

    public async ValueTask<int> SendAsync(ArraySegment<byte> arraySegment, SocketFlags none, CancelToken cancelToken)
    {
        using (cancelToken.Bridge(out var token))
            return await this.socket.SendAsync(arraySegment, none, token);
    }

    public async ValueTask<int> ReceiveAsync(Memory<byte> memory, SocketFlags none, CancelToken cancelToken)
    {
        using (cancelToken.Bridge(out var token))
            return await this.socket.ReceiveAsync(memory, none, token);
    }

    public async ValueTask ConnectAsync(NetIpEndpoint endPoint, CancelToken cancelToken)
    {
        using (cancelToken.Bridge(out var token))
            await this.socket.ConnectAsync(endPoint, token);
    }

    public void Shutdown(SocketShutdown how) => this.socket.Shutdown(how);

    public void SetSocketOption(SocketOptionLevel socketOptionLevel, SocketOptionName socketOptionName, bool optionValue) =>
        this.socket.SetSocketOption(socketOptionLevel, socketOptionName, optionValue);

    public void SetSocketOption(SocketOptionLevel socketOptionLevel, SocketOptionName socketOptionName, int optionValue) =>
        this.socket.SetSocketOption(socketOptionLevel, socketOptionName, optionValue);

    public void Listen(int backlog) => this.socket.Listen(backlog);

    public void Bind(NetIpEndpoint endPoint) => this.socket.Bind(endPoint);

    public void Listen() => this.socket.Listen();

    public async ValueTask<NetSocket> AcceptAsync(CancelToken cancelToken)
    {
        using (cancelToken.Bridge(out var token))
            return new NetSocket(await this.socket.AcceptAsync(token));
    }

    public async ValueTask DisposeAsync(int timeout)
    {
        if (this.disposed.Enter())
            return;

        timeout.RequiredPositiveOrZero();

        try
        {
            if (this.socket.Connected)
                this.socket.Shutdown(SocketShutdown.Both);
        }
        catch
        {
            // ignored
        }

        try
        {
            if (timeout > 0)
                this.socket.Close(timeout);
            else
                this.socket.Close();

            this.socket.Dispose();
        }
        catch
        {
            // ignored
        }

        if (this.dispose is Func<ValueTask> func)
            await func();
    }
}
