# Embedded Web Server Toolkit

Sources live under `src/Itexoft/EmbeddedWeb/`; the MSBuild entry point is `src/Itexoft/EmbeddedWeb/Itexoft.targets`
(which re-exports `EmbeddedWebAssets.targets`). This toolkit turns front-end bundles into embedded resources and serves
them through lightweight HTTP endpoints.

Key goals:

- Package any static web assets (React/Vue/Angular/SPAs, plain HTML, etc.) directly into a .NET assembly.
- Spin up one or more self-contained HTTP pipelines at runtime without copying files to disk.
- Support advanced delivery scenarios such as custom archive streams, SPA fallbacks, and multi-host setups.

The feature is split into:

1. **Build-time tooling** (`src/Itexoft/EmbeddedWeb/EmbeddedWebAssets.targets`) that gathers static files into `.zip`
   archives and embeds them as resources. Import `src/Itexoft/EmbeddedWeb/Itexoft.targets` if you reference the sources
   directly.
2. **Runtime API** (`EmbeddedWebServer` in `src/Itexoft/EmbeddedWeb`) that exposes simple entry points to register
   bundles
   and serve them via Kestrel or existing ASP.NET Core pipelines.

---

## Build-Time Configuration

Import the targets (skip if consumed via the packaged `buildTransitive` assets) and add `EmbeddedWebApp` items to the
consuming project (*.csproj) to describe the static assets that should be packaged:

```xml
<Import Project="src/Itexoft/EmbeddedWeb/Itexoft.targets" />

<ItemGroup>
  <EmbeddedWebApp Include="Frontend/Admin/**/*" BundleId="admin" />
  <EmbeddedWebApp Include="Frontend/Portal/**/*" BundleId="portal" />
</ItemGroup>
```

Supported metadata per item:

| Metadata           | Description                                                                                          |
|--------------------|------------------------------------------------------------------------------------------------------|
| `BundleId`         | Logical bundle identifier. Must be unique within the project.                                        |
| `RootDirectory`    | Optional absolute/relative path that should be treated as the root when computing relative file IDs. |
| `CompressionLevel` | One of `Optimal`, `Fastest`, `NoCompression`, `SmallestSize`. Defaults to `Optimal`.                 |

During `Build`/`Publish` the MSBuild task:

1. Collects the files matching each `EmbeddedWebApp`.
2. Produces a `zip` archive per bundle.
3. Embeds the archive into the assembly as an `EmbeddedResource` named `EmbeddedWeb.{BundleId}.zip`.

The targets are distributed via `buildTransitive`, so dependent projects automatically gain the packing capability when
consuming the NuGet package; use the import above when working from source.

---

## Runtime Usage

### 1. Register Embedded Bundles

Most apps only need to scan their own assembly:

```csharp
EmbeddedWebServer.RegisterBundlesFromAssembly(typeof(Program).Assembly);
```

If you need to wire a bundle manually, use the `EmbeddedArchiveSource` helpers:

```csharp
EmbeddedWebServer.RegisterBundle(
    "reports",
    EmbeddedArchiveSource.FromFile("Artifacts/reports.zip"));
```

> Any stream returned by an `IEmbeddedArchiveSource` must be seekable. Non-seekable streams are automatically buffered.

### 2. Start a Standalone HTTP Host

Launch a dedicated Kestrel instance that exposes one bundle:

```csharp
await using var handle = await EmbeddedWebServer.StartAsync(
    bundleId: "portal",
    port: 5050,
    configureBuilder: builder =>
    {
        builder.WebHost.ConfigureKestrel(_ => { /* custom Kestrel options */ });
    },
    configureOptions: options =>
    {
        options.StaticFilesCacheDuration = TimeSpan.FromHours(1);
        options.EnableSpaFallback = true; // default: true
        options.SpaFallbackFile = "index.html";
    });

Console.WriteLine($"Portal available on http://localhost:5050");
await handle.Completion; // awaits shutdown
```

The returned `EmbeddedWebHandle` implements `IAsyncDisposable` and exposes:

- `Urls` (bound addresses),
- `Completion` (shutdown task),
- `StopAsync` for graceful termination.

