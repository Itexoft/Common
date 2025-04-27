// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Itexoft.EmbeddedWeb;

public static class EmbeddedWebServer
{
    private const string ResourceMarker = ".EmbeddedWeb.";
    private static readonly ConcurrentDictionary<string, EmbeddedWebBundle> Bundles = new(StringComparer.OrdinalIgnoreCase);
    private static readonly FileExtensionContentTypeProvider ContentTypeProvider = new();

    public static void RegisterBundle(string bundleId, IEmbeddedArchiveSource source, bool overwrite = false)
    {
        if (string.IsNullOrWhiteSpace(bundleId))
            throw new ArgumentException("Bundle identifier cannot be empty.", nameof(bundleId));

        if (source == null)
            throw new ArgumentNullException(nameof(source));

        var bundle = new EmbeddedWebBundle(bundleId, source);

        if (!overwrite)
        {
            if (!Bundles.TryAdd(bundleId, bundle))
                throw new InvalidOperationException($"Bundle '{bundleId}' is already registered.");
        }
        else
        {
            Bundles[bundleId] = bundle;
        }
    }

    public static void RegisterBundlesFromAssembly(Assembly assembly)
    {
        if (assembly == null)
            throw new ArgumentNullException(nameof(assembly));

        foreach (var resource in assembly.GetManifestResourceNames())
        {
            if (!TryExtractBundleId(resource, out var bundleId))
                continue;

            if (Bundles.ContainsKey(bundleId))
                continue;

            RegisterBundle(bundleId, EmbeddedArchiveSource.FromResource(assembly, resource));
        }
    }

    internal static bool TryGetBundle(string bundleId, out EmbeddedWebBundle bundle)
        => Bundles.TryGetValue(bundleId, out bundle!);

    public static EmbeddedWebHandle Start(
        string bundleId,
        int port,
        Action<WebApplicationBuilder>? configureBuilder = null,
        Action<WebApplication>? configureApp = null,
        Action<EmbeddedWebOptions>? configureOptions = null,
        CancellationToken cancellationToken = default)
        => StartAsync(bundleId, port, configureBuilder, configureApp, configureOptions, cancellationToken).GetAwaiter().GetResult();

    public static async Task<EmbeddedWebHandle> StartAsync(
        string bundleId,
        int port,
        Action<WebApplicationBuilder>? configureBuilder = null,
        Action<WebApplication>? configureApp = null,
        Action<EmbeddedWebOptions>? configureOptions = null,
        CancellationToken cancellationToken = default)
    {
        if (!Bundles.TryGetValue(bundleId, out var bundle))
            throw new InvalidOperationException($"Bundle '{bundleId}' is not registered.");

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseKestrel();
        builder.WebHost.UseUrls($"http://127.0.0.1:{port}");

        configureBuilder?.Invoke(builder);

        var app = builder.Build();

        var options = CreateOptions(configureOptions);
        await ConfigurePipelineAsync(app, bundle, options, cancellationToken).ConfigureAwait(false);

        configureApp?.Invoke(app);

        await app.StartAsync(cancellationToken).ConfigureAwait(false);

        var completion = app.WaitForShutdownAsync();

        return new(bundleId, app, completion);
    }

    public static void MapEmbeddedWebApp(
        this WebApplication app,
        string pattern,
        string bundleId,
        Action<EmbeddedWebOptions>? configure = null)
    {
        if (app == null)
            throw new ArgumentNullException(nameof(app));
        if (pattern == null)
            throw new ArgumentNullException(nameof(pattern));
        if (!Bundles.TryGetValue(bundleId, out var bundle))
            throw new InvalidOperationException($"Bundle '{bundleId}' is not registered.");

        var options = CreateOptions(configure);

        app.Map(
            pattern,
            (WebApplication branch) =>
            {
                var provider = bundle.GetFileProviderAsync(CancellationToken.None).GetAwaiter().GetResult();
                ConfigurePipeline(branch, provider, bundle, options);
            });
    }

