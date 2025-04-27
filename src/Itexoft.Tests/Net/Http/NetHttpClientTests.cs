// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using System.Text.Json;
using Itexoft.IO;
using Itexoft.Net;
using Itexoft.Net.Core;
using Itexoft.Net.Dns;
using Itexoft.Net.Http;
using Itexoft.Threading;

namespace Itexoft.Tests.Net.Http;

public sealed class NetHttpClientTests
{
    private const string host = "httpbin.org";
    private static readonly NetDnsHost dnsHost = new(host, NetDnsResolver.Default);
    private static readonly object timeoutSource = new();

    private static NetHttpClient CreateClient(NetHttpClient.CookieHandlerDelegate? cookieHandler = null)
    {
        var client = new NetHttpClient(new(dnsHost, 443), new NetTcpConnector())
        {
            TlsOptions = new() { TargetHost = host },
            CookieHandler = cookieHandler,
        };

        client.DefaultHeaders.UserAgent = "Itexoft.Tests/1.0";

        return client;
    }

    private static NetHttpClient CreateHttpClient()
    {
        var client = new NetHttpClient(new(dnsHost, 80), new NetTcpConnector());
        client.DefaultHeaders.UserAgent = "Itexoft.Tests/1.0";

        return client;
    }

    private static CancelToken CreateTimeoutToken(TimeSpan timeout) => new CancelToken(timeoutSource).Branch(timeout);

    private static async Task<JsonDocument> SendJsonAsync(Func<CancelToken, ValueTask<NetHttpResponse>> send, CancelToken cancelToken)
    {
        var response = await send(cancelToken).ConfigureAwait(false);
        await using var response1 = response.ConfigureAwait(false);

        return await ReadJsonWithRetryAsync(response, send, cancelToken).ConfigureAwait(false);
    }

    private static async Task EnsureSetCookieAsync(NetHttpClient client, NetHttpPathQuery path, CancelToken cancelToken)
    {
        const int attempts = 3;

        for (var attempt = 0; attempt < attempts; attempt++)
        {
            var response = await client.GetAsync(path, cancelToken).ConfigureAwait(false);
            await using var response1 = response.ConfigureAwait(false);

            if (response.Headers.SetCookie.Count > 0)
                return;

            await Task.Delay(200).ConfigureAwait(false);
        }

        throw new IOException("Expected Set-Cookie header missing.");
    }

    private static async Task<JsonDocument> ReadJsonWithRetryAsync(
        NetHttpResponse response,
        Func<CancelToken, ValueTask<NetHttpResponse>> retry,
        CancelToken cancelToken)
    {
        var text = await response.ReadAsStringAsync().ConfigureAwait(false);

        if (TryParseJson(text, out var doc))
            return doc;

        if (!LooksLikeHtml(text))
            throw CreateJsonException(text, response.Status);

        await Task.Delay(200).ConfigureAwait(false);
        var retryResponse = await retry(cancelToken).ConfigureAwait(false);
        await using var retryResponse1 = retryResponse.ConfigureAwait(false);
        var retryText = await retryResponse.ReadAsStringAsync().ConfigureAwait(false);

        if (TryParseJson(retryText, out var retryDoc))
            return retryDoc;

        throw CreateJsonException(retryText, retryResponse.Status);
    }

    private static bool TryParseJson(string text, out JsonDocument doc)
    {
        try
        {
            doc = JsonDocument.Parse(text);

            return true;
        }
        catch (JsonException)
        {
            doc = null!;

            return false;
        }
    }

    private static bool LooksLikeHtml(string text)
    {
        var span = text.AsSpan().TrimStart();

        return span.Length > 0 && span[0] == '<';
    }

    private static Exception CreateJsonException(string text, NetHttpStatus status)
    {
        var span = text.AsSpan().TrimStart();
        var snippetLength = Math.Min(span.Length, 200);
        var snippet = span[..snippetLength].ToString();

        return new JsonException($"Unexpected non-JSON response (status {status}): {snippet}");
    }

