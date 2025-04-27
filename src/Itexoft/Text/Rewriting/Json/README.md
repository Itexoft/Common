# Streaming JSON rewriter and typed projections

This module rewrites JSON structures: replace values, rename fields, validate, extract data, and convert to strongly typed objects. It works over chunked input, but each JSON frame is buffered in memory before it is emitted.


## Model: frames, not an endless stream

For JSON the library uses a frame model:

- each JSON message is treated as a **frame**;
- inside a frame you can change structure (fields, values, nested objects);
- the frame result is emitted after parsing finishes.

Frames may arrive as:

- a standalone JSON document;
- a JSON object inside an SSE event;
- JSON in a protocol with prefix, length, delimiter, etc.
- `JsonRewriteWriter` keeps the entire frame in memory; keep frames reasonably sized when choosing framing boundaries.

Two API levels:

1. Low level:
   - `JsonRewritePlanBuilder`
   - `JsonRewritePlan`
   - `JsonRewriteWriter` / `JsonRewriteReader`
   - `JsonRewriteOptions`

2. High level:
   - `JsonKernel<THandlers>`
   - `JsonSession<THandlers>`
   - `JsonDsl<THandlers>`
   - rule and projection attributes


## Quick start: mask token and extract usage

Simple example: mask the `token` field and collect usage metrics from an API response.

```csharp
public sealed class UsageCollector
{
    public int? TotalTokens;
}

public static class JsonQuickStart
{
    public static void Run(TextReader input, TextWriter output)
    {
        var planBuilder = new JsonRewritePlanBuilder();

        planBuilder.ReplaceValue("/token", "***");
        planBuilder.Capture("/usage/total_tokens", v => Console.WriteLine("total_tokens=" + v));

        var plan = planBuilder.Build();

        var options = new JsonRewriteOptions
        {
            UnwrapPrefix = "data: ",
            PrefixRequired = false
        };

        using var writer = new JsonRewriteWriter(output, plan, options);
        var reader = new JsonRewriteReader(input);

        reader.CopyTo(writer);
    }
}
```

- `ReplaceValue` changes value by JSON Pointer.
- `Capture` invokes a delegate when the field is encountered.
- `JsonRewriteReader` and `JsonRewriteWriter` operate on frames and do not keep the whole stream in memory.


## JsonKernel and JsonSession

High level API with handler scope and DSL:

```csharp
public sealed class JsonKernel<THandlers>
{
    public static JsonKernel<THandlers> Compile(
        Action<JsonDsl<THandlers>> configure);

    public static JsonKernel<THandlers> CompileFromAttributes(
        params Type[] ruleContainers);

    public JsonSession<THandlers> CreateSession(
        TextWriter output,
        THandlers handlers,
        JsonRuntimeOptions? options = null);
}

public sealed class JsonSession<THandlers> : IAsyncDisposable, IDisposable
{
    public void Write(ReadOnlySpan<char> chunk);
    public ValueTask WriteAsync(ReadOnlyMemory<char> chunk, CancellationToken cancellationToken = default);

    public void Commit();
    public ValueTask CommitAsync(CancellationToken cancellationToken = default);

    public void Reset();
}
```

Session accepts chunks (`Write`/`WriteAsync`) and emits rewritten JSON to the linked `TextWriter` on `Commit`. One `JsonKernel<THandlers>` may be reused for many sessions.


## DSL for JSON: JsonDsl

DSL provides a convenient way to describe high-level rules.

