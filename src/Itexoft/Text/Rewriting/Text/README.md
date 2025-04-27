# Streaming text rewriter

This module handles live rewriting of text streams: input arrives in chunks, rules fire on the fly, and output is written as soon as it is clear that a piece of text will not change anymore.

Typical scenarios:
- cleaning logs and traces from secrets and personal data
- filtering user input in real time (chat, console, stream)
- masking tokens, passwords, identifiers
- lightweight transformations of text protocols without full buffering

The library does not parse a language fully. It efficiently finds and rewrites text fragments by rules without keeping the whole stream in memory.


## Key idea: safe prefix and uncertainty tail

The stream is split at every step into two parts:

- safe prefix  
  Part of the text that is **definitely** not going to change by any rule. It can be written immediately to the output `TextWriter`.

- uncertainty tail  
  The last characters that may still be the end of a match or its continuation. They need to be kept in memory temporarily.

For each rule the maximum look distance is known in advance (literal, regex, custom matching). From these limits the engine computes the maximum tail size. Everything to the left becomes a “safe prefix” and is flushed immediately.

Result:  
Delay and memory depend not on total input length, but on the **context depth** required by rules.


## Entry points: what you likely need

Two API layers are used in practice:

1. High level:
   - `TextKernel<THandlers>`
   - `TextSession<THandlers>`
   - `TextDsl<THandlers>`
   - rule attributes (declarative approach)

2. Low level:
   - `TextRewritePlanBuilder`
   - `TextRewritePlan`
   - `TextRewriteWriter` / `TextRewriteReader`
   - `TextRewriteOptions`

For most scenarios **kernel + DSL / attributes** is enough. Low level is for custom wrappers or full control of the plan.


## Quick start: chat filter via DSL

Example: a stream of messages where you need to mask the word “пароль” and cut e‑mail addresses from logs.

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
        await session.WriteAsync("\nВот ещё одно письмо: admin@company.local");
        await session.FlushAsync();
    }
}
```

What happens here:
- `Compile` builds an immutable plan from rule descriptions.
- `CreateSession` creates a processing session for a specific `TextWriter` and `ChatHandlers`.
- `WriteAsync` can be called any number of times, with any chunking.
- As soon as part of the text becomes a safe prefix, it goes to `output` immediately.


## Execution model: kernel and session

Key types:

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

- `TextKernel<THandlers>` is compiled once and can be used in many threads.
- `TextSession<THandlers>` is created for a single output stream.
- `THandlers` holds state and processing logic (counters, state machines, protocol caches).


## DSL for text: TextDsl and TextRuleBuilder

DSL builds rules on top of the existing engine. Main methods:

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

Briefly:

- `Literal`  
  Matches a fixed string via an efficient automaton.

- `Regex`  
  Matches by regular expression. `maxMatchLength` limits the tail size that must be buffered. The string overload defaults to `RegexOptions.CultureInvariant | RegexOptions.Compiled`; if you need a preconfigured `Regex` (timeouts, flags), use the overload with an instance—it is not recreated.

- `Tail`  
  A rule that looks only at the current buffer tail. The function returns the match length if found, or 0.

Each rule is configured through `TextRuleBuilder`:

- `Priority` controls conflicts for overlapping matches.
- `Hook` only “observes” matches without changing text.
- `Remove` cuts out the matched fragment (sync/async).
- `Replace` swaps the fragment for a constant or factory result (sync/async, with metrics-aware factory).


## Example: protocol states and RuleGate

Sometimes rules should fire only in certain protocol phases. For example, remove tokens only inside a `BEGIN/END` block.

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
            rules.Regex(@"^BEGIN\r?\n", 16, "begin")
                .Hook((h, id, match) => h.InPayload = true);

            rules.Regex(@"^END\r?\n", 16, "end")
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

        await session.WriteAsync("DEBUG token=1\n");
        await session.WriteAsync("BEGIN\n");
        await session.WriteAsync("token=secret\n");
        await session.WriteAsync("END\n");
        await session.FlushAsync();
    }
}
```

`RuleGate` lets you toggle rules dynamically based on `THandlers` state and metrics.


## Attributes: declarative rules for text

If you prefer to keep rules next to code, use attributes.

Example: mask password and remove e‑mail.

```csharp
public sealed class ChatHandlers
{
    public int Hits;
}

public static class ChatRules
{
    [TextLiteralRule("password:", Name = "mask-password", Action = MatchAction.Replace, Replacement = "password: [hidden]", Priority = 0)]
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

        await session.WriteAsync("password: 1234, mail: user@example.com");
        await session.FlushAsync();
    }
}
```

The attribute compiler turns these declarations into the same plan as if you had written DSL by hand. Rules stay streaming.


## Runtime options: TextRuntimeOptions

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

Main points:

- `RightWriteBlockSize = 0`  
  Write the safe prefix immediately when it appears. If you set a positive value, the library batches writes.

- `FlushBehavior`  
  How to act on `Flush`:
  - `PreserveMatchTail` keeps the tail that might still match rules.
  - other modes may force-flush if you know the stream is over.

- `InputNormalizer`  
  Normalize input, e.g., unify casing.

- `OutputFilter`  
  Post-process already safe text: throttling, extra formatting, logging.

- `RuleGate`  
  Enable/disable rules at runtime.

- `OnMetrics`  
  Receive aggregated metrics after work.


## SSE and framing of text streams

For Server-Sent Events (SSE) and similar protocols there is basic framing support:

```csharp
public sealed class SseOptions
{
    public string Delimiter { get; init; } = "\n\n";
    public bool IncludeDelimiter { get; init; }
    public int? MaxEventSize { get; init; }
}
```

The SSE framer allows you to:

- split the stream into events,
- limit the size of one event,
- avoid keeping the whole stream in memory.


## Low-level API: TextRewritePlan and TextRewriteWriter

If you need full control, you can work directly with the plan:

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

This level is convenient when:

- you already have your own framer/wrapper,
- you need to embed the engine in a system where `THandlers` is not very appropriate,
- you want full control of the plan without attributes/DSL.


## When to use which approach

- If you need to quickly attach a few rules to streams:  
  Use `TextKernel<THandlers>` and DSL.

- If rules logically belong to a module/service and should live near the code:  
  Use attributes.

- If you are building your own infrastructure on top of the engine:  
  Use `TextRewritePlanBuilder` and `TextRewriteWriter`.

In all variants the base guarantee is the same:  
**Any piece of text is written to output as soon as it is clear that no rule will change it.**  
Rule complexity affects CPU and tail size, but not the size of the entire input stream.
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