    [Test]
    public async Task GetAsync_ReturnsUrlAndArgs()
    {
        var client = CreateClient();
        await using var client1 = client.ConfigureAwait(false);
        var path = new NetHttpPathQuery("/get", ("a b", "c+d"), ("x", "1 2"));

        using var doc = await SendJsonAsync(ct => client.GetAsync(path, ct), CancelToken.None).ConfigureAwait(false);
        var root = doc.RootElement;

        Assert.That(root.GetProperty("url").GetString(), Is.EqualTo($"https://{host}/get?a b=c%2Bd&x=1 2"));
        var args = root.GetProperty("args");
        Assert.That(args.GetProperty("a b").GetString(), Is.EqualTo("c+d"));
        Assert.That(args.GetProperty("x").GetString(), Is.EqualTo("1 2"));
    }

    [Test]
    public async Task PostAsync_StringBody_EchoesData()
    {
        var client = CreateClient();
        await using var client1 = client.ConfigureAwait(false);
        const string payload = "hello httpbin";

        using var doc = await SendJsonAsync(ct => client.PostAsync("/post", payload, Encoding.UTF8, null, ct), CancelToken.None).ConfigureAwait(false);

        Assert.That(doc.RootElement.GetProperty("data").GetString(), Is.EqualTo(payload));
    }

    [Test]
    public async Task Tls_InvalidCertificate_Throws()
    {
        var client = new NetHttpClient(new(dnsHost, 443), new NetTcpConnector())
        {
            TlsOptions = new()
            {
                TargetHost = host,
                RemoteCertificateValidationCallback = (_, _, _, _) => false,
            },
        };

        await using var client1 = client.ConfigureAwait(false);

        Assert.That(
            async () => await client.GetAsync("/get", CancelToken.None).ConfigureAwait(false),
            Throws.Exception.TypeOf<AuthenticationException>().Or.TypeOf<IOException>());
    }

    [Test]
    public async Task GetAsync_HttpWithoutTls()
    {
        var client = CreateHttpClient();
        await using var client1 = client.ConfigureAwait(false);

        var response = await client.GetAsync("/get", CancelToken.None).ConfigureAwait(false);
        await using var response1 = response.ConfigureAwait(false);

        if (response.StatusClass == NetHttpStatusClass.Redirection)
        {
            Assert.That(response.Headers.Location, Is.Not.Null.Or.Empty);

            return;
        }

        using var doc = await ReadJsonWithRetryAsync(response, ct => client.GetAsync("/get", ct), CancelToken.None).ConfigureAwait(false);
        var root = doc.RootElement;

        Assert.That(root.GetProperty("url").GetString(), Does.Contain($"http://{host}"));
    }

    [Test]
    public async Task GetAsync_GzipDecoded()
    {
        var client = CreateClient();
        await using var client1 = client.ConfigureAwait(false);

        using var doc = await SendJsonAsync(ct => client.GetAsync("/gzip", ct), CancelToken.None).ConfigureAwait(false);

        Assert.That(doc.RootElement.GetProperty("gzipped").GetBoolean(), Is.True);
    }

    [Test]
    public async Task GetAsync_DeflateDecoded()
    {
        var client = CreateClient();
        await using var client1 = client.ConfigureAwait(false);

        using var doc = await SendJsonAsync(ct => client.GetAsync("/deflate", ct), CancelToken.None).ConfigureAwait(false);

        Assert.That(doc.RootElement.GetProperty("deflated").GetBoolean(), Is.True);
    }

    [Test]
    public async Task GetAsync_DeflateRawDecoded()
    {
        var payload = Encoding.UTF8.GetBytes("{\"ok\":true}");
        byte[] compressed;

        using (var ms = new MemoryStream())
        {
            using (var deflate = new DeflateStream(ms, CompressionLevel.SmallestSize, true))
                deflate.Write(payload, 0, payload.Length);

            compressed = ms.ToArray();
        }

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var serverReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var serverTask = Task.Run(async () =>
        {
            try
            {
                serverReady.SetResult();
                using var tcp = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
                await using var stream = tcp.GetStream().AsINetStream();

                _ = await ReadHeadersAsync(stream, CancelToken.None).ConfigureAwait(false);

                var header = $"HTTP/1.1 200 OK\r\nContent-Encoding: deflate\r\nContent-Length: {compressed.Length}\r\nConnection: close\r\n\r\n";
                var headerBytes = Encoding.ASCII.GetBytes(header);
                await stream.WriteAsync(headerBytes).ConfigureAwait(false);
                await stream.WriteAsync(compressed).ConfigureAwait(false);
                await stream.FlushAsync().ConfigureAwait(false);
            }
            catch
            {
                // ignored
            }
        });

        await serverReady.Task.ConfigureAwait(false);

        try
        {
            var client = new NetHttpClient(new("127.0.0.1", port), new NetTcpConnector());
            await using var client1 = client.ConfigureAwait(false);
            var response = await client.GetAsync("/deflate-raw", CancelToken.None).ConfigureAwait(false);
            await using var response1 = response.ConfigureAwait(false);
            var text = await response.ReadAsStringAsync().ConfigureAwait(false);

            Assert.That(text, Is.EqualTo("{\"ok\":true}"));
        }
        finally
        {
            listener.Stop();
            await serverTask.ConfigureAwait(false);
        }
    }