### 3. Integrate with Existing ASP.NET Core Apps

Map embedded bundles onto an existing WebApplication:

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddEmbeddedWebBundles(typeof(Program).Assembly);

var app = builder.Build();
app.MapEmbeddedWebApp("/admin", "admin");               // Serves SPA under /admin
app.MapEmbeddedWebApp("/portal", "portal", opts =>
{
    opts.EnableSpaFallback = false;                     // Static-only
    opts.DefaultFileNames.Clear();
    opts.DefaultFileNames.Add("home.html");
});

app.Run();
```

`MapEmbeddedWebApp` internally creates a static file pipeline with the registered archive.

### 4. Construct a Reusable WebApplication

When integrating with tests or generic hosts, you can build the pipeline without binding ports:

```csharp
var app = EmbeddedWebServer.CreateWebApp(
    bundleId: "app1",
    configureBuilder: builder => builder.WebHost.UseTestServer());

await app.StartAsync();
var client = app.GetTestClient();
var html = await client.GetStringAsync("/");
```

This is exactly how the integration tests are structured.

---

## Multiple Bundles in One Process

You can register several bundles and spin up independent hosts:

```csharp
EmbeddedWebServer.RegisterBundlesFromAssembly(typeof(Program).Assembly);

await using var admin = await EmbeddedWebServer.StartAsync("admin", 5001);
await using var portal = await EmbeddedWebServer.StartAsync("portal", 5002);
```

Each host maintains its own static file provider, so content is served independently.

---

## SPA Fallback and Static File Options

`EmbeddedWebOptions` governs the static pipeline:

| Property                   | Purpose                                                                   |
|----------------------------|---------------------------------------------------------------------------|
| `DefaultFileNames`         | Files checked by the default files middleware. (defaults to `index.html`) |
| `EnableSpaFallback`        | Whether to rewrite unknown paths to a fallback file (defaults to `true`). |
| `SpaFallbackFile`          | Specific file served for SPA routes (defaults to first default file).     |
| `StaticFilesCacheDuration` | Applies a `Cache-Control` header for static responses.                    |
| `EnableDirectoryBrowsing`  | Exposes directory listings (disabled by default).                         |
| `OnPrepareResponse`        | Hook to customize individual responses.                                   |

Example:

```csharp
EmbeddedWebServer.MapEmbeddedWebApp("/docs", "documentation", options =>
{
    options.EnableSpaFallback = false;
    options.StaticFilesCacheDuration = TimeSpan.FromDays(7);
    options.OnPrepareResponse = ctx =>
        ctx.Context.Response.Headers["X-Docs-Version"] = "2025.10";
});
```

---

## Custom Archive Sources

Use `EmbeddedArchiveSource.FromFactory` when content is loaded dynamically (e.g., from a database or cloud storage):

```csharp
EmbeddedWebServer.RegisterBundle(
    "reports",
    EmbeddedArchiveSource.FromFactory(async ct =>
    {
        var stream = await artifactStorage.OpenStreamAsync("reports.tar.gz", ct);
        return stream; // non-seekable streams are buffered automatically
    }),
    overwrite: true);
```

Archive formats supported:

- `.zip`
- `.tar`
- `.tar.gz`

Files are expanded into memory once per bundle and served from an in-memory provider (`InMemoryArchiveFileProvider`).

---

## Tests

The `src/Itexoft.Tests` project demonstrates end-to-end scenarios:

- Serving bundled assets via `CreateWebApp`.
- SPA fallback behavior.
- Launching standalone Kestrel hosts with custom archives.
- Verifying non-seekable streams and multiple independent hosts.

Run:

```bash
dotnet test src/Itexoft.Tests/Itexoft.Tests.csproj
```

---

## Limitations & Notes

- Bundles are loaded into memory; extremely large archives may require alternative streaming strategies.
- Updating front-end assets requires rebuilding the application.
- Parallel host counts are bounded by process resources (each host spins up a Kestrel instance).
- The MSBuild task currently emits `.zip` files only; `.tar.gz` sources must be registered manually at runtime.
