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
using Itexoft.Net.Core;
using Itexoft.Threading;

namespace Itexoft.Tests.Net.Proxies;

internal static class ProxyTestServers
{
    public static Task<HttpProxyServer> StartHttpAsync(Func<string, string>? responder = null) =>
        HttpProxyServer.StartAsync(responder ?? (_ => "HTTP/1.1 200 OK\r\nHeader: v\r\n\r\n"));

    public static Task<HttpsProxyServer> StartHttpsAsync(Func<string, string>? responder = null) =>
        HttpsProxyServer.StartAsync(responder ?? (_ => "HTTP/1.1 200 OK\r\nHeader: v\r\n\r\n"));

    public static Task<Socks4ProxyServer> StartSocks4Async(byte replyCode = 0x5A) =>
        Socks4ProxyServer.StartAsync(replyCode);

    public static Task<Socks5ProxyServer> StartSocks5Async(byte method = 0x00, byte replyCode = 0x00) =>
        Socks5ProxyServer.StartAsync(method, replyCode);

    private static async Task<int> ReadExactAsync(INetStream stream, byte[] buffer, int length, CancelToken cancelToken)
    {
        using (cancelToken.Bridge(out var token))
        {
            var offset = 0;

            while (offset < length)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(offset, length - offset), token).ConfigureAwait(false);

                if (read == 0)
                    throw new IOException("Connection closed");

                offset += read;
            }

