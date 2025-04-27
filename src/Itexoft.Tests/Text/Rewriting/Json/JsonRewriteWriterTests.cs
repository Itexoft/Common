// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Text.Json;
using Itexoft.Text.Rewriting.Json;

namespace Itexoft.Tests.Text.Rewriting.Json;

public sealed class JsonRewriteWriterTests
{
    [Test]
    public void PassThroughWhenNoRules()
    {
        var plan = new JsonRewritePlanBuilder().Build();

        using var sink = new StringWriter();
        using var writer = new JsonRewriteWriter(sink, plan);

        writer.Write("{\"a\":1,\"b\":2}");
        writer.Flush();

        Assert.That(sink.ToString(), Is.EqualTo("{\"a\":1,\"b\":2}"));
    }

    [Test]
    public void ReplaceValueRewritesScalar()
    {
        var plan = new JsonRewritePlanBuilder().ReplaceValue("/user/name", "***").Build();

        using var sink = new StringWriter();
        using var writer = new JsonRewriteWriter(sink, plan);

        writer.Write("{\"user\":{\"name\":\"John\",\"id\":1}}");
        writer.Flush();

        using var doc = JsonDocument.Parse(sink.ToString());
        Assert.That(doc.RootElement.GetProperty("user").GetProperty("name").GetString(), Is.EqualTo("***"));
        Assert.That(doc.RootElement.GetProperty("user").GetProperty("id").GetInt32(), Is.EqualTo(1));
    }

    [Test]
    public void RenamePropertyMovesValue()
    {
        var plan = new JsonRewritePlanBuilder().RenameProperty("/meta/id", "requestId").Build();

        using var sink = new StringWriter();
        using var writer = new JsonRewriteWriter(sink, plan);

        writer.Write("{\"meta\":{\"id\":\"abc\",\"other\":1}}");
        writer.Flush();

        using var doc = JsonDocument.Parse(sink.ToString());
        var meta = doc.RootElement.GetProperty("meta");

        Assert.That(meta.TryGetProperty("id", out _), Is.False);
        Assert.That(meta.GetProperty("requestId").GetString(), Is.EqualTo("abc"));
        Assert.That(meta.GetProperty("other").GetInt32(), Is.EqualTo(1));
    }

    [Test]
    public void MalformedJsonCanBeRepaired()
    {
        var plan = new JsonRewritePlanBuilder().Build();

        using var sink = new StringWriter();

        using var writer = new JsonRewriteWriter(
            sink,
            plan,
            new()
            {
                OnMalformedJson = bad => bad + "}",
            });

        writer.Write("{\"a\":1");
        writer.Flush();

        using var doc = JsonDocument.Parse(sink.ToString());
        Assert.That(doc.RootElement.GetProperty("a").GetInt32(), Is.EqualTo(1));
    }

    [Test]
    public void RequireThrowsWhenMissing()
    {
        var plan = new JsonRewritePlanBuilder().Require("/id").Build();

        using var sink = new StringWriter();
        using var writer = new JsonRewriteWriter(sink, plan);

        Assert.Throws<FormatException>(() =>
        {
            writer.Write("{}");
            writer.Flush();
        });
    }

    [Test]
    public void RequirePredicateFailureThrows()
    {
        var plan = new JsonRewritePlanBuilder().Require("/value", literal => literal == "expected", "wrong value").Build();

        using var sink = new StringWriter();
        using var writer = new JsonRewriteWriter(sink, plan);

        Assert.Throws<FormatException>(() =>
        {
            writer.Write("{\"value\":\"other\"}");
            writer.Flush();
        });
    }

    [Test]
    public void CaptureCollectsLiteral()
    {
        var captured = new List<string>();
        var plan = new JsonRewritePlanBuilder().Capture("/response/id", v => captured.Add(v)).Build();

        using var sink = new StringWriter();
        using var writer = new JsonRewriteWriter(sink, plan);

        writer.Write("{\"response\":{\"id\":42,\"value\":\"ok\"}}");
        writer.Flush();

        Assert.That(captured, Is.EqualTo(new[] { "42" }));
    }

    [Test]
    public void UnwrapPrefixIsApplied()
    {
        var plan = new JsonRewritePlanBuilder().Build();

        using var sink = new StringWriter();

        using var writer = new JsonRewriteWriter(
            sink,
            plan,
            new()
            {
                UnwrapPrefix = "data:",
                PrefixRequired = true,
            });

        writer.Write("data:{\"a\":1}");
        writer.Flush();

        Assert.That(sink.ToString(), Is.EqualTo("{\"a\":1}"));

        using var sink2 = new StringWriter();

        using var writer2 = new JsonRewriteWriter(
            sink2,
            plan,
            new()
            {
                UnwrapPrefix = "data:",
                PrefixRequired = true,
            });

        Assert.Throws<FormatException>(() =>
        {
            writer2.Write("{\"a\":1}");
            writer2.Flush();
        });
    }

    [Test]
    public void ReplaceInStringPredicateRewritesValues()
    {
        var plan = new JsonRewritePlanBuilder().ReplaceInString(v => v.Contains("secret", StringComparison.Ordinal), _ => "***").Build();

        using var sink = new StringWriter();
        using var writer = new JsonRewriteWriter(sink, plan);

        writer.Write("{\"token\":\"secret\",\"other\":\"open\"}");
        writer.Flush();

        using var doc = JsonDocument.Parse(sink.ToString());
        Assert.That(doc.RootElement.GetProperty("token").GetString(), Is.EqualTo("***"));
        Assert.That(doc.RootElement.GetProperty("other").GetString(), Is.EqualTo("open"));
    }

    [Test]
    public void ReplaceInStringPointerTargetsOnlyMatchingNode()
    {
        var plan = new JsonRewritePlanBuilder().ReplaceInString("/user/name", v => v.ToUpperInvariant()).Build();

        using var sink = new StringWriter();
        using var writer = new JsonRewriteWriter(sink, plan);

        writer.Write("{\"user\":{\"name\":\"john\",\"title\":\"dev\"}}");
        writer.Flush();

        using var doc = JsonDocument.Parse(sink.ToString());
        Assert.That(doc.RootElement.GetProperty("user").GetProperty("name").GetString(), Is.EqualTo("JOHN"));
        Assert.That(doc.RootElement.GetProperty("user").GetProperty("title").GetString(), Is.EqualTo("dev"));
    }
}
