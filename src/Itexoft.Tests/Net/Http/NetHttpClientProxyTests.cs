// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Itexoft.IO;
using Itexoft.Net;
using Itexoft.Net.Core;
using Itexoft.Net.Dns;
using Itexoft.Net.Http;
using Itexoft.Net.Proxies;
using Itexoft.Threading;

namespace Itexoft.Tests.Net.Http;

public sealed class NetHttpClientProxyTests
{
    private static readonly object timeoutSource = new();
    private static CancelToken CreateTimeoutToken(TimeSpan timeout) => new CancelToken(timeoutSource).Branch(timeout);

    [Test]
    public async Task HttpProxy_TunnelsRequest()
    {
        var token = CreateTimeoutToken(TimeSpan.FromSeconds(10));
        var target = await HttpTargetServer.StartAsync("ok", token).ConfigureAwait(false);
        await using var target1 = target.ConfigureAwait(false);
        var proxy = await HttpConnectProxyServer.StartAsync(target.EndPoint, token).ConfigureAwait(false);
        await using var proxy1 = proxy.ConfigureAwait(false);
        var dnsHost = new NetDnsHost("127.0.0.1", NetDnsResolver.Default);
        var client = new NetHttpClient(new NetEndpoint(dnsHost, target.EndPoint.Port), new NetHttpProxy("127.0.0.1", proxy.EndPoint.Port));
        await using var client1 = client.ConfigureAwait(false);

        var response = await client.GetAsync("/proxy", token).ConfigureAwait(false);
        await using var response1 = response.ConfigureAwait(false);
        var text = await response.ReadAsStringAsync().ConfigureAwait(false);

        Assert.That(text, Is.EqualTo("ok"));
        var (host, port) = ParseConnectTarget(proxy.LastConnectRequest);
        Assert.That(host, Is.EqualTo("127.0.0.1"));
        Assert.That(port, Is.EqualTo(target.EndPoint.Port));
    }

    [Test]
    public async Task HttpsProxy_TunnelsRequest()
    {
        var token = CreateTimeoutToken(TimeSpan.FromSeconds(10));
        var target = await HttpTargetServer.StartAsync("secure", token).ConfigureAwait(false);
        await using var target1 = target.ConfigureAwait(false);
        var proxy = await HttpsConnectProxyServer.StartAsync(target.EndPoint, token).ConfigureAwait(false);
        await using var proxy1 = proxy.ConfigureAwait(false);
        var client = new NetHttpClient(new NetEndpoint("127.0.0.1", target.EndPoint.Port), new NetHttpsProxy("127.0.0.1", proxy.EndPoint.Port));
        await using var client1 = client.ConfigureAwait(false);

        var response = await client.GetAsync("/proxy-secure", token).ConfigureAwait(false);
        await using var response1 = response.ConfigureAwait(false);
        var text = await response.ReadAsStringAsync().ConfigureAwait(false);

        Assert.That(text, Is.EqualTo("secure"));
        var (host, port) = ParseConnectTarget(proxy.LastConnectRequest);
        Assert.That(host, Is.EqualTo("127.0.0.1"));
        Assert.That(port, Is.EqualTo(target.EndPoint.Port));
    }

    [Test]
    public async Task Socks4Proxy_TunnelsRequest()
    {
        var token = CreateTimeoutToken(TimeSpan.FromSeconds(10));
        var target = await HttpTargetServer.StartAsync("socks4", token).ConfigureAwait(false);
        await using var target1 = target.ConfigureAwait(false);
        var proxy = await Socks4ConnectProxyServer.StartAsync(target.EndPoint, token).ConfigureAwait(false);
        await using var proxy1 = proxy.ConfigureAwait(false);
        var client = new NetHttpClient(new NetEndpoint("127.0.0.1", target.EndPoint.Port), new NetSocks4Proxy("127.0.0.1", proxy.EndPoint.Port));
        await using var client1 = client.ConfigureAwait(false);

        var response = await client.GetAsync("/proxy-s4", token).ConfigureAwait(false);
        await using var response1 = response.ConfigureAwait(false);
        var text = await response.ReadAsStringAsync().ConfigureAwait(false);

        Assert.That(text, Is.EqualTo("socks4"));
        Assert.That(ParseSocks4Port(proxy.LastRequest), Is.EqualTo(target.EndPoint.Port));
    }

