// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Itexoft.EmbeddedWeb;
using Itexoft.IO;
using Microsoft.AspNetCore.Hosting;

namespace Itexoft.Tests.EmbeddedWeb;

[TestFixture, NonParallelizable]
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

        await using var _ = app.ConfigureAwait(false);
        await app.StartAsync().ConfigureAwait(false);
        await Task.Delay(100).ConfigureAwait(false);

        using var client = CreateHttpClient(port);

        var root = await client.GetStringAsync("/").ConfigureAwait(false);
        StringAssert.Contains("App 1 Home", root);

        var about = await client.GetStringAsync("/about.html").ConfigureAwait(false);
        StringAssert.Contains("About App 1", about);

        var script = await client.GetStringAsync("/js/app.js").ConfigureAwait(false);
        StringAssert.Contains("window.__APP1__", script);

        await app.StopAsync().ConfigureAwait(false);
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

        await using var _ = app.ConfigureAwait(false);
        await app.StartAsync().ConfigureAwait(false);
        await Task.Delay(100).ConfigureAwait(false);

        using var client = CreateHttpClient(port);

        var response = await client.GetAsync("/missing/route").ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        StringAssert.Contains("App 1 Home", content);

        await app.StopAsync().ConfigureAwait(false);
    }

    [Test]
    public async Task StartAsync_HostsBundleOnTcpPort()
    {
        var bundleId = "runtime-" + Guid.NewGuid().ToString("N");

        var archive = CreateZipArchive(
            new Dictionary<string, string>
            {
                ["index.html"] = "<html><body><p>runtime bundle</p></body></html>",
                ["style.css"] = "body { background: #eee; }",
            });

        EmbeddedWebServer.RegisterBundle(bundleId, EmbeddedArchiveSource.FromStream(new MemoryStream(archive)), true);

        var port = GetFreeTcpPort();
        var handle = await EmbeddedWebServer.StartAsync(bundleId, port).ConfigureAwait(false);
        await using var handle1 = handle.ConfigureAwait(false);

        using var client = CreateHttpClient(port);

        var response = await client.GetAsync("/").ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        StringAssert.Contains("runtime bundle", body);
    }

    [Test]
    public async Task NonSeekableStream_IsBufferedAndServed()
    {
        var bundleId = "nonseek-" + Guid.NewGuid().ToString("N");

        var archiveBytes = CreateZipArchive(
            new Dictionary<string, string>
            {
                ["index.html"] = "<html><body>non-seek</body></html>",
            });

        var source = EmbeddedArchiveSource.FromFactory(async _ =>
        {
            await Task.Yield();

            return new NonSeekableStream(new MemoryStream(archiveBytes)).AsBclStream();
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

        await using var _ = app.ConfigureAwait(false);
        await app.StartAsync().ConfigureAwait(false);

        using var client = CreateHttpClient(port);
        var content = await client.GetStringAsync("/").ConfigureAwait(false);
        StringAssert.Contains("non-seek", content);

        await app.StopAsync().ConfigureAwait(false);
    }

    [Test]
    public async Task MultipleHandles_ServeIndependentBundles()
    {
        EmbeddedWebServer.RegisterBundlesFromAssembly(typeof(EmbeddedWebServerTests).Assembly);

        var port1 = GetFreeTcpPort();
        var port2 = GetFreeTcpPort();

        var handle1 = await EmbeddedWebServer.StartAsync("app1", port1, configureOptions: options => options.EnableSpaFallback = false).ConfigureAwait(false);
        await using var handle3 = handle1.ConfigureAwait(false);
        var handle2 = await EmbeddedWebServer.StartAsync("app2", port2, configureOptions: options => options.EnableSpaFallback = false).ConfigureAwait(false);
        await using var handle4 = handle2.ConfigureAwait(false);

        using var client1 = CreateHttpClient(port1);
        using var client2 = CreateHttpClient(port2);

        var app1Root = await client1.GetStringAsync("/").ConfigureAwait(false);
        StringAssert.Contains("App 1 Home", app1Root);

        var app1About = await client1.GetStringAsync("/about.html").ConfigureAwait(false);
        StringAssert.Contains("About App 1", app1About);

        var app2Root = await client2.GetStringAsync("/").ConfigureAwait(false);
        StringAssert.Contains("App 2 Root", app2Root);

        var missing = await client2.GetAsync("/missing").ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.NotFound, missing.StatusCode);
    }

    private static HttpClient CreateHttpClient(int port)
    {
        var handler = new SocketsHttpHandler();

        var client = new HttpClient(handler, true)
        {
            BaseAddress = new($"http://127.0.0.1:{port}"),
            DefaultRequestVersion = HttpVersion.Version11,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact,
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

    private sealed class NonSeekableStream(Stream inner) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => inner.Length;

        public override long Position
        {
            get => inner.Position;
            set => throw new NotSupportedException();
        }

        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                inner.Dispose();

            base.Dispose(disposing);
        }
    }
}
