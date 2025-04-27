# Streaming Text Filtering

Live sources: `src/Itexoft/Text/Filtering/`. The module lets you build a rule set once and apply it on the fly while
writing to a `TextWriter`, supporting literals, regexes, and custom tail matchers.

## Quick start

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
await writer.FlushAsync(); // ensures pending tail is committed if needed
```

## Rule types

- **Literal**: ordinal / ordinal-ignore-case substrings with remove, replace (static or factory) or hook-only behavior.
- **Regex**: applies to the buffered tail up to `maxMatchLength`; only matches ending at the tail are considered.
- **Tail matcher**: custom delegate returning match length for the current tail.

`MatchSelection` controls how overlaps resolve: longest-then-priority (default) or priority-then-longest. Lower
priority values win ties.

## Runtime options (writer/reader)

- `RightWriteBlockSize`: flush safe prefix when at least this many safe chars are available (`0` flushes immediately).
- `InitialBufferSize` / `MaxBufferedChars`: buffer sizing and safety cap.
- `FlushBehavior`: `PreserveMatchTail` keeps a suffix that might form a future match; `Commit` flushes everything.
- `ArrayPool` and `ClearPooledBuffersOnDispose`: pooling controls for low-alloc scenarios.
- `InputNormalizer`: optional per-char normalizer (e.g., unicode folding, dropping zero-width chars) before matching.
- `OutputFilter`: optional transform applied to safe spans before they hit the sink; can drop or rewrite text.
- `RuleGate`: predicate to toggle rules at runtime based on current `FilteringMetrics`.
- `BeforeApply` / `AfterApply`: hooks around match application; `BeforeApply` can cancel mutation.
- `OnMetrics`: callback fired when metrics update (`ProcessedChars`, `MatchesApplied`, `Replacements`, `Removals`,
  `Flushes`, `BufferedChars`).
- Context-aware replacements: use `ReplacementFactoryWithContext` to build replacements with access to metrics.
- Reader/writer share the same options and processing core; plans compile once and can be reused in both directions.