    [Test]
    public async Task GetAsync_ChunkedStream()
    {
        var client = CreateClient();
        await using var client1 = client.ConfigureAwait(false);

        var response = await client.GetAsync("/stream/3", CancelToken.None).ConfigureAwait(false);
        await using var response1 = response.ConfigureAwait(false);
        var text = await response.ReadAsStringAsync().ConfigureAwait(false);
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        Assert.That(lines.Length, Is.EqualTo(3));
        StringAssert.Contains("\"id\"", lines[0]);
    }

    [Test]
    public async Task ConnectionClose_AllowsReconnect()
    {
        var client = CreateClient();
        await using var client1 = client.ConfigureAwait(false);

        var request = new NetHttpRequest(NetHttpMethod.Get, "/get")
        {
            KeepAlive = false,
            Headers = new() { Connection = "close" },
        };

        var response = await client.SendAsync(request, CancelToken.None).ConfigureAwait(false);

        await using (response.ConfigureAwait(false))
            _ = await response.ReadAsBytes().ConfigureAwait(false);

        var second = await client.GetAsync("/get", CancelToken.None).ConfigureAwait(false);

        await using (second.ConfigureAwait(false))
            _ = await second.ReadAsBytes().ConfigureAwait(false);
    }

    [Test]
    public async Task Http10_KeepAlive_TwoRequestsSameConnection()
    {
        using var cts = CreateTimeoutToken(TimeSpan.FromSeconds(5)).Bridge(out var token);
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var serverReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var serverDone = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        var serverTask = Task.Run(async () =>
        {
            try
            {
                serverReady.SetResult();
                using var tcp = await listener.AcceptTcpClientAsync(token).ConfigureAwait(false);
                await using var stream = tcp.GetStream().AsINetStream();

                _ = await ReadHeadersAsync(stream, token).ConfigureAwait(false);
                await WriteResponseAsync(stream, "HTTP/1.0 200 OK", "Connection: keep-alive", "OK", token).ConfigureAwait(false);

                _ = await ReadHeadersAsync(stream, token).ConfigureAwait(false);
                await WriteResponseAsync(stream, "HTTP/1.0 200 OK", "Connection: close", "OK", token).ConfigureAwait(false);

                serverDone.SetResult(2);
            }
            catch (Exception ex)
            {
                serverDone.SetException(ex);
            }
        });

        await serverReady.Task.ConfigureAwait(false);

        try
        {
            var client = new NetHttpClient(new("127.0.0.1", port), new NetTcpConnector());
            await using var client1 = client.ConfigureAwait(false);

            var request = new NetHttpRequest(NetHttpMethod.Get, "/keep")
            {
                HttpVersion = NetHttpVersion.Version10,
                KeepAlive = true,
                Headers = new() { Connection = "keep-alive" },
            };

            var response = await client.SendAsync(request, CancelToken.None).ConfigureAwait(false);

            await using (response.ConfigureAwait(false))
                _ = await response.ReadAsBytes().ConfigureAwait(false);

            var second = await client.SendAsync(request, CancelToken.None).ConfigureAwait(false);

            await using (second.ConfigureAwait(false))
                _ = await second.ReadAsBytes().ConfigureAwait(false);

            var completed = await Task.WhenAny(serverDone.Task, Task.Delay(TimeSpan.FromSeconds(5))).ConfigureAwait(false);
            Assert.That(completed, Is.EqualTo(serverDone.Task));
            Assert.That(await serverDone.Task.ConfigureAwait(false), Is.EqualTo(2));
        }
        finally
        {
            listener.Stop();
            await serverTask;
        }
    }