```csharp
public sealed class JsonDsl<THandlers>
{
    public JsonDsl<THandlers> ReplaceValue(
        string pointer,
        string replacement,
        string? name = null);

    public JsonDsl<THandlers> RenameProperty(
        string pointer,
        string newName,
        string? name = null);

    public JsonDsl<THandlers> Require(
        string pointer,
        Func<THandlers, string, bool>? predicate = null,
        string? errorMessage = null,
        string? name = null);

    public JsonDsl<THandlers> RequireAsync(
        string pointer,
        Func<THandlers, string, ValueTask<bool>> predicateAsync,
        string? errorMessage = null,
        string? name = null);

    public JsonDsl<THandlers> Capture(
        string pointer,
        Action<THandlers, string> onValue,
        string? name = null);

    public JsonDsl<THandlers> CaptureAsync(
        string pointer,
        Func<THandlers, string, ValueTask> onValueAsync,
        string? name = null);

    public JsonDsl<THandlers> CaptureValue<T>(
        string pointer,
        Action<THandlers, T> onValue,
        Func<string, T>? converter = null,
        string? name = null);

    public JsonDsl<THandlers> CaptureValueAsync<T>(
        string pointer,
        Func<THandlers, T, ValueTask> onValueAsync,
        Func<string, T>? converter = null,
        string? name = null);

    public JsonDsl<THandlers> CaptureObject<T>(
        string rootPointer,
        JsonProjectionPlan<T> projection,
        Action<THandlers, T> onObject,
        string? name = null)
        where T : class;

    public JsonDsl<THandlers> CaptureObject<T>(
        string rootPointer,
        Action<THandlers, T> onObject,
        JsonProjectionOptions? options = null,
        string? name = null)
        where T : class, new();

    public JsonDsl<THandlers> CaptureMany<T>(
        string rootPointer,
        JsonProjectionPlan<T> projection,
        Action<THandlers, T> onItem,
        string? name = null)
        where T : class;

    public JsonDsl<THandlers> CaptureMany<T>(
        string rootPointer,
        Action<THandlers, T> onItem,
        JsonProjectionOptions? options = null,
        string? name = null)
        where T : class, new();

    public JsonDsl<THandlers> ReplaceInString(
        string pointer,
        Func<THandlers, string, string> replacer,
        string? name = null);

    public JsonDsl<THandlers> ReplaceInString(
        string pointer,
        Func<THandlers, JsonStringContext, ValueTask<string>> replacerAsync,
        string? name = null);

    public JsonDsl<THandlers> ReplaceInString(
        Func<THandlers, JsonStringContext, bool> predicate,
        Func<THandlers, JsonStringContext, string> replacer,
        string? name = null);

    public JsonDsl<THandlers> ReplaceInString(
        Func<THandlers, JsonStringContext, ValueTask<bool>> predicate,
        Func<THandlers, JsonStringContext, ValueTask<string>> replacer,
        string? name = null);

    public JsonDsl<THandlers> ReplaceInString(
        string pointer,
        Func<THandlers, JsonStringContext, bool> predicate,
        Func<THandlers, JsonStringContext, string> replacer,
        string? name = null);

    public JsonDsl<THandlers> ReplaceInString(
        string pointer,
        Func<THandlers, JsonStringContext, ValueTask<bool>> predicate,
        Func<THandlers, JsonStringContext, ValueTask<string>> replacer,
        string? name = null);

    public void Group(string group, Action<JsonDsl<THandlers>> configure);
}
```

Briefly:
- `ReplaceValue` renders a literal instead of the found value.
- `RenameProperty` renames a field at pointer.
- `Require`/`RequireAsync` validate presence/value and throw `FormatException` on violation.
- `Capture`/`CaptureAsync` forward raw value to handlers.
- `CaptureValue`/`CaptureValueAsync` convert string to type via converter or registry.
- `CaptureObject`/`CaptureMany` materialize typed objects via projection plan.
- `ReplaceInString` works by pointer or via `predicate + replacer` (`JsonStringContext` input), with sync and async variants.
- `Group` nests rules under a feature flag.

Example: strip secrets and log id.

```csharp
public sealed class ApiHandlers
{
    public string LastId;
}

public static class JsonDslExample
{
    public static async Task RunAsync(TextReader input, TextWriter output)
    {
        var kernel = JsonKernel<ApiHandlers>.Compile(rules =>
        {
            rules.ReplaceValue("/token", "***", "mask-token");
            rules.Capture("/id", (h, v) => h.LastId = v);
            rules.ReplaceInString("/payload", (h, v) => v.Replace("secret=", "secret=***"));

            rules.ReplaceInString(
                ctx => ctx.Pointer.StartsWith("/messages/", StringComparison.Ordinal) && ctx.Value.Contains("secret=", StringComparison.Ordinal),
                ctx => ctx.Value.Replace("secret=", "secret=***", StringComparison.Ordinal),
                name: "mask-secret");
        });

        var handlers = new ApiHandlers();

        await using var session = kernel.CreateSession(output, handlers, new JsonRuntimeOptions
        {
            UnwrapPrefix = "data: ",
            PrefixRequired = false
        });

        var buffer = new char[4096];
        int read;

        while ((read = await input.ReadAsync(buffer, 0, buffer.Length)) > 0)
        {
            await session.WriteAsync(buffer.AsMemory(0, read));
        }

        await session.CommitAsync();
    }
}
```


## Typed projections: JSON → your classes

For complex protocols it is easier to collect an object than to use pointers like `"/usage/output_tokens_details/reasoning_tokens"`.

```csharp
public sealed class UsageInfo
{
    public int InputTokens { get; set; }
    public int CachedTokens { get; set; }
    public int OutputTokens { get; set; }
    public int TotalTokens { get; set; }
    public int ReasoningTokens { get; set; }
}

public sealed class ResponseMeta
{
    public string Id { get; set; }
    public string Model { get; set; }
    public string Status { get; set; }
    public long CreatedAt { get; set; }
    public UsageInfo Usage { get; set; } = new();
}
```