    public static IServiceCollection AddEmbeddedWebBundles(this IServiceCollection services, Assembly assembly)
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));
        RegisterBundlesFromAssembly(assembly ?? throw new ArgumentNullException(nameof(assembly)));

        return services;
    }

    public static WebApplication CreateWebApp(
        string bundleId,
        Action<WebApplicationBuilder>? configureBuilder = null,
        Action<EmbeddedWebOptions>? configureOptions = null)
    {
        if (!Bundles.TryGetValue(bundleId, out var bundle))
            throw new InvalidOperationException($"Bundle '{bundleId}' is not registered.");

        var builder = WebApplication.CreateBuilder();
        configureBuilder?.Invoke(builder);

        var app = builder.Build();
        var options = CreateOptions(configureOptions);

        var provider = bundle.GetFileProviderAsync(CancellationToken.None).GetAwaiter().GetResult();
        ConfigurePipeline(app, provider, bundle, options);

        return app;
    }

    private static EmbeddedWebOptions CreateOptions(Action<EmbeddedWebOptions>? configure)
    {
        var options = new EmbeddedWebOptions();
        configure?.Invoke(options);

        if (options.EnableSpaFallback && string.IsNullOrWhiteSpace(options.SpaFallbackFile))
            options.SpaFallbackFile = options.DefaultFileNames.FirstOrDefault();

        return options;
    }

    private static async Task ConfigurePipelineAsync(
        WebApplication app,
        EmbeddedWebBundle bundle,
        EmbeddedWebOptions options,
        CancellationToken cancellationToken)
    {
        var provider = await bundle.GetFileProviderAsync(cancellationToken).ConfigureAwait(false);
        ConfigurePipeline(app, provider, bundle, options);
    }

    private static void ConfigurePipeline(WebApplication app, IFileProvider provider, EmbeddedWebBundle bundle, EmbeddedWebOptions options)
    {
        app.UseDefaultFiles(CreateDefaultFilesOptions(provider, options));
        app.UseStaticFiles(CreateStaticFileOptions(provider, options));

        if (options.EnableDirectoryBrowsing)
            app.UseDirectoryBrowser(new DirectoryBrowserOptions { FileProvider = provider });

        if (options.EnableSpaFallback && !string.IsNullOrEmpty(options.SpaFallbackFile))
            app.MapFallback("{*path}", context => ServeFileAsync(context, bundle, options.SpaFallbackFile!, options));
    }

    private static DefaultFilesOptions CreateDefaultFilesOptions(IFileProvider provider, EmbeddedWebOptions options)
    {
        var defaultFilesOptions = new DefaultFilesOptions
        {
            RequestPath = "",
            FileProvider = provider
        };
        defaultFilesOptions.DefaultFileNames.Clear();
        foreach (var name in options.DefaultFileNames)
            defaultFilesOptions.DefaultFileNames.Add(name);

        return defaultFilesOptions;
    }

    private static StaticFileOptions CreateStaticFileOptions(IFileProvider provider, EmbeddedWebOptions options)
    {
        var staticOptions = new StaticFileOptions
        {
            FileProvider = provider,
            RequestPath = "",
            ContentTypeProvider = ContentTypeProvider
        };

        var cacheDuration = options.StaticFilesCacheDuration;
        var userCallback = options.OnPrepareResponse;

        if (cacheDuration.HasValue || userCallback != null)
            staticOptions.OnPrepareResponse = context =>
            {
                if (cacheDuration.HasValue)
                {
                    var headers = context.Context.Response.GetTypedHeaders();
                    headers.CacheControl = new()
                    {
                        Public = true,
                        MaxAge = cacheDuration
                    };
                }

                userCallback?.Invoke(context);
            };

        return staticOptions;
    }

    private static async Task ServeFileAsync(HttpContext context, EmbeddedWebBundle bundle, string relativePath, EmbeddedWebOptions options)
    {
        var file = await bundle.TryGetFileAsync(relativePath, context.RequestAborted).ConfigureAwait(false);
        if (file == null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;

            return;
        }

        if (!ContentTypeProvider.TryGetContentType(relativePath, out var contentType))
            contentType = "text/html";

        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = contentType;
        context.Response.ContentLength = file.Length;
        context.Response.Headers["Last-Modified"] = file.LastModified.ToUniversalTime().ToString("R");

        if (options.StaticFilesCacheDuration.HasValue)
        {
            var headers = context.Response.GetTypedHeaders();
            headers.CacheControl = new()
            {
                Public = true,
                MaxAge = options.StaticFilesCacheDuration
            };
        }

        await context.Response.Body.WriteAsync(file.Content.AsMemory(), context.RequestAborted).ConfigureAwait(false);
    }

    private static bool TryExtractBundleId(string resourceName, out string bundleId)
    {
        var index = resourceName.IndexOf(ResourceMarker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            bundleId = string.Empty;

            return false;
        }

        var start = index + ResourceMarker.Length;
        var remainder = resourceName[start..];

        if (remainder.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
        {
            bundleId = remainder[..^7];

            return true;
        }

        if (remainder.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            bundleId = remainder[..^4];

            return true;
        }

        bundleId = string.Empty;

        return false;
    }
}