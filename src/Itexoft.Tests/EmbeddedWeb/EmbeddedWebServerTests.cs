// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Itexoft.EmbeddedWeb;
using Microsoft.AspNetCore.Hosting;

namespace Itexoft.Tests.EmbeddedWeb;

[TestFixture]
[NonParallelizable]
public class EmbeddedWebServerTests
{
    [Test]
    public async Task AssemblyBundles_AreServedThroughStaticPipeline()
    {
        EmbeddedWebServer.RegisterBundlesFromAssembly(typeof(EmbeddedWebServerTests).Assembly);

        var port = GetFreeTcpPort();
        var app = EmbeddedWebServer.CreateWebApp(
            "app1",
            builder =>
            {
                builder.WebHost.UseKestrel();
                builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
            },
            options => options.EnableSpaFallback = false);

        await using var _ = app;
        await app.StartAsync();
        await Task.Delay(100);

        using var client = CreateHttpClient(port);

        var root = await client.GetStringAsync("/");
        StringAssert.Contains("App 1 Home", root);

        var about = await client.GetStringAsync("/about.html");
        StringAssert.Contains("About App 1", about);

        var script = await client.GetStringAsync("/js/app.js");
        StringAssert.Contains("window.__APP1__", script);

        await app.StopAsync();
    }

    [Test]
    public async Task SpaFallback_ReturnsConfiguredFile()
    {
        EmbeddedWebServer.RegisterBundlesFromAssembly(typeof(EmbeddedWebServerTests).Assembly);

        var port = GetFreeTcpPort();
        var app = EmbeddedWebServer.CreateWebApp(
            "app1",
            builder =>
            {
                builder.WebHost.UseKestrel();
                builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
            },
            options =>
            {
                options.EnableSpaFallback = true;
                options.SpaFallbackFile = "index.html";
            });

        await using var _ = app;
        await app.StartAsync();
        await Task.Delay(100);

        using var client = CreateHttpClient(port);

        var response = await client.GetAsync("/missing/route");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        StringAssert.Contains("App 1 Home", content);

        await app.StopAsync();
    }

    [Test]
    public async Task StartAsync_HostsBundleOnTcpPort()
    {
        var bundleId = "runtime-" + Guid.NewGuid().ToString("N");
        var archive = CreateZipArchive(
            new Dictionary<string, string>
            {
                ["index.html"] = "<html><body><p>runtime bundle</p></body></html>",
                ["style.css"] = "body { background: #eee; }"
            });

        EmbeddedWebServer.RegisterBundle(bundleId, EmbeddedArchiveSource.FromStream(new MemoryStream(archive)), true);

        var port = GetFreeTcpPort();
        await using var handle = await EmbeddedWebServer.StartAsync(bundleId, port);

        using var client = CreateHttpClient(port);

        var response = await client.GetAsync("/");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        StringAssert.Contains("runtime bundle", body);
    }

    [Test]
    public async Task NonSeekableStream_IsBufferedAndServed()
    {
        var bundleId = "nonseek-" + Guid.NewGuid().ToString("N");
        var archiveBytes = CreateZipArchive(
            new Dictionary<string, string>
            {
                ["index.html"] = "<html><body>non-seek</body></html>"
            });

        var source = EmbeddedArchiveSource.FromFactory(async _ =>
        {
            await Task.Yield();

            return new NonSeekableStream(new MemoryStream(archiveBytes));
        });

        EmbeddedWebServer.RegisterBundle(bundleId, source, true);

        var port = GetFreeTcpPort();
        var app = EmbeddedWebServer.CreateWebApp(
            bundleId,
            builder =>
            {
                builder.WebHost.UseKestrel();
                builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
            });

        await using var _ = app;
        await app.StartAsync();

        using var client = CreateHttpClient(port);
        var content = await client.GetStringAsync("/");
        StringAssert.Contains("non-seek", content);

        await app.StopAsync();
    }

    [Test]
    public async Task MultipleHandles_ServeIndependentBundles()
    {
        EmbeddedWebServer.RegisterBundlesFromAssembly(typeof(EmbeddedWebServerTests).Assembly);

        var port1 = GetFreeTcpPort();
        var port2 = GetFreeTcpPort();

        await using var handle1 = await EmbeddedWebServer.StartAsync(
            "app1",
            port1,
            configureOptions: options => options.EnableSpaFallback = false);
        await using var handle2 = await EmbeddedWebServer.StartAsync(
            "app2",
            port2,
            configureOptions: options => options.EnableSpaFallback = false);

        using var client1 = CreateHttpClient(port1);
        using var client2 = CreateHttpClient(port2);

        var app1Root = await client1.GetStringAsync("/");
        StringAssert.Contains("App 1 Home", app1Root);

        var app1About = await client1.GetStringAsync("/about.html");
        StringAssert.Contains("About App 1", app1About);

        var app2Root = await client2.GetStringAsync("/");
        StringAssert.Contains("App 2 Root", app2Root);

        var missing = await client2.GetAsync("/missing");
        Assert.AreEqual(HttpStatusCode.NotFound, missing.StatusCode);
    }

    private static HttpClient CreateHttpClient(int port)
    {
        var handler = new SocketsHttpHandler();
        var client = new HttpClient(handler, true)
        {
            BaseAddress = new($"http://127.0.0.1:{port}"),
            DefaultRequestVersion = HttpVersion.Version11,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact
        };

        return client;
    }

    private static byte[] CreateZipArchive(IReadOnlyDictionary<string, string> files)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, true, Encoding.UTF8))
        {
            foreach (var (path, content) in files)
            {
                var entry = archive.CreateEntry(path, CompressionLevel.NoCompression);
                using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
                writer.Write(content);
            }
        }

        return output.ToArray();
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        return port;
    }

    private sealed class NonSeekableStream : Stream
    {
        private readonly Stream _inner;

        public NonSeekableStream(Stream inner) => this._inner = inner;

        public override bool CanRead => this._inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => this._inner.Length;

        public override long Position
        {
            get => this._inner.Position;
            set => throw new NotSupportedException();
        }

        public override void Flush() => this._inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => this._inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                this._inner.Dispose();
            base.Dispose(disposing);
        }
    }
}