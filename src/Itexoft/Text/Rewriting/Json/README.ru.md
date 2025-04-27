# Переписчик JSON (фреймовая обработка и типизированные проекции)

Этот модуль отвечает за переписывание JSON‑структур: замена значений, переименование полей, проверка, извлечение данных и конвертация в строго типизированные объекты. Он работает по кускам входа, но каждый JSON‑фрейм полностью буферизуется в памяти перед отдачей результата.


## Модель: фреймы, а не бесконечный поток

Для JSON у библиотеки другая модель, чем для обычного текста:

- каждое JSON‑сообщение обрабатывается как **фрейм**;
- внутри фрейма можно изменить структуру (поля, значения, вложенные объекты);
- результат фрейма отдаётся после завершения парсинга этого JSON.

Фрейм может попадать в систему разными способами:

- отдельно стоящий JSON‑документ;
- JSON‑объект внутри SSE события;
- JSON в протоколе с префиксом, длиной, разделителем и т.п.
- `JsonRewriteWriter` держит весь фрейм в памяти; выбирайте разбиение на фреймы так, чтобы они помещались в доступный объём.

Библиотека даёт два уровня API:

1. Низкоуровневый:
   - `JsonRewritePlanBuilder`
   - `JsonRewritePlan`
   - `JsonRewriteWriter` / `JsonRewriteReader`
   - `JsonRewriteOptions`

2. Высокоуровневый:
   - `JsonKernel<THandlers>`
   - `JsonSession<THandlers>`
   - `JsonDsl<THandlers>`
   - атрибуты правил и типизированных проекций


## Быстрый старт: маскирование токена и извлечение usage

Простой пример: нужно маскировать поле `token` и собирать usage‑метрики из ответа API.

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

- `ReplaceValue` меняет значение по JSON Pointer.
- `Capture` вызывает делегат при встрече указанного поля.
- `JsonRewriteReader` и `JsonRewriteWriter` работают фреймами и не держат весь поток в памяти.


## JsonKernel и JsonSession

Высокоуровневый API с поддержкой обработчиков состояния и DSL:

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

Сессия принимает чанки текста (`Write`/`WriteAsync`) и выдаёт переписанный JSON в связанный `TextWriter` при `Commit`. Один `JsonKernel<THandlers>` можно использовать для множества сессий.


## DSL для JSON: JsonDsl

DSL даёт удобный способ описывать правила высокого уровня.

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

Кратко по правилам:
- `ReplaceValue` рендерит литерал вместо найденного значения.
- `RenameProperty` переименовывает поле по указателю.
- `Require`/`RequireAsync` валидируют наличие/значение и кидают `FormatException` при нарушении.
- `Capture`/`CaptureAsync` пробрасывают текстовое значение в обработчики.
- `CaptureValue`/`CaptureValueAsync` конвертируют строку в тип через конвертер или регистр.
- `CaptureObject`/`CaptureMany` материализуют типизированные объекты по плану проекции.
- `ReplaceInString` работает по указателю или через пару делегатов `predicate + replacer` (`JsonStringContext` на входе), есть синхронные и асинхронные варианты.

Пример: очистка ответа API от секретов и логирование id.

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


## Типизированные проекции: JSON → ваши классы

Для сложных протоколов неудобно оперировать строками по путям вроде `"/usage/output_tokens_details/reasoning_tokens"`. Проще собрать один объект:

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

Идея: описать, как JSON‑поля сопоставляются свойствам объекта, а затем получать готовые экземпляры `ResponseMeta` прямо из потока JSON.

### Стратегии имён: JsonPropertyNameStyle

По умолчанию имена JSON‑полей выводятся из имён свойств:

```csharp
public enum JsonPropertyNameStyle
{
    Exact,
    CamelCase,
    SnakeCase
}
```

Примеры:

- `Exact`  
  `TestCase` → `TestCase`

- `CamelCase`  
  `TestCase` → `testCase`

- `SnakeCase`  
  `Test` → `test`  
  `TestCase` → `test_case`  
  `InputTokens` → `input_tokens`

Опции задаются через:

```csharp
public sealed class JsonProjectionOptions
{
    public JsonPropertyNameStyle PropertyNameStyle { get; init; } = JsonPropertyNameStyle.SnakeCase;
}
```

### JsonProjectionPlan<T>.FromConventions

Авто‑проекция по соглашениям:

```csharp
public sealed class JsonProjectionPlan<T>
    where T : class
{
    public static JsonProjectionPlan<T> FromConventions(
        JsonProjectionOptions? options = null);
}
```

Пример: авто‑маппинг `ResponseMeta` c `SnakeCase`:

- `ResponseMeta.Id` → `"/id"`
- `ResponseMeta.Model` → `"/model"`
- `ResponseMeta.Status` → `"/status"`
- `ResponseMeta.CreatedAt` → `"/created_at"`
- `ResponseMeta.Usage.InputTokens` → `"/usage/input_tokens"`
- `ResponseMeta.Usage.CachedTokens` → `"/usage/input_tokens_details/cached_tokens"` при явной настройке
- `ResponseMeta.Usage.OutputTokens` → `"/usage/output_tokens"`
- `ResponseMeta.Usage.TotalTokens` → `"/usage/total_tokens"`


### JsonProjectionBuilder<T>: явное описание

Если нужно точное управление путями, можно воспользоваться builder‑ом:

```csharp
public sealed class JsonProjectionBuilder<T>
    where T : class, new()
{
    public JsonProjectionBuilder(JsonProjectionOptions? options = null);

    public JsonProjectionBuilder<T> Map<TValue>(
        Expression<Func<T, TValue>> property,
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

Пример проекции для метаданных ответа:

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

- `Map(target, source, converter)` строит JSON Pointer по цепочке членов из `source` (учитывая `JsonProjectionOptions.PropertyNameStyle`), а в `target` пишет по этому пути; выражение должно быть цепочкой полей/свойств, иначе будет `InvalidOperationException`.
- Для одной цели допустимо несколько `Map` — правила применяются независимо.


## CaptureObject и CaptureMany: работа с готовыми объектами

Новый уровень удобства в `JsonRewritePlanBuilder`:

```csharp
public sealed class JsonRewritePlanBuilder
{
    public JsonRewritePlanBuilder CaptureObject<T>(
        string rootPointer,
        JsonProjectionPlan<T> projection,
        Action<T> onObject)
        where T : class;

    public JsonRewritePlanBuilder CaptureObject<THandlers, T>(
        string rootPointer,
        JsonProjectionPlan<T> projection,
        Action<THandlers, T> onObject)
        where THandlers : class
        where T : class;

    public JsonRewritePlanBuilder CaptureObject<T>(
        string rootPointer,
        Action<T> onObject,
        JsonProjectionOptions? options = null)
        where T : class, new();

    public JsonRewritePlanBuilder CaptureObject<THandlers, T>(
        string rootPointer,
        Action<THandlers, T> onObject,
        JsonProjectionOptions? options = null)
        where THandlers : class
        where T : class, new();

    public JsonRewritePlanBuilder CaptureMany<T>(
        string rootPointer,
        JsonProjectionPlan<T> projection,
        Action<T> onItem)
        where T : class;

    public JsonRewritePlanBuilder CaptureMany<THandlers, T>(
        string rootPointer,
        JsonProjectionPlan<T> projection,
        Action<THandlers, T> onItem)
        where THandlers : class
        where T : class;

    public JsonRewritePlanBuilder CaptureValue<T>(
        string pointer,
        Action<T> onValue,
        Func<string, T>? converter = null);

    public JsonRewritePlanBuilder CaptureValue<THandlers, T>(
        string pointer,
        Action<THandlers, T> onValue,
        Func<string, T>? converter = null)
        where THandlers : class;
}
```

### Пример: сбор метаданных из корня и из поля response

Старая портянка из десятков `Capture("/usage/...", v => ...)` превращается в аккуратную конфигурацию:

```csharp
public sealed class ResponseCollector
{
    public string Id;
    public string Model;
    public string Status;
    public long? CreatedAt;
    public UsageInfo Usage { get; } = new();
}

