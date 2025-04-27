// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Text.RegularExpressions;
using Itexoft.Text.Rewriting.Primitives;
using Itexoft.Text.Rewriting.Text;

namespace Itexoft.Tests.Text.Rewriting;

public sealed class TextRewriteWriterAsyncTests
{
    [Test]
    public async Task PassThroughWhenNoRulesAsync()
    {
        var plan = new TextRewritePlanBuilder().Build();

        var sink = new StringWriter();
        await using var sink1 = sink.ConfigureAwait(false);
        var writer = new TextRewriteWriter(sink, plan);
        await using var writer1 = writer.ConfigureAwait(false);

        await writer.WriteAsync("alpha").ConfigureAwait(false);
        await writer.WriteAsync(' ').ConfigureAwait(false);
        await writer.WriteAsync("beta").ConfigureAwait(false);
        await writer.FlushAsync().ConfigureAwait(false);

        Assert.That(sink.ToString(), Is.EqualTo("alpha beta"));
    }

    [Test]
    public async Task SseOutputFilterAsyncIsFramed()
    {
        var events = new List<string>();
        var plan = new TextRewritePlanBuilder().Build();

        var sink = new StringWriter();
        await using var sink1 = sink.ConfigureAwait(false);

        var writer = new TextRewriteWriter(
            sink,
            plan,
            new()
            {
                FlushBehavior = FlushBehavior.Commit,
                SseDelimiter = "\n\n",
                OutputFilterAsync = async (mem, _) =>
                {
                    events.Add(mem.Span.ToString());
                    await Task.Yield();

                    return mem.ToString();
                },
            });

        await using var writer1 = writer.ConfigureAwait(false);

        await writer.WriteAsync("one\n\ntwo\n\n").ConfigureAwait(false);
        await writer.FlushAsync().ConfigureAwait(false);

        Assert.That(events, Is.EqualTo(new[] { "one", "two" }));
        Assert.That(sink.ToString(), Is.EqualTo("one\n\ntwo\n\n"));
    }

    [Test]
    public async Task LiteralRemovalTriggersAsyncHandler()
    {
        var matches = new List<(int ruleId, string match)>();

        var plan = new TextRewritePlanBuilder().RemoveLiteral(
            "secret",
            async (id, mem) =>
            {
                matches.Add((id, mem.ToString()));
                await Task.Yield();
            }).Build();

        var sink = new StringWriter();
        await using var sink1 = sink.ConfigureAwait(false);
        var writer = new TextRewriteWriter(sink, plan, new() { FlushBehavior = FlushBehavior.Commit });
        await using var writer1 = writer.ConfigureAwait(false);

        await writer.WriteAsync("keep secret hidden").ConfigureAwait(false);
        await writer.FlushAsync().ConfigureAwait(false);

        Assert.That(sink.ToString(), Is.EqualTo("keep  hidden"));
        Assert.That(matches, Is.EqualTo(new List<(int, string)> { (0, "secret") }));
    }

    [Test]
    public async Task LiteralReplacementAsyncFactoryReceivesMemory()
    {
        var seen = new List<(int ruleId, string match)>();

        var plan = new TextRewritePlanBuilder().ReplaceLiteral(
            "abc",
            async (id, mem) =>
            {
                seen.Add((id, mem.ToString()));
                await Task.Yield();

                return mem.ToString().ToUpperInvariant();
            }).Build();

        var sink = new StringWriter();
        await using var sink1 = sink.ConfigureAwait(false);
        var writer = new TextRewriteWriter(sink, plan, new() { FlushBehavior = FlushBehavior.Commit });
        await using var writer1 = writer.ConfigureAwait(false);

        await writer.WriteAsync("abc-abc").ConfigureAwait(false);
        await writer.FlushAsync().ConfigureAwait(false);

        Assert.That(sink.ToString(), Is.EqualTo("ABC-ABC"));
        Assert.That(seen, Is.EqualTo(new List<(int, string)> { (0, "abc"), (0, "abc") }));
    }

