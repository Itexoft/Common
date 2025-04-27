// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Buffers;
using System.Text.RegularExpressions;
using Itexoft.Text.Rewriting.Primitives;
using Itexoft.Text.Rewriting.Text;

namespace Itexoft.Tests.Text.Rewriting;

public sealed class TextRewriteWriterTests
{
    [Test]
    public void PassThroughWhenNoRules()
    {
        var plan = new TextRewritePlanBuilder().Build();

        using var sink = new StringWriter();
        using var writer = new TextRewriteWriter(sink, plan);

        writer.Write("alpha");
        writer.Write(' ');
        writer.Write("beta");
        writer.Flush();

        Assert.That(sink.ToString(), Is.EqualTo("alpha beta"));
    }

    [Test]
    public void SseOutputFilterFramesEvents()
    {
        var events = new List<string>();
        var plan = new TextRewritePlanBuilder().Build();
        using var sink = new StringWriter();

        using var writer = new TextRewriteWriter(
            sink,
            plan,
            new()
            {
                FlushBehavior = FlushBehavior.Commit,
                SseDelimiter = "\n\n",
                OutputFilter = (span, _) =>
                {
                    events.Add(span.ToString());

                    return span.ToString();
                },
            });

        writer.Write("event1\n\nevent2\n\nlast");
        writer.Flush();

        Assert.That(events, Is.EqualTo(new[] { "event1", "event2", "last" }));
        Assert.That(sink.ToString(), Is.EqualTo("event1\n\nevent2\n\nlast"));
    }

    [Test]
    public void SseDelimiterCanBeIncludedInCallback()
    {
        string? seen = null;
        var plan = new TextRewritePlanBuilder().Build();
        using var sink = new StringWriter();

        using var writer = new TextRewriteWriter(
            sink,
            plan,
            new()
            {
                FlushBehavior = FlushBehavior.Commit,
                SseDelimiter = "\n\n",
                SseIncludeDelimiter = true,
                OutputFilter = (span, _) =>
                {
                    seen = span.ToString();

                    return span.ToString();
                },
            });

        writer.Write("ping\n\n");
        writer.Flush();

        Assert.That(seen, Is.EqualTo("ping\n\n"));
        Assert.That(sink.ToString(), Is.EqualTo("ping\n\n"));
    }

    [Test]
    public void SseMaxEventSizeThrowsWhenExceeded()
    {
        var plan = new TextRewritePlanBuilder().Build();
        using var sink = new StringWriter();

        Assert.Throws<FormatException>(() =>
        {
            using var writer = new TextRewriteWriter(
                sink,
                plan,
                new()
                {
                    FlushBehavior = FlushBehavior.Commit,
                    SseDelimiter = "\n",
                    SseMaxEventSize = 2,
                });

            writer.Write("abcd\n");
            writer.Flush();
        });
    }

    [Test]
    public void LiteralRemovalTriggersMatchHandler()
    {
        var matches = new List<(int ruleId, string match)>();
        var plan = new TextRewritePlanBuilder().RemoveLiteral("secret", onMatch: (id, span) => matches.Add((id, span.ToString()))).Build();

        using var sink = new StringWriter();
        using var writer = new TextRewriteWriter(sink, plan, new() { FlushBehavior = FlushBehavior.Commit });

        writer.Write("keep secret hidden");
        writer.Flush();

        Assert.That(sink.ToString(), Is.EqualTo("keep  hidden"));
        Assert.That(matches, Is.EqualTo(new List<(int, string)> { (0, "secret") }));
    }

    [Test]
    public void LiteralReplacementFactoryReceivesMatchSpan()
    {
        var seen = new List<(int ruleId, string match)>();

        var plan = new TextRewritePlanBuilder().ReplaceLiteral(
            "abc",
            (id, span) =>
            {
                seen.Add((id, span.ToString()));

                return span.ToString().ToUpperInvariant();
            }).Build();

        using var sink = new StringWriter();
        using var writer = new TextRewriteWriter(sink, plan, new() { FlushBehavior = FlushBehavior.Commit });

        writer.Write("abc-abc");
        writer.Flush();

        Assert.That(sink.ToString(), Is.EqualTo("ABC-ABC"));
        Assert.That(seen, Is.EqualTo(new List<(int, string)> { (0, "abc"), (0, "abc") }));
    }

