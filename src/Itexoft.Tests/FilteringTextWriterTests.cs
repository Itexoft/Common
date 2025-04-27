// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Buffers;
using System.Text.RegularExpressions;
using Itexoft.Text.Filtering;

namespace Itexoft.Tests;

public sealed class FilteringTextWriterTests
{
    [Test]
    public void PassThroughWhenNoRules()
    {
        var plan = new TextFilterBuilder().Build();

        using var sink = new StringWriter();
        using var writer = new FilteringTextWriter(sink, plan);

        writer.Write("alpha");
        writer.Write(' ');
        writer.Write("beta");
        writer.Flush();

        Assert.That(sink.ToString(), Is.EqualTo("alpha beta"));
    }

    [Test]
    public void LiteralRemovalTriggersMatchHandler()
    {
        var matches = new List<(int ruleId, string match)>();
        var plan = new TextFilterBuilder()
            .RemoveLiteral("secret", onMatch: (id, span) => matches.Add((id, span.ToString())))
            .Build();

        using var sink = new StringWriter();
        using var writer = new FilteringTextWriter(
            sink,
            plan,
            new() { FlushBehavior = FlushBehavior.Commit });

        writer.Write("keep secret hidden");
        writer.Flush();

        Assert.That(sink.ToString(), Is.EqualTo("keep  hidden"));
        Assert.That(matches, Is.EqualTo(new List<(int, string)> { (0, "secret") }));
    }

    [Test]
    public void LiteralReplacementFactoryReceivesMatchSpan()
    {
        var seen = new List<(int ruleId, string match)>();
        var plan = new TextFilterBuilder()
            .ReplaceLiteral(
                "abc",
                (id, span) =>
                {
                    seen.Add((id, span.ToString()));

                    return span.ToString().ToUpperInvariant();
                })
            .Build();

        using var sink = new StringWriter();
        using var writer = new FilteringTextWriter(
            sink,
            plan,
            new() { FlushBehavior = FlushBehavior.Commit });

        writer.Write("abc-abc");
        writer.Flush();

        Assert.That(sink.ToString(), Is.EqualTo("ABC-ABC"));
        Assert.That(seen, Is.EqualTo(new List<(int, string)> { (0, "abc"), (0, "abc") }));
    }

    [Test]
    public void CaseInsensitiveLiteralUsesOrdinalFolding()
    {
        var plan = new TextFilterBuilder()
            .RemoveLiteral("PING", comparison: StringComparison.OrdinalIgnoreCase)
            .Build();

        using var sink = new StringWriter();
        using var writer = new FilteringTextWriter(
            sink,
            plan,
            new() { FlushBehavior = FlushBehavior.Commit });

        writer.Write("piNg pong");
        writer.Flush();

        Assert.That(sink.ToString(), Is.EqualTo(" pong"));
    }

    [Test]
    public void RegexMatchMustEndAtTail()
    {
        var plan = new TextFilterBuilder()
            .RemoveRegex(new("foo"), 8)
            .Build();

        using var sink = new StringWriter();
        using var writer = new FilteringTextWriter(
            sink,
            plan,
            new() { FlushBehavior = FlushBehavior.Commit });

        writer.Write("fooXfoo");
        writer.Flush();

        Assert.That(sink.ToString(), Is.EqualTo("X"));
    }

    [Test]
    public void RegexMaxMatchLengthBoundsTail()
    {
        var plan = new TextFilterBuilder()
            .RemoveRegex(new("abcd"), 3)
            .Build();

        using var sink = new StringWriter();
        using var writer = new FilteringTextWriter(
            sink,
            plan,
            new() { FlushBehavior = FlushBehavior.Commit });

        writer.Write("abcd");
        writer.Flush();

        Assert.That(sink.ToString(), Is.EqualTo("abcd"));
    }

    [Test]
    public void TailMatcherReplacementRunsWhenTailMatches()
    {
        var matches = new List<(int ruleId, string match)>();
        var plan = new TextFilterBuilder()
            .ReplaceTailMatcher(
                4,
                tail => tail.EndsWith("end", StringComparison.Ordinal) ? 3 : 0,
                "***",
                onMatch: (id, span) => matches.Add((id, span.ToString())))
            .Build();

        using var sink = new StringWriter();
        using var writer = new FilteringTextWriter(
            sink,
            plan,
            new() { FlushBehavior = FlushBehavior.Commit });

        writer.Write("the end");
        writer.Flush();

        Assert.That(sink.ToString(), Is.EqualTo("the ***"));
        Assert.That(matches, Is.EqualTo(new List<(int, string)> { (0, "end") }));
    }

