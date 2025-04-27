// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Net;
using System.Net.Security;
using System.Net.Sockets;

namespace Itexoft.Networking.P2P.Adapters;

/// <summary>
/// TCP-адаптер: открывает отдельные соединения для данных и контрольного канала.
/// </summary>
public sealed class TcpP2PTransportAdapter : IP2PTransportAdapter
{
    private readonly TcpP2PTransportAdapterOptions _options;

    public TcpP2PTransportAdapter(TcpP2PTransportAdapterOptions? options = null) =>
        this._options = options ?? new TcpP2PTransportAdapterOptions();

    public async Task<P2PTransportConnection> ConnectAsync(
        P2PTransportEndpoint endpoint,
        P2PConnectionOptions options,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(endpoint.Transport, P2PTransportNames.Tcp, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Endpoint transport '{endpoint.Transport}' не поддерживается TCP-адаптером.");

        var sessionId = Guid.NewGuid();
        Socket? dataSocket = null;
        NetworkStream? dataNetworkStream = null;
        Stream? dataStream = null;
        Socket? controlSocket = null;
        NetworkStream? controlNetworkStream = null;
        Stream? controlStream = null;
        try
        {
            dataSocket = this.CreateSocket(endpoint);
            await this.ConnectAsync(dataSocket, endpoint, cancellationToken).ConfigureAwait(false);
            await TcpP2PChannelHeader.SendAsync(dataSocket, sessionId, TcpP2PChannel.Data, cancellationToken).ConfigureAwait(false);
            dataNetworkStream = new(dataSocket, false);
            dataStream = await this.WrapStreamAsync(dataNetworkStream, endpoint, false, cancellationToken).ConfigureAwait(false);

            controlSocket = this.CreateSocket(endpoint);
            await this.ConnectAsync(controlSocket, endpoint, cancellationToken).ConfigureAwait(false);
            await TcpP2PChannelHeader.SendAsync(controlSocket, sessionId, TcpP2PChannel.Control, cancellationToken).ConfigureAwait(false);
            controlNetworkStream = new(controlSocket, false);
            controlStream = await this.WrapStreamAsync(controlNetworkStream, endpoint, true, cancellationToken).ConfigureAwait(false);
            var controlChannel = new SimpleStreamControlChannel(controlStream);

            async ValueTask OnDisposeAsync()
            {
                if (controlStream is IAsyncDisposable asyncDisposable)
                    await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                else
                    controlStream.Dispose();
                controlSocket!.CloseGracefully();
            }

            return new(
                dataStream,
                controlChannel,
                dataSocket.RemoteEndPoint,
                dataSocket,
                OnDisposeAsync)
            {
                TransportCancellationToken = cancellationToken
            };
        }
        catch
        {
            controlStream?.Dispose();
            controlNetworkStream?.Dispose();
            controlSocket?.CloseGracefully();
            dataStream?.Dispose();
            dataNetworkStream?.Dispose();
            dataSocket?.CloseGracefully();

            throw;
        }
    }

    private Socket CreateSocket(P2PTransportEndpoint endpoint)
    {
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
        if (this._options.NoDelay.HasValue)
            socket.NoDelay = this._options.NoDelay.Value;

        if (this._options.SendBufferSize.HasValue)
            socket.SendBufferSize = this._options.SendBufferSize.Value;

        if (this._options.ReceiveBufferSize.HasValue)
            socket.ReceiveBufferSize = this._options.ReceiveBufferSize.Value;

        this._options.ConfigureSocket?.Invoke(socket);

        return socket;
    }

    private async Task ConnectAsync(Socket socket, P2PTransportEndpoint endpoint, CancellationToken cancellationToken)
    {
        var endPoint = new DnsEndPoint(endpoint.Host, endpoint.Port);
        await socket.ConnectAsync(endPoint, cancellationToken).ConfigureAwait(false);
        this._options.ConfigureSocket?.Invoke(socket);
        this.ApplyKeepAlive(socket);
    }

    private async ValueTask<Stream> WrapStreamAsync(
        NetworkStream baseStream,
        P2PTransportEndpoint endpoint,
        bool useControlChannel,
        CancellationToken cancellationToken)
    {
        if (!endpoint.UseTls)
            return baseStream;

        if (useControlChannel && !this._options.EnableControlChannelTls)
            return baseStream;

        var sslStream = new SslStream(
            baseStream,
            false,
            this._options.RemoteCertificateValidationCallback ?? ((_, __, ___, ____) => true),
            this._options.ClientCertificateSelectionCallback);

        var sslOptions = this._options.CreateSslOptions?.Invoke(endpoint, useControlChannel)
                         ?? new SslClientAuthenticationOptions
                         {
                             TargetHost = endpoint.Host
                         };

        await sslStream.AuthenticateAsClientAsync(sslOptions, cancellationToken).ConfigureAwait(false);

        return sslStream;
    }

    private void ApplyKeepAlive(Socket socket)
    {
        if (!this._options.EnableKeepAlive)
            return;

        try
        {
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);

#if NET6_0_OR_GREATER
            if (this._options.KeepAliveTime.HasValue)
                socket.SetSocketOption(
                    SocketOptionLevel.Tcp,
                    SocketOptionName.TcpKeepAliveTime,
                    (int)this._options.KeepAliveTime.Value.TotalSeconds);

            if (this._options.KeepAliveInterval.HasValue)
                socket.SetSocketOption(
                    SocketOptionLevel.Tcp,
                    SocketOptionName.TcpKeepAliveInterval,
                    (int)this._options.KeepAliveInterval.Value.TotalSeconds);

            if (this._options.KeepAliveRetryCount.HasValue)
                socket.SetSocketOption(
                    SocketOptionLevel.Tcp,
                    SocketOptionName.TcpKeepAliveRetryCount,
                    this._options.KeepAliveRetryCount.Value);
#else
            TryApplyKeepAliveViaIoControl(socket);
#endif
        }
        catch (SocketException)
        {
            this.TryApplyKeepAliveViaIoControl(socket);
        }
        catch (PlatformNotSupportedException)
        {
            this.TryApplyKeepAliveViaIoControl(socket);
        }
    }