    [Test]
    public async Task ReceiveHeadersOnly_DoesNotReadBody()
    {
        var client = CreateClient();
        await using var client1 = client.ConfigureAwait(false);

        var request = new NetHttpRequest(NetHttpMethod.Get, "/get")
        {
            ReceiveHeadersOnly = true,
        };

        var response = await client.SendAsync(request, CancelToken.None).ConfigureAwait(false);
        await using var response1 = response.ConfigureAwait(false);
        var bytes = await response.ReadAsBytes().ConfigureAwait(false);

        Assert.That(bytes.Length, Is.EqualTo(0));
        Assert.That(response.Headers.Count, Is.GreaterThan(0));
    }

    [Test]
    public async Task Headers_OverrideAcceptEncoding()
    {
        var client = CreateClient();
        await using var client1 = client.ConfigureAwait(false);
        var headers = new NetHttpHeaders { AcceptEncoding = "identity" };
        var request = new NetHttpRequest(NetHttpMethod.Get, "/headers") { Headers = headers };

        using var doc = await SendJsonAsync(ct => client.SendAsync(request, ct), CancelToken.None).ConfigureAwait(false);
        var requestHeaders = doc.RootElement.GetProperty("headers");

        Assert.That(requestHeaders.GetProperty("Accept-Encoding").GetString(), Is.EqualTo("identity"));
    }

    [Test]
    public async Task Cookies_AcceptedAndSent()
    {
        var client = CreateClient();
        await using var client1 = client.ConfigureAwait(false);
        var setPath = new NetHttpPathQuery("/cookies/set", ("session", "abc"));

        var setResponse = await client.GetAsync(setPath, CancelToken.None).ConfigureAwait(false);

        await using (setResponse.ConfigureAwait(false))
            await setResponse.DisposeAsync().ConfigureAwait(false);

        using var doc = await SendJsonAsync(ct => client.GetAsync("/cookies", ct), CancelToken.None).ConfigureAwait(false);
        var cookies = doc.RootElement.GetProperty("cookies");

        Assert.That(cookies.GetProperty("session").GetString(), Is.EqualTo("abc"));
    }

    [Test]
    public async Task Cookies_RejectedByHandler()
    {
        var client = CreateClient(_ => false);
        await using var client1 = client.ConfigureAwait(false);
        var setPath = new NetHttpPathQuery("/cookies/set", ("deny", "1"));

        var setResponse = await client.GetAsync(setPath, CancelToken.None).ConfigureAwait(false);

        await using (setResponse.ConfigureAwait(false))
            await setResponse.DisposeAsync().ConfigureAwait(false);

        using var doc = await SendJsonAsync(ct => client.GetAsync("/cookies", ct), CancelToken.None).ConfigureAwait(false);
        var cookies = doc.RootElement.GetProperty("cookies");

        Assert.That(cookies.TryGetProperty("deny", out _), Is.False);
    }

    [Test]
    public async Task Cookies_RequestContainerOverridesClient()
    {
        var client = CreateClient();
        await using var client1 = client.ConfigureAwait(false);
        client.Cookies.Add(new("client", "1", host, "/"));

        var requestContainer = new NetCookieContainer();
        requestContainer.Add(new("req", "2", host, "/"));

        var request = new NetHttpRequest(NetHttpMethod.Get, "/cookies")
        {
            CookieContainer = requestContainer,
        };

        using var doc = await SendJsonAsync(ct => client.SendAsync(request, ct), CancelToken.None).ConfigureAwait(false);
        var cookies = doc.RootElement.GetProperty("cookies");

        Assert.That(cookies.GetProperty("req").GetString(), Is.EqualTo("2"));
        Assert.That(cookies.TryGetProperty("client", out _), Is.False);
    }