    [Test]
    public void CaseInsensitiveLiteralUsesOrdinalFolding()
    {
        var plan = new TextRewritePlanBuilder().RemoveLiteral("PING", comparison: StringComparison.OrdinalIgnoreCase).Build();

        using var sink = new StringWriter();
        using var writer = new TextRewriteWriter(sink, plan, new() { FlushBehavior = FlushBehavior.Commit });

        writer.Write("piNg pong");
        writer.Flush();

        Assert.That(sink.ToString(), Is.EqualTo(" pong"));
    }

    [Test]
    public void RegexMatchMustEndAtTail()
    {
        var plan = new TextRewritePlanBuilder().RemoveRegex(new Regex("foo"), 8).Build();

        using var sink = new StringWriter();
        using var writer = new TextRewriteWriter(sink, plan, new() { FlushBehavior = FlushBehavior.Commit });

        writer.Write("fooXfoo");
        writer.Flush();

        Assert.That(sink.ToString(), Is.EqualTo("X"));
    }

    [Test]
    public void RegexMaxMatchLengthBoundsTail()
    {
        var plan = new TextRewritePlanBuilder().RemoveRegex(new Regex("abcd"), 3).Build();

        using var sink = new StringWriter();
        using var writer = new TextRewriteWriter(sink, plan, new() { FlushBehavior = FlushBehavior.Commit });

        writer.Write("abcd");
        writer.Flush();

        Assert.That(sink.ToString(), Is.EqualTo("abcd"));
    }

    [Test]
    public void TailMatcherReplacementRunsWhenTailMatches()
    {
        var matches = new List<(int ruleId, string match)>();

        var plan = new TextRewritePlanBuilder().ReplaceTailMatcher(
            4,
            tail => tail.EndsWith("end", StringComparison.Ordinal) ? 3 : 0,
            "***",
            onMatch: (id, span) => matches.Add((id, span.ToString()))).Build();

        using var sink = new StringWriter();
        using var writer = new TextRewriteWriter(sink, plan, new() { FlushBehavior = FlushBehavior.Commit });

        writer.Write("the end");
        writer.Flush();

        Assert.That(sink.ToString(), Is.EqualTo("the ***"));
        Assert.That(matches, Is.EqualTo(new List<(int, string)> { (0, "end") }));
    }

    [Test]
    public void TailMatcherReturnGreaterThanTailIgnored()
    {
        var plan = new TextRewritePlanBuilder().RemoveTailMatcher(4, tail => tail.Length + 1).Build();

        using var sink = new StringWriter();
        using var writer = new TextRewriteWriter(sink, plan, new() { FlushBehavior = FlushBehavior.Commit });

        writer.Write("data");
        writer.Flush();

        Assert.That(sink.ToString(), Is.EqualTo("data"));
    }

    [Test]
    public void LongestThenPriorityPrefersLongerMatch()
    {
        var plan = new TextRewritePlanBuilder().RemoveLiteral("bab", 1).ReplaceLiteral("ab", "X", 0)
            .Build(new() { MatchSelection = MatchSelection.LongestThenPriority });

        using var sink = new StringWriter();
        using var writer = new TextRewriteWriter(sink, plan, new() { FlushBehavior = FlushBehavior.Commit });

        writer.Write("bab");
        writer.Flush();

        Assert.That(sink.ToString(), Is.EqualTo(string.Empty));
    }

    [Test]
    public void PriorityThenLongestPrefersPriorityEvenIfShorter()
    {
        var plan = new TextRewritePlanBuilder().RemoveLiteral("bab", 1).ReplaceLiteral("ab", "X", 0)
            .Build(new() { MatchSelection = MatchSelection.PriorityThenLongest });

        using var sink = new StringWriter();
        using var writer = new TextRewriteWriter(sink, plan, new() { FlushBehavior = FlushBehavior.Commit });

        writer.Write("bab");
        writer.Flush();

        Assert.That(sink.ToString(), Is.EqualTo("bX"));
    }

    [Test]
    public void RuleOrderBreaksPriorityTiesAcrossRuleTypes()
    {
        var plan = new TextRewritePlanBuilder().ReplaceLiteral("foo", "L", 0).ReplaceRegex(new Regex("foo"), 3, "R", 0).Build();

        using var sink = new StringWriter();
        using var writer = new TextRewriteWriter(sink, plan, new() { FlushBehavior = FlushBehavior.Commit });

        writer.Write("foo");
        writer.Flush();

        Assert.That(sink.ToString(), Is.EqualTo("L"));
    }