public static class CaptureObjectExample
{
    public static JsonRewritePlan BuildPlan(JsonProjectionPlan<ResponseMeta> projection)
    {
        var collector = new ResponseCollector();
        var builder = new JsonRewritePlanBuilder();

        builder.CaptureObject("/", projection, meta =>
        {
            collector.Id ??= meta.Id;
            collector.Model ??= meta.Model;
            collector.Status ??= meta.Status;
            collector.CreatedAt ??= meta.CreatedAt;

            if (collector.Usage.InputTokens == 0)
                collector.Usage.InputTokens = meta.Usage.InputTokens;

            if (collector.Usage.CachedTokens == 0)
                collector.Usage.CachedTokens = meta.Usage.CachedTokens;

            if (collector.Usage.OutputTokens == 0)
                collector.Usage.OutputTokens = meta.Usage.OutputTokens;

            if (collector.Usage.TotalTokens == 0)
                collector.Usage.TotalTokens = meta.Usage.TotalTokens;

            if (collector.Usage.ReasoningTokens == 0)
                collector.Usage.ReasoningTokens = meta.Usage.ReasoningTokens;
        });

        builder.CaptureObject("/response", projection, meta =>
        {
            collector.Id ??= meta.Id;
            collector.Model ??= meta.Model;
            collector.Status ??= meta.Status;
            collector.CreatedAt ??= meta.CreatedAt;

            if (collector.Usage.InputTokens == 0)
                collector.Usage.InputTokens = meta.Usage.InputTokens;

            if (collector.Usage.CachedTokens == 0)
                collector.Usage.CachedTokens = meta.Usage.CachedTokens;

            if (collector.Usage.OutputTokens == 0)
                collector.Usage.OutputTokens = meta.Usage.OutputTokens;

            if (collector.Usage.TotalTokens == 0)
                collector.Usage.TotalTokens = meta.Usage.TotalTokens;

            if (collector.Usage.ReasoningTokens == 0)
                collector.Usage.ReasoningTokens = meta.Usage.ReasoningTokens;
        });

        builder.Capture("/output/*", v => Console.WriteLine(v));
        builder.Capture("/response/output/*", v => Console.WriteLine(v));

        return builder.Build();
    }
}
```

Стриминговость не ломается: JSON всё так же обрабатывается по фреймам, а объект `ResponseMeta` собирается после парсинга одного JSON‑документа.


## CaptureValue<T>: типизированные скаляры

Вместо ручного `Capture("/created_at", v => ParseLong(v))`:

```csharp
planBuilder.CaptureValue("/created_at", v => createdAt ??= v, ParseLong);
planBuilder.CaptureValue("/usage/total_tokens", v => usageTotal ??= v, ParseInt);
```

Тип `T` определяется по делегату, а конвертер можно либо передать явно, либо использовать стандартные реализации `IJsonScalarConverter<T>`.


## Декларативный подход: атрибуты DTO и методов

### JsonPointerAttribute

Если базового авто‑маппинга по имени недостаточно, можно задать путь явно на уровне модели:

```csharp
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class JsonPointerAttribute : Attribute
{
    public JsonPointerAttribute(string path)
    {
        Path = path;
    }

    public string Path { get; }
    public Type? Converter { get; set; }
}
```

Пример:

```csharp
public sealed class UsageInfo
{
    [JsonPointer("/usage/input_tokens")]
    public int InputTokens { get; set; }

    [JsonPointer("/usage/input_tokens_details/cached_tokens")]
    public int CachedTokens { get; set; }

    [JsonPointer("/usage/output_tokens")]
    public int OutputTokens { get; set; }

    [JsonPointer("/usage/total_tokens")]
    public int TotalTokens { get; set; }

    [JsonPointer("/usage/output_tokens_details/reasoning_tokens")]
    public int ReasoningTokens { get; set; }
}

public sealed class ResponseMeta
{
    [JsonPointer("/id")]
    public string Id { get; set; }

    [JsonPointer("/model")]
    public string Model { get; set; }

    [JsonPointer("/status")]
    public string Status { get; set; }

    [JsonPointer("/created_at", Converter = typeof(LongConverter))]
    public long CreatedAt { get; set; }

    public UsageInfo Usage { get; set; } = new();
}

public sealed class LongConverter : IJsonScalarConverter<long>
{
    public long Convert(string value) => long.Parse(value, CultureInfo.InvariantCulture);
}
```

### JsonCaptureObjectAttribute

Метод‑обработчик можно связать с моделью и корнем JSON:

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

Пример:

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
```

Сборка плана:

```csharp
var kernel = JsonKernel<StreamHandlers>.CompileFromAttributes(typeof(StreamRules));
```


## JsonRuntimeOptions и фрейминг

Опции исполнения для `JsonSession<THandlers>`:

```csharp
public sealed class JsonRuntimeOptions
{
    public string? UnwrapPrefix { get; init; }
    public bool PrefixRequired { get; init; }
    public Func<string, string?>? OnMalformedJson { get; init; }
    public IJsonFraming? Framing { get; init; }
}
```

- `UnwrapPrefix` и `PrefixRequired`  
  Удобны для SSE‑подобных протоколов вида `data: { json }`.

- `OnMalformedJson`  
  Позволяет аккуратно обработать «почти JSON» и попытаться его починить.

- `IJsonFraming`  
  Интерфейс стратегии фрейминга (по разделителю, по длине, по своему протоколу).


## Низкоуровневый API

Если нужен полный контроль над планом, можно работать напрямую через `JsonRewritePlanBuilder` и `JsonRewriteWriter`, без kernel:

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


## Что в итоге даёт модуль JSON

- Переписывание JSON по указателям (JSON Pointer) без ручного парсинга.
- Проверку структуры и обязательных полей.
- Стриминговую обработку фреймов без хранения всего потока.
- Два стиля настройки:
  - DSL и атрибуты по строкам.
  - типизированные проекции `JsonProjectionPlan<T>` с авто‑маппингом по именам.
- Поддержку типизированных обработчиков через `JsonKernel<THandlers>`.

Для простых задач достаточно пары `ReplaceValue` и `Capture`.  
Для сложных протоколов удобно собрать один раз проекцию в модель и дальше работать с нормальными объектами, а не с путями вида `"/usage/output_tokens_details/reasoning_tokens"`.
