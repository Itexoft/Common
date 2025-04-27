// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Text.Rewriting.Json.Dsl;
using Itexoft.Text.Rewriting.Primitives;

namespace Itexoft.Tests.Text.Rewriting.Json;

public sealed class JsonRewriteTests
{
    [Test]
    public void EnabledGroupsSkipDisabledRules()
    {
        var kernel = JsonKernel<object>.Compile(dsl =>
        {
            dsl.Group("alpha", g => g.ReplaceValue("/name", "alpha", "first"));
            dsl.Group("beta", g => g.ReplaceValue("/name", "beta", "second"));
        });

        var writer = new StringWriter();

        using var session = kernel.CreateSession(
            writer,
            new(),
            new()
            {
                EnabledGroups = ["beta"],
            });

        session.Write("""{"name":"input"}""");
        session.Commit();

        Assert.That(writer.ToString(), Is.EqualTo("""{"name":"beta"}"""));
    }

    [Test]
    public void ReplaceInStringPredicateReceivesPointer()
    {
        var pointers = new List<string>();

        var kernel = JsonKernel<object>.Compile(dsl =>
        {
            dsl.ReplaceInString(
                "/msg",
                (_, ctx) =>
                {
                    pointers.Add(ctx.Pointer);

                    return true;
                },
                (_, ctx) => ctx.Value + "!");
        });

        var writer = new StringWriter();
        using var session = kernel.CreateSession(writer, new());

        session.Write("""{"msg":"hi"}""");
        session.Commit();

        Assert.That(writer.ToString(), Is.EqualTo("""{"msg":"hi!"}"""));
        Assert.That(pointers, Is.EquivalentTo(new[] { "/msg" }));
    }

    [Test]
    public void DescribeExposesRuleMetadata()
    {
        var kernel = JsonKernel<object>.Compile(dsl => { dsl.Group("g", g => g.ReplaceValue("/a", "b", "rule")); });

        var descriptor = kernel.Describe().Single();

        Assert.That(descriptor.Dialect, Is.EqualTo("Json"));
        Assert.That(descriptor.Kind, Is.EqualTo("ReplaceValue"));
        Assert.That(descriptor.Target, Is.EqualTo("/a"));
        Assert.That(descriptor.Name, Is.EqualTo("rule"));
        Assert.That(descriptor.Group, Is.EqualTo("g"));
    }

    [Test]
    public void MaxFrameSizeDropsOversizedFrame()
    {
        var kernel = JsonKernel<object>.Compile(_ => { });
        var writer = new StringWriter();

        using var session = kernel.CreateSession(
            writer,
            new(),
            new()
            {
                Framing = new LineFraming(),
                MaxFrameSize = 5,
                FrameOverflowBehavior = OverflowBehavior.Drop,
            });

        session.Write("123456\n{}\n");
        session.Commit();

        Assert.That(writer.ToString(), Is.EqualTo("{}"));
    }

    [Test]
    public void MaxFrameSizeThrowsWhenConfigured()
    {
        var kernel = JsonKernel<object>.Compile(_ => { });

        using var session = kernel.CreateSession(
            new StringWriter(),
            new(),
            new()
            {
                Framing = new LineFraming(),
                MaxFrameSize = 3,
                FrameOverflowBehavior = OverflowBehavior.Error,
            });

        Assert.Throws<FormatException>(() => { session.Write("1234\n"); });
    }

    [Test]
    public async Task OversizedFrameIsDroppedThenNextFrameProcessed()
    {
        var kernel = JsonKernel<object>.Compile(_ => { });
        var writer = new StringWriter();

        var session = kernel.CreateSession(
            writer,
            new(),
            new()
            {
                Framing = new LineFraming(),
                MaxFrameSize = 4,
                FrameOverflowBehavior = OverflowBehavior.Drop,
            });

        await using var session1 = session.ConfigureAwait(false);

        await session.WriteAsync("12345\n{}\n".AsMemory()).ConfigureAwait(false);
        await session.CommitAsync().ConfigureAwait(false);

        Assert.That(writer.ToString(), Is.EqualTo("{}"));
    }

    [Test]
    public void EmitsRuleMetrics()
    {
        var stats = new List<RuleStat>();
        var kernel = JsonKernel<object>.Compile(dsl => dsl.ReplaceValue("/v", "out", "named"));
        var writer = new StringWriter();

        using var session = kernel.CreateSession(
            writer,
            new(),
            new()
            {
                OnRuleMetrics = s => stats.AddRange(s),
            });

        session.Write("""{"v":"in"}""");
        session.Commit();

        Assert.That(stats, Has.Count.EqualTo(1));
        var stat = stats[0];
        Assert.That(stat.Name, Is.EqualTo("named"));
        Assert.That(stat.Hits, Is.EqualTo(1));
    }

    [Test]
    public async Task ReplaceInStringAsyncIsAwaited()
    {
        var kernel = JsonKernel<object>.Compile(dsl => dsl.ReplaceInString(
            "/name",
            async (_, ctx) =>
            {
                await Task.Yield();

                return ctx.Value.ToUpperInvariant();
            }));

        var writer = new StringWriter();
        var session = kernel.CreateSession(writer, new());
        await using var session1 = session.ConfigureAwait(false);

        await session.WriteAsync("""{"name":"demo"}""".AsMemory()).ConfigureAwait(false);
        await session.CommitAsync().ConfigureAwait(false);

        Assert.That(writer.ToString(), Is.EqualTo("""{"name":"DEMO"}"""));
    }

    [Test]
    public void RequireAsyncBlocksWhenPredicateFails()
    {
        var kernel = JsonKernel<object>.Compile(dsl => dsl.RequireAsync(
            "/flag",
            async (_, value) =>
            {
                await Task.Yield();

                return value == "ok";
            }));

        using var session = kernel.CreateSession(new StringWriter(), new());

        Assert.Throws<FormatException>(() =>
        {
            session.Write("""{"flag":"no"}""");
            session.Commit();
        });
    }

    private sealed class LineFraming : IJsonFraming
    {
        public bool TryCutFrame(ref ReadOnlySpan<char> buffer, out ReadOnlySpan<char> frame)
        {
            var idx = buffer.IndexOf('\n');

            if (idx < 0)
            {
                frame = ReadOnlySpan<char>.Empty;

                return false;
            }

            frame = buffer[..idx];
            buffer = buffer[(idx + 1)..];

            return true;
        }

        public bool TryCutFrame(ref ReadOnlyMemory<char> buffer, out ReadOnlyMemory<char> frame)
        {
            var idx = buffer.Span.IndexOf('\n');

            if (idx < 0)
            {
                frame = ReadOnlyMemory<char>.Empty;

                return false;
            }

            frame = buffer[..idx];
            buffer = buffer[(idx + 1)..];

            return true;
        }
    }
}