Idea: describe how JSON fields map to object properties, then get ready `ResponseMeta` instances from the JSON stream.


### Naming strategies: JsonPropertyNameStyle

By default JSON field names are derived from property names:

```csharp
public enum JsonPropertyNameStyle
{
    Exact,
    CamelCase,
    SnakeCase
}
```

Examples:

- `Exact`  
  `TestCase` → `TestCase`

- `CamelCase`  
  `TestCase` → `testCase`

- `SnakeCase`  
  `Test` → `test`  
  `TestCase` → `test_case`  
  `InputTokens` → `input_tokens`


### JsonProjectionBuilder<T>: explicit description

If you need precise control over paths, use the builder:

```csharp
public sealed class JsonProjectionBuilder<T>
    where T : class, new()
{
    public JsonProjectionBuilder(JsonProjectionOptions? options = null);

    public JsonProjectionBuilder<T> Map<TValue>(
        Expression<Func<T, TValue>> target,
        string pointer,
        Func<string, TValue>? converter = null);

    public JsonProjectionBuilder<T> Map<TValue>(
        Expression<Func<T, TValue>> target,
        Expression<Func<T, TValue>> source,
        Func<string, TValue>? converter = null);

    public JsonProjectionBuilder<T> MapObject<TChild>(
        Expression<Func<T, TChild>> property,
        JsonProjectionPlan<TChild> childPlan)
        where TChild : class;

    public JsonProjectionPlan<T> Build();
}
```

Projection example for response metadata:

```csharp
public static class MetaProjections
{
    public static JsonProjectionPlan<ResponseMeta> Build()
    {
        var usage = new JsonProjectionBuilder<UsageInfo>()
            .Map(x => x.InputTokens, x => x.InputTokens, ParseInt)
            .Map(x => x.CachedTokens, x => x.CachedTokens, ParseInt)
            .Map(x => x.OutputTokens, x => x.OutputTokens, ParseInt)
            .Map(x => x.TotalTokens, x => x.TotalTokens, ParseInt)
            .Map(x => x.ReasoningTokens, x => x.ReasoningTokens, ParseInt)
            .Build();

        var meta = new JsonProjectionBuilder<ResponseMeta>()
            .Map(x => x.Id, x => x.Id)
            .Map(x => x.Model, x => x.Model)
            .Map(x => x.Status, x => x.Status)
            .Map(x => x.CreatedAt, x => x.CreatedAt, ParseLong)
            .MapObject(x => x.Usage, usage)
            .Build();

        return meta;
    }

    static int ParseInt(string value) => int.Parse(value, CultureInfo.InvariantCulture);
    static long ParseLong(string value) => long.Parse(value, CultureInfo.InvariantCulture);
}
```

Notes:
- `Map(target, source, converter)` builds JSON Pointer from a member chain in `source`, respecting `JsonProjectionOptions.PropertyNameStyle`. Expression must be a pure chain of properties/fields; otherwise `InvalidOperationException` is thrown.
- Multiple sources for one target are allowed; rules apply independently.


## CaptureObject и CaptureMany: работа с готовыми объектами

`JsonRewritePlanBuilder` exposes projecting objects when a path completes:

```csharp
public sealed class JsonRewritePlanBuilder
{
    public JsonRewritePlanBuilder CaptureObject<T>(
        string rootPointer,
        JsonProjectionPlan<T> projection,
        Action<THandlers, T> onObject,
        string? name = null)
        where T : class;

    public JsonRewritePlanBuilder CaptureObject<T>(
        string rootPointer,
        Action<THandlers, T> onObject,
        JsonProjectionOptions? options = null,
        string? name = null)
        where T : class, new();

    public JsonRewritePlanBuilder CaptureMany<T>(
        string rootPointer,
        JsonProjectionPlan<T> projection,
        Action<THandlers, T> onItem,
        string? name = null)
        where T : class;

    public JsonRewritePlanBuilder CaptureMany<T>(
        string rootPointer,
        Action<THandlers, T> onItem,
        JsonProjectionOptions? options = null,
        string? name = null)
        where T : class, new();
}
```

Example:

```csharp
rules.CaptureObject("/response", JsonProjectionPlan<ResponseMeta>.FromConventions(), (h, m) => h.Response = m);
rules.CaptureMany("/items", (h, item) => h.Items.Add(item));
```

- For arrays, each element under the root pointer is projected separately.
- Overloads accept explicit `JsonProjectionPlan<T>` or build it via conventions; `new()` is required for auto mode.
- Conversion errors include type/pointer/value.


## JsonRuntimeOptions and framing

Runtime options for `JsonSession<THandlers>`:

```csharp
public sealed class JsonRuntimeOptions
{
    public string? UnwrapPrefix { get; init; }
    public bool PrefixRequired { get; init; }
    public Func<string, string?>? OnMalformedJson { get; init; }
    public IJsonFraming? Framing { get; init; }
}
```

- `UnwrapPrefix` / `PrefixRequired` are handy for SSE-like protocols (`data: { json }`).
- `OnMalformedJson` lets you handle “almost JSON” and try to fix it.
- `IJsonFraming` defines framing strategy (delimiter-based, length-prefixed, custom protocol).


## Low-level API

If you need full control over the plan, use `JsonRewritePlanBuilder` and `JsonRewriteWriter` directly:

```csharp
public static class LowLevelJsonExample
{
    public static void Run(TextReader input, TextWriter output)
    {
        var planBuilder = new JsonRewritePlanBuilder();

        planBuilder.ReplaceValue("/token", "***");
        planBuilder.Require("/id");
        planBuilder.Capture("/usage/total_tokens", v => Console.WriteLine("total=" + v));

        var plan = planBuilder.Build();

        var options = new JsonRewriteOptions
        {
            UnwrapPrefix = "data: ",
            PrefixRequired = false
        };

        using var writer = new JsonRewriteWriter(output, plan, options);
        var reader = new JsonRewriteReader(input);

        reader.CopyTo(writer);
    }
}
```

This level is useful when:

- you already have your own framing/wrapper,
- you want to embed the engine without `THandlers`,
- you prefer manual control instead of attributes/DSL.


## Attributes

`JsonPointerAttribute` on properties/fields overrides the auto pointer and lets you specify a converter type (`IJsonScalarConverter<TProperty>`). Missing/invalid converters throw during plan compilation.

`JsonCaptureObjectAttribute` on static methods wires capture handlers:

```csharp
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class JsonCaptureObjectAttribute : Attribute
{
    public JsonCaptureObjectAttribute(string rootPointer, Type modelType)
    {
        RootPointer = rootPointer;
        ModelType = modelType;
    }

    public string RootPointer { get; }
    public Type ModelType { get; }
}
```

Example with neutral names:

```csharp
public sealed class StreamHandlers
{
    public string CreatedId;
    public string CreatedModel;
    public string CreatedStatus;
    public long? CreatedAt;
    public UsageInfo Usage { get; } = new();
}

public static class StreamRules
{
    [JsonCaptureObject("/", typeof(ResponseMeta))]
    [JsonCaptureObject("/response", typeof(ResponseMeta))]
    public static void OnMeta(StreamHandlers h, ResponseMeta meta)
    {
        h.CreatedId ??= meta.Id;
        h.CreatedModel ??= meta.Model;
        h.CreatedStatus ??= meta.Status;
        h.CreatedAt ??= meta.CreatedAt;

        if (h.Usage.InputTokens == 0)
            h.Usage.InputTokens = meta.Usage.InputTokens;

        if (h.Usage.CachedTokens == 0)
            h.Usage.CachedTokens = meta.Usage.CachedTokens;

        if (h.Usage.OutputTokens == 0)
            h.Usage.OutputTokens = meta.Usage.OutputTokens;

        if (h.Usage.TotalTokens == 0)
            h.Usage.TotalTokens = meta.Usage.TotalTokens;

        if (h.Usage.ReasoningTokens == 0)
            h.Usage.ReasoningTokens = meta.Usage.ReasoningTokens;
    }
}

var kernel = JsonKernel<StreamHandlers>.CompileFromAttributes(typeof(StreamRules));
```

The attribute compiler scans model types for `JsonPointerAttribute`, builds `JsonProjectionPlan<T>` (explicit attributes win, the rest use the naming style from `JsonProjectionOptions`), caches plans per model type, and adds `CaptureObject<THandlers, TModel>` rules to the DSL.


## Error handling and validation

- Invalid JSON yields `FormatException` unless `OnMalformedJson` repairs the text.
- `Require` rules throw with a custom message when missing or predicate fails.
- Projection/capture conversion errors include type, pointer, and raw value.
- Attribute mismatches (wrong converter type, wrong handler signatures) throw during plan compilation.


## What is not supported

- No third-party JSON parsers or dynamic/JObject.
- No wildcard pointers in core rules (explicit absolute pointers only).
- Streaming behavior is preserved: projections and captures do not alter framing boundaries of `JsonRewriteWriter`.


## Choosing an approach

- If you need to quickly attach rules to streams: use `JsonKernel<THandlers>` and DSL.
- If you want declarative style close to code: use attributes.
- If you are building your own hosting: use `JsonRewritePlanBuilder` and `JsonRewriteWriter`.

Guarantee: **any JSON is emitted as soon as its parts become stable for all rules**, with no DOM and bounded buffering.
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
<!-- pad -->
