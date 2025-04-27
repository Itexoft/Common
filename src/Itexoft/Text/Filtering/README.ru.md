# Потоковая фильтрация текста

Исходники: `src/Itexoft/Text/Filtering/`. Правила собираются один раз, а затем применяются на лету при записи в
`TextWriter` — поддерживаются литералы, регулярные выражения и кастомные матчеры хвоста.

## Быстрый старт

```csharp
var builder = new TextFilterBuilder()
    .ReplaceLiteral("secret", "***", priority: 0)
    .RemoveRegex(new Regex(@"token=[^&\\s]+", RegexOptions.Compiled), maxMatchLength: 128)
    .HookTailMatcher(16, tail => tail.EndsWith("PING", StringComparison.Ordinal) ? 4 : 0,
        (id, match) => Console.WriteLine($"Ping at {DateTimeOffset.Now}"));

var plan = builder.Build();

using var writer = new FilteringTextWriter(Console.Out, plan, new FilteringTextProcessorOptions
{
    FlushBehavior = FlushBehavior.PreserveMatchTail,
    RightWriteBlockSize = 1024
});

await writer.WriteAsync("ping?token=abc");
await writer.FlushAsync(); // дописывает хвост, если нужно
```

## Типы правил

- **Литералы**: подстроки с Ordinal / OrdinalIgnoreCase, действия — убрать, заменить (строка или фабрика) или только
  hook.
- **Regex**: проверяет хвост буфера длиной до `maxMatchLength`; учитываются совпадения, заканчивающиеся в хвосте.
- **Tail matcher**: делегат, возвращающий длину совпадения на текущем хвосте.

`MatchSelection` управляет приоритетом пересечений: длиннее‑затем‑приоритет (по умолчанию) или приоритет‑затем‑длина.
Меньшее значение `priority` выигрывает.

## Настройки рантайма (writer/reader)

- `RightWriteBlockSize`: сбрасывать безопасный префикс, когда доступно не меньше указанного числа символов (`0` —
  сразу).
- `InitialBufferSize` / `MaxBufferedChars`: стартовый размер и верхний лимит буфера.
- `FlushBehavior`: `PreserveMatchTail` сохраняет хвост, который может сформировать будущий матч; `Commit` пишет все.
- `ArrayPool` и `ClearPooledBuffersOnDispose`: управление пулами для снижения аллокаций.
- `InputNormalizer`: опциональный нормализатор входных символов (например, складка юникода, вырезание zero-width) до
  матчинга.
- `OutputFilter`: трансформация безопасного префикса перед записью в sink; может переписать или отбросить текст.
- `RuleGate`: предикат, который включает/выключает правила на лету в зависимости от `FilteringMetrics`.
- `BeforeApply` / `AfterApply`: хуки вокруг применения матча; `BeforeApply` может отменить мутацию.
- `OnMetrics`: коллбек на обновление метрик (`ProcessedChars`, `MatchesApplied`, `Replacements`, `Removals`, `Flushes`,
  `BufferedChars`).
- Контекстные замены: `ReplacementFactoryWithContext` даёт доступ к метрикам при построении replacement.
- Reader/writer используют общее ядро и один набор опций; план собирается один раз и переиспользуется в обе стороны.