    [Test]
    public async Task AsyncOutputFilterTransformsSafePrefix()
    {
        var plan = new TextRewritePlanBuilder().Build();

        var sink = new StringWriter();
        await using var sink1 = sink.ConfigureAwait(false);

        var writer = new TextRewriteWriter(
            sink,
            plan,
            new()
            {
                RightWriteBlockSize = 0,
                FlushBehavior = FlushBehavior.Commit,
                OutputFilterAsync = async (buffer, metrics) =>
                {
                    await Task.Yield();

                    return buffer.Span.ToString().ToUpperInvariant();
                },
            });

        await using var writer1 = writer.ConfigureAwait(false);

        await writer.WriteAsync("mixed").ConfigureAwait(false);
        await writer.FlushAsync().ConfigureAwait(false);

        Assert.That(sink.ToString(), Is.EqualTo("MIXED"));
    }

    [Test]
    public async Task RegexPlusAppliedOncePerSpanAsync()
    {
        var plan = new TextRewritePlanBuilder().ReplaceRegex(new Regex("x+"), 4, "***")
            .Build(new() { MatchSelection = MatchSelection.LongestThenPriority });

        var sink = new StringWriter();
        await using var sink1 = sink.ConfigureAwait(false);
        var writer = new TextRewriteWriter(sink, plan, new() { FlushBehavior = FlushBehavior.Commit });
        await using var writer1 = writer.ConfigureAwait(false);

        await writer.WriteAsync("aaxxbb").ConfigureAwait(false);
        await writer.FlushAsync().ConfigureAwait(false);

        Assert.That(sink.ToString(), Is.EqualTo("aa***bb"));
    }

    [Test]
    public async Task RegexPlusAppliedOnFlushCommitAsync()
    {
        var plan = new TextRewritePlanBuilder().ReplaceRegex(new Regex("x+"), 4, "***")
            .Build(new() { MatchSelection = MatchSelection.LongestThenPriority });

        var sink = new StringWriter();
        await using var sink1 = sink.ConfigureAwait(false);
        var writer = new TextRewriteWriter(sink, plan, new() { FlushBehavior = FlushBehavior.Commit });
        await using var writer1 = writer.ConfigureAwait(false);

        await writer.WriteAsync("xx").ConfigureAwait(false);
        await writer.FlushAsync().ConfigureAwait(false);

        Assert.That(sink.ToString(), Is.EqualTo("***"));
    }

    [Test]
    public async Task AsyncRuleGateBlocksThenAllows()
    {
        var gateHits = 0;
        var plan = new TextRewritePlanBuilder().RemoveLiteral("abc").Build();

        var sink = new StringWriter();
        await using var sink1 = sink.ConfigureAwait(false);

        var writer = new TextRewriteWriter(
            sink,
            plan,
            new()
            {
                FlushBehavior = FlushBehavior.Commit,
                RuleGateAsync = async (ruleId, metrics) =>
                {
                    await Task.Yield();
                    gateHits++;

                    return gateHits > 1;
                },
            });

        await using var writer1 = writer.ConfigureAwait(false);

        await writer.WriteAsync("abcabc").ConfigureAwait(false);
        await writer.FlushAsync().ConfigureAwait(false);

        Assert.That(sink.ToString(), Is.EqualTo("abc"));
        Assert.That(gateHits, Is.EqualTo(2));
    }

    [Test]
    public async Task AsyncBeforeAfterApplyRunAroundMutation()
    {
        var sequence = new List<string>();
        var plan = new TextRewritePlanBuilder().ReplaceRegex(new Regex("x+"), 8, "***").Build();

        var sink = new StringWriter();
        await using var sink1 = sink.ConfigureAwait(false);

        var writer = new TextRewriteWriter(
            sink,
            plan,
            new()
            {
                FlushBehavior = FlushBehavior.Commit,
                BeforeApplyAsync = async ctx =>
                {
                    sequence.Add($"before:{ctx.RuleId}:{ctx.MatchLength}");
                    await Task.Yield();

                    return true;
                },
                AfterApplyAsync = async ctx =>
                {
                    sequence.Add($"after:{ctx.RuleId}:{ctx.MatchLength}");
                    await Task.Yield();
                },
            });

        await using var writer1 = writer.ConfigureAwait(false);

        await writer.WriteAsync("aaxxbb").ConfigureAwait(false);
        await writer.FlushAsync().ConfigureAwait(false);

        Assert.That(sink.ToString(), Is.EqualTo("aa***bb"));
        Assert.That(sequence, Is.EqualTo(new[] { "before:0:2", "after:0:2" }));
    }
}