    [Test]
    public async Task Cookies_PathScope()
    {
        var client = CreateClient();
        await using var client1 = client.ConfigureAwait(false);
        var setPath = new NetHttpPathQuery("/response-headers", ("Set-Cookie", "pathcookie=1; Path=/cookies"));

        await EnsureSetCookieAsync(client, setPath, CancelToken.None).ConfigureAwait(false);

        using var matchDoc = await SendJsonAsync(ct => client.GetAsync("/cookies", ct), CancelToken.None).ConfigureAwait(false);
        var matchCookies = matchDoc.RootElement.GetProperty("cookies");

        Assert.That(matchCookies.GetProperty("pathcookie").GetString(), Is.EqualTo("1"));

        using var missDoc = await SendJsonAsync(ct => client.GetAsync("/get", ct), CancelToken.None).ConfigureAwait(false);
        var cookieHeader = TryGetHeader(missDoc.RootElement, "Cookie");

        Assert.That(cookieHeader, Is.Null.Or.Empty);
    }

    [Test]
    public async Task Cookies_SecureScope()
    {
        var client = CreateClient();
        await using var client1 = client.ConfigureAwait(false);
        var setPath = new NetHttpPathQuery("/response-headers", ("Set-Cookie", "securecookie=1; Secure; Path=/"));

        await EnsureSetCookieAsync(client, setPath, CancelToken.None).ConfigureAwait(false);

        var secureHeader = client.Cookies.GetCookieHeader(new(dnsHost, 443), "/get", true);
        var insecureHeader = client.Cookies.GetCookieHeader(new(dnsHost, 80), "/get", false);

        Assert.That(secureHeader, Does.Contain("securecookie=1"));
        Assert.That(insecureHeader, Is.Null.Or.Empty);
    }

    [Test]
    public async Task Cookies_MaxAgeExpired_NotSent()
    {
        var client = CreateClient();
        await using var client1 = client.ConfigureAwait(false);
        var setPath = new NetHttpPathQuery("/response-headers", ("Set-Cookie", "expire=1; Max-Age=0; Path=/"));

        await EnsureSetCookieAsync(client, setPath, CancelToken.None).ConfigureAwait(false);

        using var doc = await SendJsonAsync(ct => client.GetAsync("/cookies", ct), CancelToken.None).ConfigureAwait(false);
        var cookies = doc.RootElement.GetProperty("cookies");

        Assert.That(cookies.TryGetProperty("expire", out _), Is.False);
    }

    [Test]
    public async Task Status_NoContent_HasNoBody()
    {
        var client = CreateClient();
        await using var client1 = client.ConfigureAwait(false);

        var response = await client.GetAsync("/status/204", CancelToken.None).ConfigureAwait(false);
        await using var response1 = response.ConfigureAwait(false);
        var bytes = await response.ReadAsBytes().ConfigureAwait(false);

        Assert.That(response.Status, Is.EqualTo(NetHttpStatus.NoContent));
        Assert.That(bytes.Length, Is.EqualTo(0));
    }

    [Test]
    public async Task StatusClasses_NoRedirectHandling()
    {
        var client = CreateClient();
        await using var client1 = client.ConfigureAwait(false);

        var redirection = await client.GetAsync("/status/302", CancelToken.None).ConfigureAwait(false);

        await using (redirection.ConfigureAwait(false))
            Assert.That(redirection.StatusClass, Is.EqualTo(NetHttpStatusClass.Redirection));

        var clientError = await client.GetAsync("/status/404", CancelToken.None).ConfigureAwait(false);

        await using (clientError.ConfigureAwait(false))
            Assert.That(clientError.StatusClass, Is.EqualTo(NetHttpStatusClass.ClientError));

        var serverError = await client.GetAsync("/status/500", CancelToken.None).ConfigureAwait(false);

        await using (serverError.ConfigureAwait(false))
            Assert.That(serverError.StatusClass, Is.EqualTo(NetHttpStatusClass.ServerError));
    }

    [Test]
    public async Task KeepAlive_MultipleRequests()
    {
        var client = CreateClient();
        await using var client1 = client.ConfigureAwait(false);

        var first = await client.GetAsync("/uuid", CancelToken.None).ConfigureAwait(false);

        await using (first.ConfigureAwait(false))
            _ = await first.ReadAsStringAsync().ConfigureAwait(false);

        var second = await client.GetAsync("/get", CancelToken.None).ConfigureAwait(false);

        await using (second.ConfigureAwait(false))
            _ = await second.ReadAsStringAsync().ConfigureAwait(false);
    }