    [Test]
    public void FlushPreserveTailKeepsPendingForFutureMatch()
    {
        var plan = new TextRewritePlanBuilder().RemoveLiteral("abc").Build();

        using var sink = new StringWriter();
        using var writer = new TextRewriteWriter(sink, plan);

        writer.Write("ab");
        writer.Flush();

        writer.Write("c");
        writer.Flush();

        Assert.That(sink.ToString(), Is.EqualTo(string.Empty));
    }

    [Test]
    public void FlushCommitWritesPendingAndResetsState()
    {
        var plan = new TextRewritePlanBuilder().RemoveLiteral("abc").Build();

        using var sink = new StringWriter();
        using var writer = new TextRewriteWriter(sink, plan, new() { FlushBehavior = FlushBehavior.Commit });

        writer.Write("ab");
        writer.Flush();

        writer.Write("c");
        writer.Flush();

        Assert.That(sink.ToString(), Is.EqualTo("abc"));
    }

    [Test]
    public async Task WriteAsyncThrowsWhenCanceledBeforeProcessing()
    {
        var plan = new TextRewritePlanBuilder().RemoveLiteral("x").Build();

        var sink = new StringWriter();
        await using var sink1 = sink.ConfigureAwait(false);
        var writer = new TextRewriteWriter(sink, plan, new() { FlushBehavior = FlushBehavior.Commit });
        await using var writer1 = writer.ConfigureAwait(false);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync().ConfigureAwait(false);

        Assert.ThrowsAsync<OperationCanceledException>(async () => await writer.WriteAsync("x".AsMemory(), cts.Token).ConfigureAwait(false));
        Assert.That(sink.ToString(), Is.Empty);
    }

    [TestCase(-1, null, null), TestCase(null, 0, null), TestCase(null, null, -1)]
    public void ConstructorValidatesOptionBounds(int? initialBufferSize, int? maxBufferedChars, int? rightWriteBlockSize)
    {
        var plan = new TextRewritePlanBuilder().HookLiteral("x", (_, _) => { }).Build();

        var options = new TextRewriteOptions
        {
            InitialBufferSize = initialBufferSize ?? 256,
            MaxBufferedChars = maxBufferedChars ?? 1_048_576,
            RightWriteBlockSize = rightWriteBlockSize ?? 4096,
        };

        using var sink = new StringWriter();
        Assert.Throws<ArgumentOutOfRangeException>(() => new TextRewriteWriter(sink, plan, options));
    }

    [Test]
    public void BuilderValidatesInputs()
    {
        var builder = new TextRewritePlanBuilder();

        Assert.Throws<ArgumentNullException>(() => builder.HookLiteral(null!, (_, _) => { }));
        Assert.Throws<ArgumentException>(() => builder.HookLiteral(string.Empty, (_, _) => { }));
        Assert.Throws<NotSupportedException>(() => builder.HookLiteral("a", (_, _) => { }, comparison: StringComparison.CurrentCulture));

        Assert.Throws<ArgumentNullException>(() => builder.HookRegex((Regex)null!, 1, (_, _) => { }));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.HookRegex(new Regex("a"), 0, (_, _) => { }));

