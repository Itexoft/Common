# Стриминговый переписчик текста

Этот модуль отвечает за «живое» переписывание текстовых потоков: вход идёт чанками, правила срабатывают на лету, а выход пишется сразу, как только становится понятно, что конкретный кусок текста больше не изменится.

Основные сценарии:
- очистка логов и трассировок от секретов и персональных данных
- фильтрация пользовательского ввода в реальном времени (чат, консоль, стрим)
- маскирование токенов, паролей, идентификаторов
- легковесные преобразования текстовых протоколов без полной буферизации

Библиотека не парсит язык целиком. Она умеет очень эффективно находить и переписывать фрагменты текста по правилам, не держа весь поток в памяти.


## Ключевая идея: безопасный префикс и хвост неопределённости

Поток на каждом шаге делится на две части:

- безопасный префикс  
  Часть текста, которая уже **точно** не изменится ни одним правилом. Её можно сразу записывать в выходной `TextWriter`.

- хвост неопределённости  
  Последние символы, которые ещё могут оказаться концом совпадения или его продолжением. Их нужно временно держать в памяти.

Для каждого правила заранее известна максимальная длина просмотра (литерал, regex, пользовательский matching). Из этих ограничений движок вычисляет максимальный размер хвоста. Всё, что левее, становится «безопасным префиксом» и немедленно пишется в выход.

Итого:  
Задержка и потребление памяти зависят не от длины входного текста, а от **глубины контекста**, требуемого правилами.


## Входные точки: что вам скорее всего нужно

На практике используются два уровня API:

1. Высокоуровневый:
   - `TextKernel<THandlers>`
   - `TextSession<THandlers>`
   - `TextDsl<THandlers>`
   - атрибуты правил (декларативный подход)

2. Низкоуровневый:
   - `TextRewritePlanBuilder`
   - `TextRewritePlan`
   - `TextRewriteWriter` / `TextRewriteReader`
   - `TextRewriteOptions`

Для большинства сценариев достаточно **kernel + DSL / атрибуты**. Низкоуровневый API нужен, если вы пишете свою обвязку или хотите полный контроль над планом.


## Быстрый старт: фильтр чата через DSL

Пример: поток сообщений, где нужно маскировать слово «пароль» и вырезать e‑mail‑адреса из логов.

```csharp
public sealed class ChatHandlers
{
    public int HiddenPasswords;
    public int HiddenEmails;
}

public static class ChatTextExample
{
    public static async Task RunAsync(TextWriter output)
    {
        var kernel = TextKernel<ChatHandlers>.Compile(rules =>
        {
            rules.Literal("пароль:", StringComparison.OrdinalIgnoreCase, "mask-password")
                .Replace("пароль: [скрыто]")
                .Hook((h, id, match) => h.HiddenPasswords++);

            rules.Regex(@"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+", 128, "email")
                .Remove((h, id, match) => h.HiddenEmails++);
        });

        var handlers = new ChatHandlers();

        await using var session = kernel.CreateSession(output, handlers, new TextRuntimeOptions
        {
            RightWriteBlockSize = 0,
            FlushBehavior = FlushBehavior.PreserveMatchTail
        });

        await session.WriteAsync("Привет, мой пароль: 1234, почта: user@example.com");
        await session.WriteAsync("
Вот ещё одно письмо: admin@company.local");
        await session.FlushAsync();
    }
}
```

Что здесь происходит:
- `Compile` строит неизменяемый план из описания правил.
- `CreateSession` создаёт сессию обработки для конкретного `TextWriter` и `ChatHandlers`.
- `WriteAsync` можно вызывать сколько угодно раз, любыми чанками.
- Как только часть текста становится безопасным префиксом, она сразу уходит в `output`.


## Модель исполнения: kernel и session

Основные типы:

```csharp
public sealed class TextKernel<THandlers>
{
    public static TextKernel<THandlers> Compile(
        Action<TextDsl<THandlers>> configure,
        TextCompileOptions? options = null);

    public static TextKernel<THandlers> CompileFromAttributes(
        params Type[] ruleContainers);

    public TextSession<THandlers> CreateSession(
        TextWriter output,
        THandlers handlers,
        TextRuntimeOptions? options = null);
}

public sealed class TextSession<THandlers> : IAsyncDisposable, IDisposable
{
    public FilterMetrics Metrics { get; }

    public void Write(ReadOnlySpan<char> chunk);
    public ValueTask WriteAsync(ReadOnlyMemory<char> chunk, CancellationToken cancellationToken = default);

    public void Flush();
    public ValueTask FlushAsync(CancellationToken cancellationToken = default);
}
```

- `TextKernel<THandlers>` компилируется один раз и может использоваться во многих потоках.
- `TextSession<THandlers>` создаётся на один конкретный поток вывода.
- `THandlers` содержит состояние и логику обработки (счётчики, машины состояний, кеши протокола).


## DSL для текста: TextDsl и TextRuleBuilder

DSL строит правила поверх уже готового движка. Основные методы:

```csharp
public sealed class TextDsl<THandlers>
{
    public TextRuleBuilder<THandlers> Literal(
        string pattern,
        StringComparison comparison = StringComparison.Ordinal,
        string? name = null);

    public TextRuleBuilder<THandlers> Regex(
        string pattern,
        int maxMatchLength,
        RegexOptions options = RegexOptions.CultureInvariant | RegexOptions.Compiled,
        string? name = null);

    public TextRuleBuilder<THandlers> Regex(
        Regex regex,
        int maxMatchLength,
        string? name = null);

    public TextRuleBuilder<THandlers> Tail(
        int maxMatchLength,
        Func<ReadOnlySpan<char>, int> matcher,
        string? name = null);

    public void Group(string group, Action<TextDsl<THandlers>> configure);
}

public sealed class TextRuleBuilder<THandlers>
{
    public TextRuleBuilder<THandlers> Priority(int value);

    public TextRuleBuilder<THandlers> Hook(
        Action<THandlers, int, ReadOnlySpan<char>> onMatch);

    public TextRuleBuilder<THandlers> HookAsync(
        Func<THandlers, int, ReadOnlyMemory<char>, ValueTask> onMatchAsync);

    public TextRuleBuilder<THandlers> Remove(
        Action<THandlers, int, ReadOnlySpan<char>>? onMatch = null);

    public TextRuleBuilder<THandlers> Remove(
        Func<THandlers, int, ReadOnlyMemory<char>, ValueTask> onMatchAsync);

    public TextRuleBuilder<THandlers> Replace(
        string replacement,
        Action<THandlers, int, ReadOnlySpan<char>>? onMatch = null);

    public TextRuleBuilder<THandlers> Replace(
        Func<THandlers, int, ReadOnlySpan<char>, string?> replacementFactory);

    public TextRuleBuilder<THandlers> ReplaceAsync(
        Func<THandlers, int, ReadOnlyMemory<char>, ValueTask<string?>> replacementFactoryAsync);

    public TextRuleBuilder<THandlers> Replace(
        Func<THandlers, int, ReadOnlySpan<char>, RewriteMetrics, string?> replacementFactoryWithContext);
}
```

Кратко:

- `Literal`  
  Совпадения фиксированной строки; работает через эффективный автомат.

- `Regex`  
  Совпадения по регулярному выражению. `maxMatchLength` ограничивает размер хвоста, который приходится держать в памяти. Строковая перегрузка по умолчанию использует `RegexOptions.CultureInvariant | RegexOptions.Compiled`; если нужен заранее сконфигурированный `Regex` (таймауты, флаги), используйте перегрузку с экземпляром — он не пересоздаётся.

- `Tail`  
  Правило, которое смотрит только на хвост текущего буфера. Функция возвращает длину совпадения, если оно найдено, или 0.

Каждое правило после создания настраивается через `TextRuleBuilder`:

- `Priority` управляет конфликтами при перекрывающихся совпадениях.
- `Hook` только «наблюдает» совпадения, не изменяя текст.
- `Remove` вырезает совпавший фрагмент.
- `Replace` заменяет фрагмент на константу или результат функции.


## Пример: состояния протокола и RuleGate

Иногда правила должны срабатывать только в определённых фазах протокола. Например, нужно удалять токены только внутри блока `BEGIN/END`.

```csharp
public sealed class ProtocolHandlers
{
    public bool InPayload;
    public int TokensRemoved;
}

public static class ProtocolExample
{
    public static async Task RunAsync(TextWriter output)
    {
        var kernel = TextKernel<ProtocolHandlers>.Compile(rules =>
        {
            rules.Regex(@"^BEGIN?
", 16, "begin")
                .Hook((h, id, match) => h.InPayload = true);

            rules.Regex(@"^END?
", 16, "end")
                .Hook((h, id, match) => h.InPayload = false);

            rules.Regex(@"token=[^&\s]+", 128, "strip-token")
                .Remove((h, id, match) => h.TokensRemoved++);
        });

        var handlers = new ProtocolHandlers();

        await using var session = kernel.CreateSession(output, handlers, new TextRuntimeOptions
        {
            RightWriteBlockSize = 0,
            RuleGate = (rule, metrics) =>
            {
                if (rule.Name == "strip-token")
                    return handlers.InPayload;
                return true;
            }
        });

        await session.WriteAsync("DEBUG token=1
");
        await session.WriteAsync("BEGIN
");
        await session.WriteAsync("token=secret
");
        await session.WriteAsync("END
");
        await session.FlushAsync();
    }
}
```

`RuleGate` позволяет включать и выключать правила динамически, на основе состояния `THandlers` и метрик.


## Атрибуты: декларативные правила для текста

Если удобнее описывать правила рядом с кодом, можно использовать атрибуты.

Пример: маскирование пароля и удаление e‑mail.