    [Test]
    public async Task ContentStream_WithOffset_WritesRemainingBytes()
    {
        var client = CreateClient();
        await using var client1 = client.ConfigureAwait(false);
        var bytes = Encoding.UTF8.GetBytes("xxhello");
        var stream = new OffsetStream(bytes, 2);
        await using var stream1 = stream.ConfigureAwait(false);

        var request = new NetHttpRequest(NetHttpMethod.Post, "/post")
        {
            Content = stream,
        };

        using var doc = await SendJsonAsync(ct => client.SendAsync(request, ct), CancelToken.None).ConfigureAwait(false);

        Assert.That(doc.RootElement.GetProperty("data").GetString(), Is.EqualTo("hello"));
    }

    [Test]
    public async Task Headers_TooLarge_Throws()
    {
        using var cts = CreateTimeoutToken(TimeSpan.FromSeconds(5)).Bridge(out var token);
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var serverReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var serverTask = Task.Run(async () =>
        {
            try
            {
                serverReady.SetResult();
                using var tcp = await listener.AcceptTcpClientAsync(token).ConfigureAwait(false);
                await using var stream = tcp.GetStream().AsINetStream();

                _ = await ReadHeadersAsync(stream, token).ConfigureAwait(false);
                var bigHeader = new string('a', 70_000);
                var response = $"HTTP/1.1 200 OK\r\nX-Big: {bigHeader}\r\n\r\n";
                var bytes = Encoding.ASCII.GetBytes(response);
                await stream.WriteAsync(bytes, token).ConfigureAwait(false);
                await stream.FlushAsync(token).ConfigureAwait(false);
            }
            catch
            {
                // ignored
            }
        });

        await serverReady.Task.ConfigureAwait(false);

        try
        {
            var client = new NetHttpClient(new("127.0.0.1", port), new NetTcpConnector());
            await using var client1 = client.ConfigureAwait(false);

            Assert.That(async () => await client.GetAsync("/big", CancelToken.None).ConfigureAwait(false), Throws.InstanceOf<IOException>());
        }
        finally
        {
            listener.Stop();
            await serverTask;
        }
    }

    [Test]
    public async Task Timeout_RequestTimeout()
    {
        var client = CreateClient();
        await using var client1 = client.ConfigureAwait(false);

        var request = new NetHttpRequest(NetHttpMethod.Get, "/delay/3")
        {
            Timeout = TimeSpan.FromMilliseconds(500),
        };

        Assert.That(async () => await client.SendAsync(request, CancelToken.None).ConfigureAwait(false), Throws.InstanceOf<OperationCanceledException>());
    }

    [Test]
    public async Task Timeout_CancelToken()
    {
        var client = CreateClient();
        await using var client1 = client.ConfigureAwait(false);
        var token = CreateTimeoutToken(TimeSpan.FromMilliseconds(200));

        Assert.That(async () => await client.GetAsync("/delay/3", token).ConfigureAwait(false), Throws.InstanceOf<OperationCanceledException>());
    }

    [Test]
    public async Task Stress_LargeResponse()
    {
        var client = CreateClient();
        await using var client1 = client.ConfigureAwait(false);

        var response = await client.GetAsync("/bytes/1048576", CancelToken.None).ConfigureAwait(false);
        await using var response1 = response.ConfigureAwait(false);
        var bytes = await response.ReadAsBytes().ConfigureAwait(false);

        Assert.That(bytes.Length, Is.EqualTo(102400));
    }

    [Test]
    public async Task Stress_ChunkedBytes()
    {
        var client = CreateClient();
        await using var client1 = client.ConfigureAwait(false);

        var response = await client.GetAsync("/stream-bytes/131072", CancelToken.None).ConfigureAwait(false);
        await using var response1 = response.ConfigureAwait(false);
        var bytes = await response.ReadAsBytes().ConfigureAwait(false);

        Assert.That(bytes.Length, Is.EqualTo(102400));
    }

    [Test]
    public async Task Stress_ManySequentialRequests()
    {
        var client = CreateClient();
        await using var client1 = client.ConfigureAwait(false);

        for (var i = 0; i < 10; i++)
        {
            var path = new NetHttpPathQuery("/get", ("i", i.ToString()));
            var response = await client.GetAsync(path, CancelToken.None).ConfigureAwait(false);
            await using var response1 = response.ConfigureAwait(false);
            var text = await response.ReadAsStringAsync().ConfigureAwait(false);
            Assert.That(text, Is.Not.Empty);
        }
    }