    private void TryApplyKeepAliveViaIoControl(Socket socket)
    {
#if !NET6_0_OR_GREATER
        try
        {
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);

            var keepAliveTime = (uint)(_options.KeepAliveTime?.TotalMilliseconds ?? TimeSpan.FromSeconds(10).TotalMilliseconds);
            var keepAliveInterval = (uint)(_options.KeepAliveInterval?.TotalMilliseconds ?? TimeSpan.FromSeconds(2).TotalMilliseconds);

            var buffer = new byte[3 * sizeof(uint)];
            BitConverter.GetBytes((uint)1).CopyTo(buffer, 0);
            BitConverter.GetBytes(keepAliveTime).CopyTo(buffer, sizeof(uint));
            BitConverter.GetBytes(keepAliveInterval).CopyTo(buffer, sizeof(uint) * 2);

            socket.IOControl(IOControlCode.KeepAliveValues, buffer, null);
        }
        catch
        {
            // ignored
        }
#else
        _ = socket;
#endif
    }
}

public sealed class TcpP2PTransportAdapterOptions
{
    /// <summary>
    /// Управление TCP_NODELAY (true по умолчанию).
    /// </summary>
    public bool? NoDelay { get; set; } = true;

    /// <summary>
    /// Включать ли TLS для контрольного канала (если основной поток шифруется).
    /// </summary>
    public bool EnableControlChannelTls { get; set; } = true;

    /// <summary>
    /// Фабрика параметров TLS (bool указывает control-канал или data).
    /// </summary>
    public Func<P2PTransportEndpoint, bool, SslClientAuthenticationOptions>? CreateSslOptions { get; set; }

    public RemoteCertificateValidationCallback? RemoteCertificateValidationCallback { get; set; }

    public LocalCertificateSelectionCallback? ClientCertificateSelectionCallback { get; set; }

    /// <summary>
    /// Позволяет дополнительно настроить сокет после подключения.
    /// </summary>
    public Action<Socket>? ConfigureSocket { get; set; }

    /// <summary>
    /// Размер буфера отправки (по умолчанию берется из ОС).
    /// </summary>
    public int? SendBufferSize { get; set; }

    /// <summary>
    /// Размер буфера приема (по умолчанию берется из ОС).
    /// </summary>
    public int? ReceiveBufferSize { get; set; }

    /// <summary>
    /// Включить ли TCP keep-alive (по умолчанию включен).
    /// </summary>
    public bool EnableKeepAlive { get; set; } = true;

    /// <summary>
    /// Начальное время бездействия перед первой keep-alive проверкой.
    /// </summary>
    public TimeSpan? KeepAliveTime { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Интервал между keep-alive пакетами.
    /// </summary>
    public TimeSpan? KeepAliveInterval { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Количество повторов keep-alive перед признанием соединения мёртвым (если поддерживается платформой).
    /// </summary>
    public int? KeepAliveRetryCount { get; set; } = 5;
}