```csharp
public sealed class ChatHandlers
{
    public int Hits;
}

public static class ChatRules
{
    [TextLiteralRule("пароль:", Name = "mask-password", Action = MatchAction.Replace, Replacement = "пароль: [скрыто]", Priority = 0)]
    public static void OnPassword(ChatHandlers h, int ruleId, ReadOnlySpan<char> match)
    {
        h.Hits++;
    }

    [TextRegexRule(@"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+", 128, Name = "email", Action = MatchAction.Remove, Priority = 10)]
    public static void OnEmail(ChatHandlers h, int ruleId, ReadOnlySpan<char> match)
    {
        h.Hits++;
    }
}

public static class AttributeExample
{
    public static async Task RunAsync(TextWriter output)
    {
        var kernel = TextKernel<ChatHandlers>.CompileFromAttributes(typeof(ChatRules));
        var handlers = new ChatHandlers();

        await using var session = kernel.CreateSession(output, handlers, new TextRuntimeOptions
        {
            RightWriteBlockSize = 0
        });

        await session.WriteAsync("пароль: 1234, почта: user@example.com");
        await session.FlushAsync();
    }
}
```

Компилятор атрибутов превращает эти декларации в обычный план, как если бы вы написали DSL руками. Правила остаются стриминговыми.


## Опции выполнения: TextRuntimeOptions

```csharp
public sealed class TextRuntimeOptions
{
    public int RightWriteBlockSize { get; init; } = 0;
    public FlushBehavior FlushBehavior { get; init; } = FlushBehavior.PreserveMatchTail;

    public Func<char, char>? InputNormalizer { get; init; }

    public Func<ReadOnlySpan<char>, FilterMetrics, string?>? OutputFilter { get; init; }
    public Func<ReadOnlyMemory<char>, FilterMetrics, ValueTask<string?>>? OutputFilterAsync { get; init; }

    public Func<RuleInfo, FilterMetrics, bool>? RuleGate { get; init; }
    public Func<RuleInfo, FilterMetrics, ValueTask<bool>>? RuleGateAsync { get; init; }

    public Action<MatchContext>? AfterApply { get; init; }
    public Func<MatchContext, ValueTask>? AfterApplyAsync { get; init; }

    public Action<FilterMetrics>? OnMetrics { get; init; }
    public Func<FilterMetrics, ValueTask>? OnMetricsAsync { get; init; }

    public SseOptions? Sse { get; init; }
}
```

Основное:

- `RightWriteBlockSize = 0`  
  Писать безопасный префикс сразу, как только он появляется. Если указать положительное значение, библиотека будет группировать записи.

- `FlushBehavior`  
  Как вести себя при `Flush`:
  - `PreserveMatchTail` оставляет хвост, который может ещё попасть под правила.
  - другие режимы могут дожимать весь буфер, если вы уверены, что поток закончился.

- `InputNormalizer`  
  Можно нормализовать вход, например, приводя всё к одному регистру.

- `OutputFilter`  
  Постобработка уже безопасного текста: троттлинг, дополнительное форматирование, логирование.

- `RuleGate`  
  Включение/выключение правил в рантайме.

- `OnMetrics`  
  Получение агрегированных метрик после работы.


## SSE и фрейминг текстовых потоков

Для потоков Server‑Sent Events (SSE) и похожих протоколов есть базовая поддержка фрейминга:

```csharp
public sealed class SseOptions
{
    public string Delimiter { get; init; } = "

";
    public bool IncludeDelimiter { get; init; }
    public int? MaxEventSize { get; init; }
}
```

SSE‑фреймер позволяет:

- разделять поток на события,
- ограничивать размер одного события,
- не держать в памяти весь стрим.


## Низкоуровневый API: TextRewritePlan и TextRewriteWriter

Если нужен полный контроль, можно работать напрямую с планом:

```csharp
public static class LowLevelExample
{
    public static void Run(TextWriter output)
    {
        var planBuilder = new TextRewritePlanBuilder();

        planBuilder.ReplaceLiteral("пароль:", "пароль: [скрыто]");
        planBuilder.RemoveRegex(@"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+", 128);

        var plan = planBuilder.Build();

        using var writer = new TextRewriteWriter(output, plan, new TextRewriteOptions
        {
            RightWriteBlockSize = 0,
            FlushBehavior = FlushBehavior.PreserveMatchTail
        });

        writer.Write("пароль: 1234, почта: user@example.com");
        writer.Flush();
    }
}
```

Этот уровень удобен, когда:

- уже есть свой фреймер/обвязка,
- нужно встроить движок в существующую систему, где `THandlers` не очень уместен,
- хочется полностью контролировать план и не использовать атрибуты/DSL.


## Когда использовать какой подход

- Если нужно быстро навесить несколько правил над потоками:  
  Используйте `TextKernel<THandlers>` и DSL.

- Если правила логически принадлежат модулю/сервису и хочется держать их рядом с кодом:  
  Используйте атрибуты.

- Если вы пишете свою инфраструктуру поверх движка:  
  Используйте `TextRewritePlanBuilder` и `TextRewriteWriter`.

Во всех вариантах базовая гарантия одинакова:  
**Любая часть текста записывается в выход сразу, как только становится понятно, что никакое правило её больше не изменит.**  
От сложности правил зависит только CPU и размер хвоста, но не объём всего входного потока.