    [Test]
    public async Task Stress_ConcurrentRequests()
    {
        var client = CreateClient();
        await using var client1 = client.ConfigureAwait(false);

        var tasks = Enumerable.Range(0, 5).Select(async i =>
        {
            var path = new NetHttpPathQuery("/get", ("i", i.ToString()));
            var response = await client.GetAsync(path, CancelToken.None).ConfigureAwait(false);
            await using var response1 = response.ConfigureAwait(false);
            _ = await response.ReadAsStringAsync().ConfigureAwait(false);
        }).ToArray();

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    [Test]
    public async Task ReadUntilClose_WhenNoContentLength()
    {
        var body = Encoding.UTF8.GetBytes("close-body");
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var serverTask = Task.Run(async () =>
        {
            using var tcp = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
            using var stream = tcp.GetStream();
            var buffer = new byte[1024];
            var matched = 0;

            while (true)
            {
                var read = await stream.ReadAsync(buffer).ConfigureAwait(false);

                if (read == 0)
                    break;

                for (var i = 0; i < read; i++)
                {
                    matched = buffer[i] switch
                    {
                        (byte)'\r' when matched is 0 or 2 => matched + 1,
                        (byte)'\n' when matched is 1 or 3 => matched + 1,
                        _ => 0,
                    };

                    if (matched == 4)
                        break;
                }

                if (matched == 4)
                    break;
            }

            var header = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(header).ConfigureAwait(false);
            await stream.WriteAsync(body).ConfigureAwait(false);
            await stream.FlushAsync().ConfigureAwait(false);
        });

        try
        {
            var client = new NetHttpClient(new("127.0.0.1", port), new NetTcpConnector());
            await using var client1 = client.ConfigureAwait(false);
            var response = await client.GetAsync("/close", CancelToken.None).ConfigureAwait(false);
            await using var response1 = response.ConfigureAwait(false);
            var text = await response.ReadAsStringAsync().ConfigureAwait(false);

            Assert.That(text, Is.EqualTo("close-body"));
        }
        finally
        {
            listener.Stop();
            await serverTask.ConfigureAwait(false);
        }
    }

    private static string? TryGetHeader(JsonElement root, string name)
    {
        if (!root.TryGetProperty("headers", out var headers))
            return null;

        return headers.TryGetProperty(name, out var value) ? value.GetString() : null;
    }

    private static async Task<string> ReadHeadersAsync(INetStream stream, CancelToken cancelToken)
    {
        var buffer = new byte[1];
        var match = 0;
        var builder = new StringBuilder();

        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancelToken).ConfigureAwait(false);

            if (read == 0)
                throw new IOException("Unexpected end of stream.");

            builder.Append((char)buffer[0]);

            match = buffer[0] switch
            {
                (byte)'\r' when match is 0 or 2 => match + 1,
                (byte)'\n' when match is 1 or 3 => match + 1,
                _ => 0,
            };

            if (match == 4)
                break;

            if (builder.Length > 128_000)
                throw new IOException("Request headers too large.");
        }

        return builder.ToString();
    }

    private static async Task WriteResponseAsync(
        INetStream stream,
        string statusLine,
        string connectionHeader,
        string body,
        CancelToken cancelToken)
    {
        var header = $"{statusLine}\r\nContent-Length: {body.Length}\r\n{connectionHeader}\r\n\r\n";
        var bytes = Encoding.ASCII.GetBytes(header + body);
        await stream.WriteAsync(bytes, cancelToken).ConfigureAwait(false);
        await stream.FlushAsync(cancelToken).ConfigureAwait(false);
    }

    private sealed class OffsetStream(byte[] data, int start) : StreamBase, IStreamRal
    {
        private int offset = start;

        public long Length => data.Length;
        public long Position => this.offset;

        public ValueTask<int> ReadAsync(Memory<byte> buffer, CancelToken cancelToken = default)
        {
            if (this.offset >= data.Length)
                return new(0);

            var toCopy = Math.Min(buffer.Length, data.Length - this.offset);
            data.AsMemory(this.offset, toCopy).CopyTo(buffer);
            this.offset += toCopy;

            return new(toCopy);
        }

        protected override ValueTask DisposeAny() => ValueTask.CompletedTask;
    }
}