    [Test]
    public async Task Socks5Proxy_TunnelsRequest()
    {
        var token = CreateTimeoutToken(TimeSpan.FromSeconds(10));
        var target = await HttpTargetServer.StartAsync("socks5", token).ConfigureAwait(false);
        await using var target1 = target.ConfigureAwait(false);
        var proxy = await Socks5ConnectProxyServer.StartAsync(target.EndPoint, token).ConfigureAwait(false);
        await using var proxy1 = proxy.ConfigureAwait(false);
        var client = new NetHttpClient(new NetEndpoint("127.0.0.1", target.EndPoint.Port), new NetSocks5Proxy("127.0.0.1", proxy.EndPoint.Port));
        await using var client1 = client.ConfigureAwait(false);

        var response = await client.GetAsync("/proxy-s5", token).ConfigureAwait(false);
        await using var response1 = response.ConfigureAwait(false);
        var text = await response.ReadAsStringAsync().ConfigureAwait(false);

        Assert.That(text, Is.EqualTo("socks5"));
        Assert.That(ParseSocks5Port(proxy.ConnectRequest), Is.EqualTo(target.EndPoint.Port));
    }

    private static async Task<string> ReadHeadersAsync(IStreamRa stream, CancelToken cancelToken)
    {
        using var ms = new MemoryStream();
        var buffer = new byte[1];
        var match = 0;

        while (true)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, 1), cancelToken).ConfigureAwait(false);

            if (read == 0)
                throw new IOException("Unexpected end of stream.");

            ms.WriteByte(buffer[0]);

            match = buffer[0] switch
            {
                (byte)'\r' when match is 0 or 2 => match + 1,
                (byte)'\n' when match is 1 or 3 => match + 1,
                _ => 0,
            };

            if (match == 4)
                break;

            if (ms.Length > 32768)
                throw new IOException("Headers too large.");
        }

        return Encoding.ASCII.GetString(ms.GetBuffer(), 0, (int)ms.Length);
    }

    private static (string Host, int Port) ParseConnectTarget(string? request)
    {
        if (string.IsNullOrEmpty(request))
            throw new IOException("Missing CONNECT request.");

        var span = request.AsSpan();
        var lineEnd = span.IndexOf("\r\n".AsSpan());
        var line = lineEnd >= 0 ? span[..lineEnd] : span;

        const string prefix = "CONNECT ";

        if (!line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Invalid CONNECT request.");

        var target = line[prefix.Length..];
        var space = target.IndexOf(' ');

        if (space >= 0)
            target = target[..space];

        var colon = target.LastIndexOf(':');

        if (colon <= 0)
            throw new IOException("Invalid CONNECT target.");

        var host = target[..colon].ToString();
        var port = int.Parse(target[(colon + 1)..]);

        return (host, port);
    }

    private static int ParseSocks4Port(byte[]? request)
    {
        if (request is null || request.Length < 4)
            throw new IOException("Missing SOCKS4 request.");

        return (request[2] << 8) | request[3];
    }

    private static int ParseSocks5Port(byte[]? request)
    {
        if (request is null || request.Length < 6)
            throw new IOException("Missing SOCKS5 request.");

        var portOffset = request.Length - 2;

        return (request[portOffset] << 8) | request[portOffset + 1];
    }

    private static async Task ReadExactAsync(IStreamRa stream, Memory<byte> buffer, CancelToken cancelToken)
    {
        var offset = 0;

        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer[offset..], cancelToken).ConfigureAwait(false);

            if (read == 0)
                throw new IOException("Connection closed.");

            offset += read;
        }
    }

    private static async Task RelayAsync(IStreamRa source, IStreamRwa destination, CancelToken cancelToken)
    {
        var buffer = new byte[8192];

        while (true)
        {
            var read = await source.ReadAsync(buffer.AsMemory(), cancelToken).ConfigureAwait(false);

            if (read == 0)
                break;

            await destination.WriteAsync(buffer.AsMemory(0, read), cancelToken).ConfigureAwait(false);
        }
    }

    private static async Task BridgeAsync(IStreamRwa clientStream, IPEndPoint target, CancelToken cancelToken)
    {
        using var targetClient = new TcpClient();

        using (cancelToken.Bridge(out var token))
        {
            await targetClient.ConnectAsync(target.Address, target.Port, token).ConfigureAwait(false);
            var targetStream = targetClient.GetStream().AsBclStream();
            await using var stream = targetStream.ConfigureAwait(false);

            var relay1 = RelayAsync(clientStream, targetStream, cancelToken);
            var relay2 = RelayAsync(targetStream, clientStream, cancelToken);

            _ = await Task.WhenAny(relay1, relay2).ConfigureAwait(false);


            try
            {
                await clientStream.DisposeAsync().ConfigureAwait(false);
            }
            catch { }

            try
            {
                await targetStream.DisposeAsync().ConfigureAwait(false);
            }
            catch { }

            try
            {
                await Task.WhenAll(relay1, relay2).ConfigureAwait(false);
            }
            catch { }
        }
    }

    private sealed class HttpTargetServer : IAsyncDisposable
    {
        private readonly string body;
        private readonly TcpListener listener;
        private Task serverTask;

        private HttpTargetServer(TcpListener listener, string body, Task serverTask)
        {
            this.listener = listener;
            this.body = body;
            this.serverTask = serverTask;
            this.EndPoint = (IPEndPoint)listener.LocalEndpoint;
        }

        public IPEndPoint EndPoint { get; }
        public string? LastRequest { get; private set; }

        public async ValueTask DisposeAsync()
        {
            this.listener.Stop();

            try
            {
                await this.serverTask.ConfigureAwait(false);
            }
            catch { }
        }

        public static Task<HttpTargetServer> StartAsync(string body, CancelToken cancelToken)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var server = new HttpTargetServer(listener, body, Task.CompletedTask);
            var task = runAsync(server, cancelToken);
            server.serverTask = task;

            return Task.FromResult(server);

            static async Task runAsync(HttpTargetServer server, CancelToken cancelToken)
            {
                using (cancelToken.Bridge(out var token))
                {
                    try
                    {
                        using var client = await server.listener.AcceptTcpClientAsync(token).ConfigureAwait(false);
                        var stream = client.GetStream().AsBclStream();
                        await using var stream1 = stream.ConfigureAwait(false);
                        var request = await ReadHeadersAsync(stream, cancelToken).ConfigureAwait(false);
                        server.LastRequest = request;

                        var payload = Encoding.ASCII.GetBytes(server.body);
                        var header = $"HTTP/1.1 200 OK\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n\r\n";
                        var headerBytes = Encoding.ASCII.GetBytes(header);
                        await stream.WriteAsync(headerBytes, cancelToken).ConfigureAwait(false);
                        await stream.WriteAsync(payload, cancelToken).ConfigureAwait(false);
                        await stream.FlushAsync(cancelToken).ConfigureAwait(false);
                    }
                    catch
                    {
                        // ignored
                    }
                }
            }
        }
    }

    private sealed class HttpConnectProxyServer : IAsyncDisposable
    {
        private readonly TcpListener listener;
        private Task serverTask;

        private HttpConnectProxyServer(TcpListener listener, Task serverTask)
        {
            this.listener = listener;
            this.serverTask = serverTask;
            this.EndPoint = (IPEndPoint)listener.LocalEndpoint;
        }

        public IPEndPoint EndPoint { get; }
        public string? LastConnectRequest { get; private set; }

        public async ValueTask DisposeAsync()
        {
            this.listener.Stop();

            try
            {
                await this.serverTask.ConfigureAwait(false);
            }
            catch { }
        }

        public static Task<HttpConnectProxyServer> StartAsync(IPEndPoint target, CancelToken cancelToken)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var server = new HttpConnectProxyServer(listener, Task.CompletedTask);
            var task = runAsync(server, target, cancelToken);
            server.serverTask = task;

            return Task.FromResult(server);

            static async Task runAsync(HttpConnectProxyServer server, IPEndPoint target, CancelToken cancelToken)
            {
                using (cancelToken.Bridge(out var token))
                {
                    try
                    {
                        using var client = await server.listener.AcceptTcpClientAsync(token).ConfigureAwait(false);
                        var stream = client.GetStream().AsBclStream();
                        await using var stream1 = stream.ConfigureAwait(false);
                        var connectRequest = await ReadHeadersAsync(stream, cancelToken).ConfigureAwait(false);
                        server.LastConnectRequest = connectRequest;

                        var ok = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\n\r\n");
                        await stream.WriteAsync(ok, cancelToken).ConfigureAwait(false);
                        await stream.FlushAsync(cancelToken).ConfigureAwait(false);

                        await BridgeAsync(stream, target, cancelToken).ConfigureAwait(false);
                    }
                    catch
                    {
                        // ignored
                    }
                }
            }
        }
    }

    private sealed class HttpsConnectProxyServer : IAsyncDisposable
    {
        private readonly X509Certificate2 certificate;
        private readonly TcpListener listener;
        private Task serverTask;

        private HttpsConnectProxyServer(TcpListener listener, Task serverTask, X509Certificate2 certificate)
        {
            this.listener = listener;
            this.serverTask = serverTask;
            this.certificate = certificate;
            this.EndPoint = (IPEndPoint)listener.LocalEndpoint;
        }

        public IPEndPoint EndPoint { get; }
        public string? LastConnectRequest { get; private set; }

        public async ValueTask DisposeAsync()
        {
            this.listener.Stop();

            try
            {
                await this.serverTask.ConfigureAwait(false);
            }
            catch { }

            this.certificate.Dispose();
        }

        public static Task<HttpsConnectProxyServer> StartAsync(IPEndPoint target, CancelToken cancelToken)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var certificate = CreateCertificate();
            var server = new HttpsConnectProxyServer(listener, Task.CompletedTask, certificate);
            var task = runAsync(server, target, cancelToken);
            server.serverTask = task;

            return Task.FromResult(server);

            static async Task runAsync(HttpsConnectProxyServer server, IPEndPoint target, CancelToken cancelToken)
            {
                try
                {
                    using (cancelToken.Bridge(out var token))
                    {
                        using var client = await server.listener.AcceptTcpClientAsync(token).ConfigureAwait(false);
                        var rawStream = client.GetStream().AsINetStream();
                        await using var stream = rawStream.ConfigureAwait(false);
                        var sslStream = new NetSslStream(rawStream, false);
                        await using var sslStream1 = sslStream.ConfigureAwait(false);
                        await sslStream.AuthenticateAsServerAsync(server.certificate, false, SslProtocols.None, false).ConfigureAwait(false);

                        var connectRequest = await ReadHeadersAsync(sslStream, cancelToken).ConfigureAwait(false);
                        server.LastConnectRequest = connectRequest;

                        var ok = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\n\r\n");
                        await sslStream.WriteAsync(ok, cancelToken).ConfigureAwait(false);
                        await sslStream.FlushAsync(cancelToken).ConfigureAwait(false);

                        await BridgeAsync(sslStream, target, cancelToken).ConfigureAwait(false);
                    }
                }
                catch
                {
                    // ignored
                }
            }
        }

        private static X509Certificate2 CreateCertificate()
        {
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
            request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.1") }, false));

            request.CertificateExtensions.Add(
                new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));

            var san = new SubjectAlternativeNameBuilder();
            san.AddDnsName("localhost");
            san.AddIpAddress(IPAddress.Loopback);
            request.CertificateExtensions.Add(san.Build());

            return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(1));
        }
    }

    private sealed class Socks4ConnectProxyServer : IAsyncDisposable
    {
        private readonly TcpListener listener;
        private Task serverTask;

        private Socks4ConnectProxyServer(TcpListener listener, Task serverTask)
        {
            this.listener = listener;
            this.serverTask = serverTask;
            this.EndPoint = (IPEndPoint)listener.LocalEndpoint;
        }

        public IPEndPoint EndPoint { get; }
        public byte[]? LastRequest { get; private set; }

        public async ValueTask DisposeAsync()
        {
            this.listener.Stop();

            try
            {
                await this.serverTask.ConfigureAwait(false);
            }
            catch { }
        }

        public static Task<Socks4ConnectProxyServer> StartAsync(IPEndPoint target, CancelToken cancelToken)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var server = new Socks4ConnectProxyServer(listener, Task.CompletedTask);
            var task = runAsync(server, target, cancelToken);
            server.serverTask = task;

            return Task.FromResult(server);

            static async Task runAsync(Socks4ConnectProxyServer server, IPEndPoint target, CancelToken cancelToken)
            {
                try
                {
                    using (cancelToken.Bridge(out var token))
                    {
                        using var client = await server.listener.AcceptTcpClientAsync(token).ConfigureAwait(false);
                        var stream = client.GetStream().AsBclStream();
                        await using var stream1 = stream.ConfigureAwait(false);

                        var request = await ReadSocks4RequestAsync(stream, cancelToken).ConfigureAwait(false);
                        server.LastRequest = request;

                        var response = new byte[8];
                        response[1] = 0x5A;
                        await stream.WriteAsync(response.AsMemory(0, response.Length), cancelToken).ConfigureAwait(false);
                        await stream.FlushAsync(cancelToken).ConfigureAwait(false);

                        await BridgeAsync(stream, target, cancelToken).ConfigureAwait(false);
                    }
                }
                catch
                {
                    // ignored
                }
            }
        }

        private static async Task<byte[]> ReadSocks4RequestAsync(IStreamRa stream, CancelToken cancelToken)
        {
            var head = new byte[8];
            await ReadExactAsync(stream, head.AsMemory(), cancelToken).ConfigureAwait(false);
            var request = new List<byte>(head);

            var buffer = new byte[1];

            while (true)
            {
                await ReadExactAsync(stream, buffer.AsMemory(0, 1), cancelToken).ConfigureAwait(false);
                request.Add(buffer[0]);

                if (buffer[0] == 0x00)
                    break;
            }

            if (head[4] == 0 && head[5] == 0 && head[6] == 0 && head[7] == 1)
            {
                while (true)
                {
                    await ReadExactAsync(stream, buffer.AsMemory(0, 1), cancelToken).ConfigureAwait(false);
                    request.Add(buffer[0]);

                    if (buffer[0] == 0x00)
                        break;
                }
            }

            return request.ToArray();
        }
    }

    private sealed class Socks5ConnectProxyServer : IAsyncDisposable
    {
        private readonly TcpListener listener;
        private Task serverTask;

        private Socks5ConnectProxyServer(TcpListener listener, Task serverTask)
        {
            this.listener = listener;
            this.serverTask = serverTask;
            this.EndPoint = (IPEndPoint)listener.LocalEndpoint;
        }

        public IPEndPoint EndPoint { get; }
        public byte[]? ConnectRequest { get; private set; }

        public async ValueTask DisposeAsync()
        {
            this.listener.Stop();

            try
            {
                await this.serverTask.ConfigureAwait(false);
            }
            catch { }
        }

        public static Task<Socks5ConnectProxyServer> StartAsync(IPEndPoint target, CancelToken cancelToken)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var server = new Socks5ConnectProxyServer(listener, Task.CompletedTask);
            var task = runAsync(server, target, cancelToken);
            server.serverTask = task;

            return Task.FromResult(server);

            static async Task runAsync(Socks5ConnectProxyServer server, IPEndPoint target, CancelToken cancelToken)
            {
                try
                {
                    using (cancelToken.Bridge(out var token))
                    {
                        using var client = await server.listener.AcceptTcpClientAsync(token).ConfigureAwait(false);
                        var stream = client.GetStream().AsBclStream();
                        await using var stream1 = stream.ConfigureAwait(false);

                        var greeting = new byte[2];
                        await ReadExactAsync(stream, greeting.AsMemory(), cancelToken).ConfigureAwait(false);

                        if (greeting[0] != 0x05)
                            throw new IOException("Invalid SOCKS5 greeting.");

                        if (greeting[1] > 0)
                        {
                            var methods = new byte[greeting[1]];
                            await ReadExactAsync(stream, methods.AsMemory(), cancelToken).ConfigureAwait(false);
                        }

                        await stream.WriteAsync(new byte[] { 0x05, 0x00 }, cancelToken).ConfigureAwait(false);
                        await stream.FlushAsync(cancelToken).ConfigureAwait(false);

                        var head = new byte[4];
                        await ReadExactAsync(stream, head.AsMemory(), cancelToken).ConfigureAwait(false);

                        var address = head[3] switch
                        {
                            0x01 => await ReadAddressAsync(stream, 4, cancelToken).ConfigureAwait(false),
                            0x04 => await ReadAddressAsync(stream, 16, cancelToken).ConfigureAwait(false),
                            0x03 => await ReadDomainAsync(stream, cancelToken).ConfigureAwait(false),
                            _ => throw new IOException("Unsupported ATYP."),
                        };

                        var portBytes = new byte[2];
                        await ReadExactAsync(stream, portBytes.AsMemory(), cancelToken).ConfigureAwait(false);

                        var request = new byte[4 + address.Length + 2];
                        head.CopyTo(request, 0);
                        address.CopyTo(request, 4);
                        portBytes.CopyTo(request, 4 + address.Length);
                        server.ConnectRequest = request;

                        var response = new byte[4 + address.Length + 2];
                        response[0] = 0x05;
                        response[1] = 0x00;
                        response[2] = 0x00;
                        response[3] = head[3];
                        address.CopyTo(response, 4);
                        portBytes.CopyTo(response, 4 + address.Length);
                        await stream.WriteAsync(response.AsMemory(0, response.Length), cancelToken).ConfigureAwait(false);
                        await stream.FlushAsync(cancelToken).ConfigureAwait(false);

                        await BridgeAsync(stream, target, cancelToken).ConfigureAwait(false);
                    }
                }
                catch
                {
                    // ignored
                }
            }
        }

        private static async Task<byte[]> ReadAddressAsync(IStreamRa stream, int length, CancelToken cancelToken)
        {
            var buffer = new byte[length];
            await ReadExactAsync(stream, buffer.AsMemory(), cancelToken).ConfigureAwait(false);

            return buffer;
        }

        private static async Task<byte[]> ReadDomainAsync(IStreamRa stream, CancelToken cancelToken)
        {
            var lenBuffer = new byte[1];
            await ReadExactAsync(stream, lenBuffer.AsMemory(), cancelToken).ConfigureAwait(false);
            var buffer = new byte[lenBuffer[0]];
            await ReadExactAsync(stream, buffer.AsMemory(), cancelToken).ConfigureAwait(false);

            return [lenBuffer[0], .. buffer];
        }
    }
}