    [Test]
    public void TailMatcherReturnGreaterThanTailIgnored()
    {
        var plan = new TextFilterBuilder()
            .RemoveTailMatcher(4, tail => tail.Length + 1)
            .Build();

        using var sink = new StringWriter();
        using var writer = new FilteringTextWriter(
            sink,
            plan,
            new() { FlushBehavior = FlushBehavior.Commit });

        writer.Write("data");
        writer.Flush();

        Assert.That(sink.ToString(), Is.EqualTo("data"));
    }

    [Test]
    public void LongestThenPriorityPrefersLongerMatch()
    {
        var plan = new TextFilterBuilder()
            .RemoveLiteral("bab", 1)
            .ReplaceLiteral("ab", "X", 0)
            .Build(new() { MatchSelection = MatchSelection.LongestThenPriority });

        using var sink = new StringWriter();
        using var writer = new FilteringTextWriter(
            sink,
            plan,
            new() { FlushBehavior = FlushBehavior.Commit });

        writer.Write("bab");
        writer.Flush();

        Assert.That(sink.ToString(), Is.EqualTo(string.Empty));
    }

    [Test]
    public void PriorityThenLongestPrefersPriorityEvenIfShorter()
    {
        var plan = new TextFilterBuilder()
            .RemoveLiteral("bab", 1)
            .ReplaceLiteral("ab", "X", 0)
            .Build(new() { MatchSelection = MatchSelection.PriorityThenLongest });

        using var sink = new StringWriter();
        using var writer = new FilteringTextWriter(
            sink,
            plan,
            new() { FlushBehavior = FlushBehavior.Commit });

        writer.Write("bab");
        writer.Flush();

        Assert.That(sink.ToString(), Is.EqualTo("bX"));
    }

    [Test]
    public void RuleOrderBreaksPriorityTiesAcrossRuleTypes()
    {
        var plan = new TextFilterBuilder()
            .ReplaceLiteral("foo", "L", 0)
            .ReplaceRegex(new("foo"), 3, "R", 0)
            .Build();

        using var sink = new StringWriter();
        using var writer = new FilteringTextWriter(
            sink,
            plan,
            new() { FlushBehavior = FlushBehavior.Commit });

        writer.Write("foo");
        writer.Flush();

        Assert.That(sink.ToString(), Is.EqualTo("L"));
    }

    [Test]
    public void FlushPreserveTailKeepsPendingForFutureMatch()
    {
        var plan = new TextFilterBuilder()
            .RemoveLiteral("abc")
            .Build();

        using var sink = new StringWriter();
        using var writer = new FilteringTextWriter(sink, plan);

        writer.Write("ab");
        writer.Flush();

        writer.Write("c");
        writer.Flush();

        Assert.That(sink.ToString(), Is.EqualTo(string.Empty));
    }

    [Test]
    public void FlushCommitWritesPendingAndResetsState()
    {
        var plan = new TextFilterBuilder()
            .RemoveLiteral("abc")
            .Build();

        using var sink = new StringWriter();
        using var writer = new FilteringTextWriter(
            sink,
            plan,
            new() { FlushBehavior = FlushBehavior.Commit });

        writer.Write("ab");
        writer.Flush();

        writer.Write("c");
        writer.Flush();

        Assert.That(sink.ToString(), Is.EqualTo("abc"));
    }

    [Test]
    public async Task WriteAsyncThrowsWhenCanceledBeforeProcessing()
    {
        var plan = new TextFilterBuilder()
            .RemoveLiteral("x")
            .Build();

        using var sink = new StringWriter();
        await using var writer = new FilteringTextWriter(
            sink,
            plan,
            new() { FlushBehavior = FlushBehavior.Commit });

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAsync<OperationCanceledException>(async () => await writer.WriteAsync("x".AsMemory(), cts.Token));
        Assert.That(sink.ToString(), Is.Empty);
    }

    [TestCase(-1, null, null)]
    [TestCase(null, 0, null)]
    [TestCase(null, null, -1)]
    public void ConstructorValidatesOptionBounds(int? initialBufferSize, int? maxBufferedChars, int? rightWriteBlockSize)
    {
        var plan = new TextFilterBuilder()
            .HookLiteral("x", (_, _) => { })
            .Build();

        var options = new FilteringTextProcessorOptions
        {
            InitialBufferSize = initialBufferSize ?? 256,
            MaxBufferedChars = maxBufferedChars ?? 1_048_576,
            RightWriteBlockSize = rightWriteBlockSize ?? 4096
        };

        using var sink = new StringWriter();
        Assert.Throws<ArgumentOutOfRangeException>(() => new FilteringTextWriter(sink, plan, options));
    }

