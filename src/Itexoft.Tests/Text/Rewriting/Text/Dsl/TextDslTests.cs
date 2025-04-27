// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Text.Rewriting.Primitives;
using Itexoft.Text.Rewriting.Text.Dsl;

namespace Itexoft.Tests.Text.Rewriting.Text.Dsl;

public sealed class TextDslTests
{
    [Test]
    public void KernelKeepsHandlersIsolatedAcrossSessions()
    {
        var kernel = TextKernel<TextHandler>.Compile(dsl =>
        {
            dsl.Literal("ping", name: "ping").Hook((h, _, _) => h.Pings++);
            dsl.Literal("token=abc", name: "token").Replace("***", (h, _, _) => h.Tokens.Add("abc"));
            dsl.Literal("token=def", name: "token2").Replace("***", (h, _, _) => h.Tokens.Add("def"));
        });

        var sink1 = new StringWriter();
        var sink2 = new StringWriter();
        var handlers1 = new TextHandler();
        var handlers2 = new TextHandler();

        using (var session = kernel.CreateSession(sink1, handlers1, new() { FlushBehavior = FlushBehavior.Commit }))
        {
            session.Write("ping token=abc");
            session.Flush();
        }

        using (var session = kernel.CreateSession(sink2, handlers2, new() { FlushBehavior = FlushBehavior.Commit }))
        {
            session.Write("ping token=def");
            session.Flush();
        }

        Assert.That(handlers1.Pings, Is.EqualTo(1));
        Assert.That(handlers2.Pings, Is.EqualTo(1));
        Assert.That(handlers1.Tokens, Is.EqualTo((string[])["abc"]));
        Assert.That(handlers2.Tokens, Is.EqualTo((string[])["def"]));
        Assert.That(sink1.ToString(), Is.EqualTo("ping ***"));
        Assert.That(sink2.ToString(), Is.EqualTo("ping ***"));
    }

    [Test]
    public void KernelBuildsRuleInfosForMetrics()
    {
        var kernel = TextKernel<TextHandler>.Compile(dsl => { dsl.Literal("foo", name: "rule-foo").Replace("X"); });

        using var sink = new StringWriter();
        using var session = kernel.CreateSession(sink, new(), new() { FlushBehavior = FlushBehavior.Commit });

        session.Write("foo");
        session.Flush();

        Assert.That(session.Metrics.MatchesApplied, Is.EqualTo(1));
        Assert.That(sink.ToString(), Is.EqualTo("X"));
    }

    private sealed class TextHandler
    {
        public int Pings { get; set; }

        public List<string> Tokens { get; } = [];
    }
}