        Assert.Throws<ArgumentNullException>(() => builder.HookTailMatcher(1, null!, (_, _) => { }));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.HookTailMatcher(0, _ => 0, (_, _) => { }));
    }

    [Test]
    public void MaxBufferedFlushesWhenThresholdReached()
    {
        var plan = new TextRewritePlanBuilder().HookLiteral("zz", (_, _) => { }).Build();

        var sink = new RecordingWriter();

        using var writer = new TextRewriteWriter(
            sink,
            plan,
            new()
            {
                FlushBehavior = FlushBehavior.Commit,
                RightWriteBlockSize = 10,
                MaxBufferedChars = 5,
            });

        writer.Write("abcdef");
        writer.Flush();

        Assert.That(sink.ToString(), Is.EqualTo("abcdef"));
        Assert.That(sink.Segments, Is.EqualTo(new[] { "abcd", "e", "f" }));
    }

    [Test]
    public void DisposeFlushesTrailingPendingContent()
    {
        var plan = new TextRewritePlanBuilder().RemoveLiteral("abc").Build();

        var sink = new StringWriter();
        var writer = new TextRewriteWriter(sink, plan);

        writer.Write("ab");
        writer.Dispose();

        Assert.That(sink.ToString(), Is.EqualTo("ab"));
    }

    [Test]
    public void ClearFlagFlowsToArrayPoolOnDispose()
    {
        var pool = new TrackingArrayPool();
        var plan = new TextRewritePlanBuilder().HookLiteral("x", (_, _) => { }).Build();

        var sink = new StringWriter();

        var writer = new TextRewriteWriter(
            sink,
            plan,
            new()
            {
                ArrayPool = pool,
                ClearPooledBuffersOnDispose = true,
                FlushBehavior = FlushBehavior.Commit,
            });

        writer.Write("x");
        writer.Dispose();

        Assert.That(pool.ReturnCalledWithClear, Is.True);
    }

    [Test]
    public void InputNormalizerDropsCharacters()
    {
        var plan = new TextRewritePlanBuilder().HookLiteral("zzz", (_, _) => { }).Build();

        using var sink = new StringWriter();

        using var writer = new TextRewriteWriter(
            sink,
            plan,
            new()
            {
                InputNormalizer = c => c == '\u200b' ? '\0' : c,
                FlushBehavior = FlushBehavior.Commit,
            });

        writer.Write("a\u200bb");
        writer.Flush();

        Assert.That(sink.ToString(), Is.EqualTo("ab"));
    }

    [Test]
    public void RuleGateDisablesRuleAfterFirstMatch()
    {
        var plan = new TextRewritePlanBuilder().RemoveLiteral("x").Build();

        using var sink = new StringWriter();

        using var writer = new TextRewriteWriter(
            sink,
            plan,
            new()
            {
                FlushBehavior = FlushBehavior.Commit,
                RuleGate = (id, metrics) => metrics.MatchesApplied == 0,
            });

        writer.Write("xx");
        writer.Flush();

        Assert.That(sink.ToString(), Is.EqualTo("x"));
    }

    [Test]
    public void BeforeApplyCanCancelMutation()
    {
        var plan = new TextRewritePlanBuilder().RemoveLiteral("x").Build();

        var after = false;

        using var sink = new StringWriter();

        using var writer = new TextRewriteWriter(
            sink,
            plan,
            new()
            {
                FlushBehavior = FlushBehavior.Commit,
                BeforeApply = _ => false,
                AfterApply = _ => after = true,
            });

        writer.Write("x");
        writer.Flush();

        Assert.That(sink.ToString(), Is.EqualTo("x"));
        Assert.That(after, Is.False);
    }

    [Test]
    public void ReplacementFactoryWithContextReceivesMetrics()
    {
        var plan = new TextRewritePlanBuilder().ReplaceLiteral("x", (id, span, metrics) => metrics.ProcessedChars.ToString()).Build();

        using var sink = new StringWriter();
        using var writer = new TextRewriteWriter(sink, plan, new() { FlushBehavior = FlushBehavior.Commit });

        writer.Write("xx");
        writer.Flush();

        Assert.That(sink.ToString(), Is.EqualTo("12"));
    }

    [Test]
    public void ReplacementFactoryWithContextWorksForRegex()
    {
        var plan = new TextRewritePlanBuilder().ReplaceRegex(new Regex("x."), 2, (id, span, metrics) => $"{metrics.ProcessedChars}:{span.ToString()}")
            .Build();

        using var sink = new StringWriter();
        using var writer = new TextRewriteWriter(sink, plan, new() { FlushBehavior = FlushBehavior.Commit });

        writer.Write("0x1x2");
        writer.Flush();

        Assert.That(sink.ToString(), Is.EqualTo("03:x15:x2"));
    }

    [Test]
    public void ReplacementFactoryWithContextWorksForTailMatcher()
    {
        var plan = new TextRewritePlanBuilder().ReplaceTailMatcher(
            2,
            tail => tail.EndsWith("ab", StringComparison.Ordinal) ? 2 : 0,
            (id, span, metrics) => $"{metrics.ProcessedChars}").Build();

        using var sink = new StringWriter();
        using var writer = new TextRewriteWriter(sink, plan, new() { FlushBehavior = FlushBehavior.Commit });

        writer.Write("ab ab");
        writer.Flush();

        Assert.That(sink.ToString(), Is.EqualTo("2 5"));
    }

    [Test]
    public void OutputFilterCanDropSafePrefix()
    {
        var plan = new TextRewritePlanBuilder().HookLiteral("zzz", (_, _) => { }).Build();

        using var sink = new StringWriter();

        using var writer = new TextRewriteWriter(
            sink,
            plan,
            new()
            {
                FlushBehavior = FlushBehavior.Commit,
                OutputFilter = (_, _) => string.Empty,
            });

        writer.Write("abc");
        writer.Flush();

        Assert.That(sink.ToString(), Is.EqualTo(string.Empty));
    }

    [Test]
    public void RuleGateWithMatchSelectionRespectsDisabledRule()
    {
        var plan = new TextRewritePlanBuilder().RemoveLiteral("ab", 0).RemoveLiteral("abc", 1)
            .Build(new() { MatchSelection = MatchSelection.LongestThenPriority });

        using var sink = new StringWriter();

        using var writer = new TextRewriteWriter(
            sink,
            plan,
            new()
            {
                FlushBehavior = FlushBehavior.Commit,
                RuleGate = (id, _) => id != 1,
            });

        writer.Write("abc");
        writer.Flush();

        Assert.That(sink.ToString(), Is.EqualTo("c"));
    }

    [Test]
    public void OutputFilterTransformsSafePrefix()
    {
        var plan = new TextRewritePlanBuilder().HookLiteral("zzz", (_, _) => { }).Build();

        using var sink = new StringWriter();

        using var writer = new TextRewriteWriter(
            sink,
            plan,
            new()
            {
                FlushBehavior = FlushBehavior.Commit,
                OutputFilter = (span, _) => span.ToString().ToUpperInvariant(),
            });

        writer.Write("abc");
        writer.Flush();

        Assert.That(sink.ToString(), Is.EqualTo("ABC"));
    }

    [Test]
    public void RegexPlusAppliedOncePerSpan()
    {
        var plan = new TextRewritePlanBuilder().ReplaceRegex(new Regex("x+"), 4, "***")
            .Build(new() { MatchSelection = MatchSelection.LongestThenPriority });

        using var sink = new StringWriter();
        using var writer = new TextRewriteWriter(sink, plan, new() { FlushBehavior = FlushBehavior.Commit });

        writer.Write("aaxxbb");
        writer.Flush();

        Assert.That(sink.ToString(), Is.EqualTo("aa***bb"));
    }

    [Test]
    public void RegexPlusAppliedOnFlushCommit()
    {
        var plan = new TextRewritePlanBuilder().ReplaceRegex(new Regex("x+"), 4, "***")
            .Build(new() { MatchSelection = MatchSelection.LongestThenPriority });

        using var sink = new StringWriter();
        using var writer = new TextRewriteWriter(sink, plan, new() { FlushBehavior = FlushBehavior.Commit });

        writer.Write("xx");
        writer.Flush();

        Assert.That(sink.ToString(), Is.EqualTo("***"));
    }

    [Test]
    public void InputNormalizerDropsCharAndReportsMetrics()
    {
        var metrics = new List<RewriteMetrics>();
        var plan = new TextRewritePlanBuilder().Build();
        using var sink = new StringWriter();

        using var writer = new TextRewriteWriter(
            sink,
            plan,
            new()
            {
                InputNormalizer = c => c == 'x' ? '\0' : c,
                OnMetrics = m => metrics.Add(m),
                FlushBehavior = FlushBehavior.Commit,
                RightWriteBlockSize = 0,
            });

        writer.Write("axbx");
        writer.Flush();

        Assert.That(sink.ToString(), Is.EqualTo("ab"));
        Assert.That(metrics, Is.Not.Empty);
        Assert.That(metrics[^1].ProcessedChars, Is.EqualTo(4));
        Assert.That(metrics[^1].BufferedChars, Is.EqualTo(0));
    }

    private sealed class RecordingWriter : StringWriter
    {
        public List<string> Segments { get; } = [];

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

        public override void Return(char[] array, bool clearArray) => this.ReturnCalledWithClear = clearArray;
    }
}