    [Test]
    public void BuilderValidatesInputs()
    {
        var builder = new TextFilterBuilder();

        Assert.Throws<ArgumentNullException>(() => builder.HookLiteral(null!, (_, _) => { }));
        Assert.Throws<ArgumentException>(() => builder.HookLiteral(string.Empty, (_, _) => { }));
        Assert.Throws<NotSupportedException>(() => builder.HookLiteral("a", (_, _) => { }, comparison: StringComparison.CurrentCulture));

        Assert.Throws<ArgumentNullException>(() => builder.HookRegex(null!, 1, (_, _) => { }));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.HookRegex(new Regex("a"), 0, (_, _) => { }));

        Assert.Throws<ArgumentNullException>(() => builder.HookTailMatcher(1, null!, (_, _) => { }));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.HookTailMatcher(0, _ => 0, (_, _) => { }));
    }

    [Test]
    public void MaxBufferedFlushesWhenThresholdReached()
    {
        var plan = new TextFilterBuilder()
            .HookLiteral("zz", (_, _) => { })
            .Build();

        var sink = new RecordingWriter();
        using var writer = new FilteringTextWriter(
            sink,
            plan,
            new()
            {
                FlushBehavior = FlushBehavior.Commit,
                RightWriteBlockSize = 10,
                MaxBufferedChars = 5
            });

        writer.Write("abcdef");
        writer.Flush();

        Assert.That(sink.ToString(), Is.EqualTo("abcdef"));
        Assert.That(sink.Segments, Is.EqualTo(new[] { "abcd", "e", "f" }));
    }

    [Test]
    public void DisposeFlushesTrailingPendingContent()
    {
        var plan = new TextFilterBuilder()
            .RemoveLiteral("abc")
            .Build();

        var sink = new StringWriter();
        var writer = new FilteringTextWriter(sink, plan);

        writer.Write("ab");
        writer.Dispose();

        Assert.That(sink.ToString(), Is.EqualTo("ab"));
    }

    [Test]
    public void ClearFlagFlowsToArrayPoolOnDispose()
    {
        var pool = new TrackingArrayPool();
        var plan = new TextFilterBuilder()
            .HookLiteral("x", (_, _) => { })
            .Build();

        var sink = new StringWriter();
        var writer = new FilteringTextWriter(
            sink,
            plan,
            new()
            {
                ArrayPool = pool,
                ClearPooledBuffersOnDispose = true,
                FlushBehavior = FlushBehavior.Commit
            });

        writer.Write("x");
        writer.Dispose();

        Assert.That(pool.ReturnCalledWithClear, Is.True);
    }

    [Test]
    public void InputNormalizerDropsCharacters()
    {
        var plan = new TextFilterBuilder()
            .HookLiteral("zzz", (_, _) => { })
            .Build();

        using var sink = new StringWriter();
        using var writer = new FilteringTextWriter(
            sink,
            plan,
            new()
            {
                InputNormalizer = c => c == '\u200b' ? '\0' : c,
                FlushBehavior = FlushBehavior.Commit
            });

        writer.Write("a\u200bb");
        writer.Flush();

        Assert.That(sink.ToString(), Is.EqualTo("ab"));
    }

    [Test]
    public void RuleGateDisablesRuleAfterFirstMatch()
    {
        var plan = new TextFilterBuilder()
            .RemoveLiteral("x")
            .Build();

        using var sink = new StringWriter();
        using var writer = new FilteringTextWriter(
            sink,
            plan,
            new()
            {
                FlushBehavior = FlushBehavior.Commit,
                RuleGate = (id, metrics) => metrics.MatchesApplied == 0
            });

        writer.Write("xx");
        writer.Flush();

        Assert.That(sink.ToString(), Is.EqualTo("x"));
    }

    [Test]
    public void BeforeApplyCanCancelMutation()
    {
        var plan = new TextFilterBuilder()
            .RemoveLiteral("x")
            .Build();

        var after = false;

        using var sink = new StringWriter();
        using var writer = new FilteringTextWriter(
            sink,
            plan,
            new()
            {
                FlushBehavior = FlushBehavior.Commit,
                BeforeApply = _ => false,
                AfterApply = _ => after = true
            });

        writer.Write("x");
        writer.Flush();

        Assert.That(sink.ToString(), Is.EqualTo("x"));
        Assert.That(after, Is.False);
    }

    [Test]
    public void ReplacementFactoryWithContextReceivesMetrics()
    {
        var plan = new TextFilterBuilder()
            .ReplaceLiteral("x", (id, span, metrics) => metrics.ProcessedChars.ToString())
            .Build();

        using var sink = new StringWriter();
        using var writer = new FilteringTextWriter(
            sink,
            plan,
            new() { FlushBehavior = FlushBehavior.Commit });

        writer.Write("xx");
        writer.Flush();

        Assert.That(sink.ToString(), Is.EqualTo("12"));
    }

    [Test]
    public void ReplacementFactoryWithContextWorksForRegex()
    {
        var plan = new TextFilterBuilder()
            .ReplaceRegex(new("x."), 2, (id, span, metrics) => $"{metrics.ProcessedChars}:{span.ToString()}")
            .Build();

        using var sink = new StringWriter();
        using var writer = new FilteringTextWriter(
            sink,
            plan,
            new() { FlushBehavior = FlushBehavior.Commit });

        writer.Write("0x1x2");
        writer.Flush();

        Assert.That(sink.ToString(), Is.EqualTo("03:x15:x2"));
    }

    [Test]
    public void ReplacementFactoryWithContextWorksForTailMatcher()
    {
        var plan = new TextFilterBuilder()
            .ReplaceTailMatcher(
                2,
                tail => tail.EndsWith("ab", StringComparison.Ordinal) ? 2 : 0,
                (id, span, metrics) => $"{metrics.ProcessedChars}")
            .Build();

        using var sink = new StringWriter();
        using var writer = new FilteringTextWriter(
            sink,
            plan,
            new() { FlushBehavior = FlushBehavior.Commit });

        writer.Write("ab ab");
        writer.Flush();

        Assert.That(sink.ToString(), Is.EqualTo("2 5"));
    }

    [Test]
    public void OutputFilterCanDropSafePrefix()
    {
        var plan = new TextFilterBuilder()
            .HookLiteral("zzz", (_, _) => { })
            .Build();

        using var sink = new StringWriter();
        using var writer = new FilteringTextWriter(
            sink,
            plan,
            new()
            {
                FlushBehavior = FlushBehavior.Commit,
                OutputFilter = (_, _) => string.Empty
            });

        writer.Write("abc");
        writer.Flush();

        Assert.That(sink.ToString(), Is.EqualTo(string.Empty));
    }

    [Test]
    public void RuleGateWithMatchSelectionRespectsDisabledRule()
    {
        var plan = new TextFilterBuilder()
            .RemoveLiteral("ab", 0)
            .RemoveLiteral("abc", 1)
            .Build(new() { MatchSelection = MatchSelection.LongestThenPriority });

        using var sink = new StringWriter();
        using var writer = new FilteringTextWriter(
            sink,
            plan,
            new()
            {
                FlushBehavior = FlushBehavior.Commit,
                RuleGate = (id, _) => id != 1
            });

        writer.Write("abc");
        writer.Flush();

        Assert.That(sink.ToString(), Is.EqualTo("c"));
    }

    [Test]
    public void OutputFilterTransformsSafePrefix()
    {
        var plan = new TextFilterBuilder()
            .HookLiteral("zzz", (_, _) => { })
            .Build();

        using var sink = new StringWriter();
        using var writer = new FilteringTextWriter(
            sink,
            plan,
            new()
            {
                FlushBehavior = FlushBehavior.Commit,
                OutputFilter = (span, _) => span.ToString().ToUpperInvariant()
            });

        writer.Write("abc");
        writer.Flush();

        Assert.That(sink.ToString(), Is.EqualTo("ABC"));
    }

    private sealed class RecordingWriter : StringWriter
    {
        public List<string> Segments { get; } = new();

        public override void Write(ReadOnlySpan<char> buffer)
        {
            this.Segments.Add(buffer.ToString());
            base.Write(buffer);
        }
    }

    private sealed class TrackingArrayPool : ArrayPool<char>
    {
        public bool ReturnCalledWithClear { get; private set; }

        public override char[] Rent(int minimumLength) => new char[Math.Max(1, minimumLength)];

        public override void Return(char[] array, bool clearArray)
        {
            this.ReturnCalledWithClear = clearArray;
        }
    }
}