            return offset;
        }
    }

    internal sealed class HttpProxyServer : IAsyncDisposable
    {
        private readonly CancellationTokenSource cts;
        private readonly TcpListener listener;
        private readonly Func<string, string> responder;
        private Task serverTask;

        private HttpProxyServer(TcpListener listener, CancellationTokenSource cts, Task serverTask, Func<string, string> responder)
        {
            this.listener = listener;
            this.cts = cts;
            this.serverTask = serverTask;
            this.responder = responder;
            this.EndPoint = (IPEndPoint)this.listener.LocalEndpoint;
        }

        public IPEndPoint EndPoint { get; }
        public string? LastRequest { get; private set; }

        public async ValueTask DisposeAsync()
        {
            await this.cts.CancelAsync().ConfigureAwait(false);
            this.listener.Stop();

            try
            {
                await this.serverTask.ConfigureAwait(false);
            }
            catch { }

            this.cts.Dispose();
        }

        public static Task<HttpProxyServer> StartAsync(Func<string, string> responder)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var server = new HttpProxyServer(listener, cts, Task.CompletedTask, responder);
            var task = runAsync(server, cts.Token);
            server.serverTask = task;

            return Task.FromResult(server);

            static async Task runAsync(HttpProxyServer server, CancelToken cancelToken)
            {
                using (cancelToken.Bridge(out var token))
                {
                    using var client = await server.listener.AcceptTcpClientAsync(token).ConfigureAwait(false);
                    var stream = client.GetStream().AsINetStream();
                    await using var stream1 = stream.ConfigureAwait(false);

                    var request = await ReadHeadersAsync(stream, cancelToken).ConfigureAwait(false);
                    server.LastRequest = request;

                    var response = server.responder(request);
                    var bytes = Encoding.ASCII.GetBytes(response);
                    await stream.WriteAsync(bytes, cancelToken).ConfigureAwait(false);
                    await stream.FlushAsync(cancelToken).ConfigureAwait(false);
                }
            }
        }

        internal static async Task<string> ReadHeadersAsync(INetStream stream, CancelToken cancelToken)
        {
            using var ms = new MemoryStream();
            var buffer = new byte[1];
            var crlfMatch = 0;
            var lfMatch = 0;

            while (true)
            {
                await ReadExactAsync(stream, buffer, 1, cancelToken).ConfigureAwait(false);
                ms.WriteByte(buffer[0]);

                crlfMatch = buffer[0] switch
                {
                    (byte)'\r' when crlfMatch is 0 or 2 => crlfMatch + 1,
                    (byte)'\n' when crlfMatch is 1 or 3 => crlfMatch + 1,
                    _ => 0,
                };

                lfMatch = buffer[0] == (byte)'\n' ? lfMatch + 1 : 0;

                if (crlfMatch == 4 || lfMatch == 2)
                    break;

                if (ms.Length > 16384)
                    throw new IOException("Request too large");
            }

            var data = ms.ToArray();

            return Encoding.ASCII.GetString(data);
        }
    }

    internal sealed class HttpsProxyServer : IAsyncDisposable
    {
        private readonly X509Certificate2 certificate;
        private readonly CancellationTokenSource cts;
        private readonly TcpListener listener;
        private readonly Func<string, string> responder;
        private Task serverTask;

        private HttpsProxyServer(
            TcpListener listener,
            CancellationTokenSource cts,
            Task serverTask,
            Func<string, string> responder,
            X509Certificate2 certificate)
        {
            this.listener = listener;
            this.cts = cts;
            this.serverTask = serverTask;
            this.responder = responder;
            this.certificate = certificate;
            this.EndPoint = (IPEndPoint)this.listener.LocalEndpoint;
        }

        public IPEndPoint EndPoint { get; }
        public string? LastRequest { get; private set; }

        public async ValueTask DisposeAsync()
        {
            await this.cts.CancelAsync().ConfigureAwait(false);
            this.listener.Stop();

            try
            {
                await this.serverTask.ConfigureAwait(false);
            }
            catch { }

            this.certificate.Dispose();
            this.cts.Dispose();
        }

        public static Task<HttpsProxyServer> StartAsync(Func<string, string> responder)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var certificate = CreateCertificate();
            var server = new HttpsProxyServer(listener, cts, Task.CompletedTask, responder, certificate);
            var task = runAsync(server, cts.Token);
            server.serverTask = task;

            return Task.FromResult(server);

            static async Task runAsync(HttpsProxyServer server, CancelToken cancelToken)
            {
                using (cancelToken.Bridge(out var token))
                {
                    using var client = await server.listener.AcceptTcpClientAsync(token).ConfigureAwait(false);
                    var rawStream = client.GetStream().AsINetStream();
                    await using var stream = rawStream.ConfigureAwait(false);
                    await using var sslStream = new NetSslStream(rawStream, false);
                    await sslStream.AuthenticateAsServerAsync(server.certificate, false, SslProtocols.None, false).ConfigureAwait(false);

                    var request = await HttpProxyServer.ReadHeadersAsync(sslStream, cancelToken).ConfigureAwait(false);
                    server.LastRequest = request;

                    var response = server.responder(request);
                    var bytes = Encoding.ASCII.GetBytes(response);
                    await sslStream.WriteAsync(bytes, cancelToken).ConfigureAwait(false);
                    await sslStream.FlushAsync(cancelToken).ConfigureAwait(false);
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

            var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(1));

            return cert;
        }
    }

    internal sealed class Socks4ProxyServer : IAsyncDisposable
    {
        private readonly CancellationTokenSource cts;
        private readonly TcpListener listener;
        private readonly byte replyCode;
        private Task serverTask;

        private Socks4ProxyServer(TcpListener listener, CancellationTokenSource cts, Task serverTask, byte replyCode)
        {
            this.listener = listener;
            this.cts = cts;
            this.serverTask = serverTask;
            this.replyCode = replyCode;
            this.EndPoint = (IPEndPoint)this.listener.LocalEndpoint;
        }

        public IPEndPoint EndPoint { get; }
        public byte[]? LastRequest { get; private set; }

        public async ValueTask DisposeAsync()
        {
            await this.cts.CancelAsync().ConfigureAwait(false);
            this.listener.Stop();

            try
            {
                await this.serverTask.ConfigureAwait(false);
            }
            catch { }

            this.cts.Dispose();
        }

        public static Task<Socks4ProxyServer> StartAsync(byte replyCode)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var server = new Socks4ProxyServer(listener, cts, Task.CompletedTask, replyCode);
            server.serverTask = runAsync(server, cts.Token);

            return Task.FromResult(server);

            static async Task runAsync(Socks4ProxyServer server, CancelToken cancelToken)
            {
                using (cancelToken.Bridge(out var token))
                {
                    using var client = await server.listener.AcceptTcpClientAsync(token).ConfigureAwait(false);
                    var stream = client.GetStream().AsINetStream();
                    await using var stream1 = stream.ConfigureAwait(false);

                    var head = new byte[8];
                    await ReadExactAsync(stream, head, head.Length, cancelToken).ConfigureAwait(false);
                    var request = new List<byte>(head);

                    var userBuffer = new byte[1];

                    while (true)
                    {
                        await ReadExactAsync(stream, userBuffer, 1, cancelToken).ConfigureAwait(false);
                        request.Add(userBuffer[0]);

                        if (userBuffer[0] == 0x00)
                            break;
                    }

                    if (head[4] == 0 && head[5] == 0 && head[6] == 0 && head[7] == 1)
                    {
                        while (true)
                        {
                            await ReadExactAsync(stream, userBuffer, 1, cancelToken).ConfigureAwait(false);
                            request.Add(userBuffer[0]);

                            if (userBuffer[0] == 0x00)
                                break;
                        }
                    }

                    server.LastRequest = request.ToArray();

                    var response = new byte[8];
                    response[1] = server.replyCode;
                    await stream.WriteAsync(response.AsMemory(0, 8), cancelToken).ConfigureAwait(false);
                }
            }
        }
    }

    internal sealed class Socks5ProxyServer : IAsyncDisposable
    {
        private readonly CancellationTokenSource cts;
        private readonly TcpListener listener;
        private readonly byte method;
        private readonly byte replyCode;
        private Task serverTask;

        private Socks5ProxyServer(TcpListener listener, CancellationTokenSource cts, Task serverTask, byte method, byte replyCode)
        {
            this.listener = listener;
            this.cts = cts;
            this.serverTask = serverTask;
            this.method = method;
            this.replyCode = replyCode;
            this.EndPoint = (IPEndPoint)this.listener.LocalEndpoint;
        }

        public IPEndPoint EndPoint { get; }
        public byte[]? AuthRequest { get; private set; }
        public byte[]? ConnectRequest { get; private set; }

        public async ValueTask DisposeAsync()
        {
            await this.cts.CancelAsync().ConfigureAwait(false);
            this.listener.Stop();

            try
            {
                await this.serverTask.ConfigureAwait(false);
            }
            catch { }

            this.cts.Dispose();
        }

        public static Task<Socks5ProxyServer> StartAsync(byte method, byte replyCode)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var server = new Socks5ProxyServer(listener, cts, Task.CompletedTask, method, replyCode);
            server.serverTask = runAsync(server, cts.Token);

            return Task.FromResult(server);

            static async Task runAsync(Socks5ProxyServer server, CancelToken cancelToken)
            {
                using (cancelToken.Bridge(out var token))
                {
                    using var client = await server.listener.AcceptTcpClientAsync(token).ConfigureAwait(false);
                    var stream = client.GetStream().AsINetStream();
                    await using var stream1 = stream.ConfigureAwait(false);

                    var greetingHead = new byte[2];
                    await ReadExactAsync(stream, greetingHead, greetingHead.Length, cancelToken).ConfigureAwait(false);

                    if (greetingHead[0] != 0x05)
                        throw new IOException("Invalid SOCKS5 greeting");

                    var methods = new byte[greetingHead[1]];

                    if (methods.Length > 0)
                        await ReadExactAsync(stream, methods, methods.Length, cancelToken).ConfigureAwait(false);

                    await stream.WriteAsync(new byte[] { 0x05, server.method }, cancelToken).ConfigureAwait(false);
                    await stream.FlushAsync(cancelToken).ConfigureAwait(false);

                    if (server.method == 0x02)
                    {
                        var authLenBuffer = new byte[2];
                        await ReadExactAsync(stream, authLenBuffer, 2, cancelToken).ConfigureAwait(false);
                        var ulen = authLenBuffer[1];
                        var user = new byte[ulen];
                        await ReadExactAsync(stream, user, ulen, cancelToken).ConfigureAwait(false);
                        await ReadExactAsync(stream, authLenBuffer, 1, cancelToken).ConfigureAwait(false);
                        var plen = authLenBuffer[0];
                        var pass = new byte[plen];
                        await ReadExactAsync(stream, pass, plen, cancelToken).ConfigureAwait(false);

                        var auth = new byte[2 + ulen + 1 + plen];
                        auth[0] = 0x01;
                        auth[1] = ulen;
                        user.CopyTo(auth, 2);
                        auth[2 + ulen] = plen;
                        pass.CopyTo(auth, 3 + ulen);
                        server.AuthRequest = auth;

                        await stream.WriteAsync(new byte[] { 0x01, 0x00 }, cancelToken).ConfigureAwait(false);
                        await stream.FlushAsync(cancelToken).ConfigureAwait(false);
                    }

                    var head = new byte[4];
                    await ReadExactAsync(stream, head, head.Length, cancelToken).ConfigureAwait(false);
                    var atyp = head[3];

                    var address = atyp switch
                    {
                        0x01 => await ReadAddressAsync(stream, 4, cancelToken).ConfigureAwait(false),
                        0x04 => await ReadAddressAsync(stream, 16, cancelToken).ConfigureAwait(false),
                        0x03 => await ReadDomainAsync(stream, cancelToken).ConfigureAwait(false),
                        _ => throw new IOException("Unsupported ATYP"),
                    };

                    var portBytes = new byte[2];
                    await ReadExactAsync(stream, portBytes, 2, cancelToken).ConfigureAwait(false);

                    var request = new byte[4 + address.Length + 2];
                    head.CopyTo(request, 0);
                    address.CopyTo(request, 4);
                    portBytes.CopyTo(request, 4 + address.Length);
                    server.ConnectRequest = request;

                    var response = new byte[4 + address.Length + 2];
                    response[0] = 0x05;
                    response[1] = server.replyCode;
                    response[3] = atyp;
                    address.CopyTo(response, 4);
                    portBytes.CopyTo(response, 4 + address.Length);
                    await stream.WriteAsync(response, cancelToken).ConfigureAwait(false);
                    await stream.FlushAsync(cancelToken).ConfigureAwait(false);
                }
            }
        }

        private static async Task<byte[]> ReadAddressAsync(INetStream stream, int length, CancelToken cancelToken)
        {
            var buffer = new byte[length];
            await ReadExactAsync(stream, buffer, length, cancelToken).ConfigureAwait(false);

            return buffer;
        }

        private static async Task<byte[]> ReadDomainAsync(INetStream stream, CancelToken cancelToken)
        {
            var lenBuffer = new byte[1];
            await ReadExactAsync(stream, lenBuffer, 1, cancelToken).ConfigureAwait(false);
            var buffer = new byte[lenBuffer[0]];
            await ReadExactAsync(stream, buffer, buffer.Length, cancelToken).ConfigureAwait(false);

            return [lenBuffer[0], .. buffer];
        }
    }
}
