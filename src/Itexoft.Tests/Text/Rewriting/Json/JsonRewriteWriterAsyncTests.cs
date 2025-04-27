// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Text.Json;
using Itexoft.Text.Rewriting.Json;

namespace Itexoft.Tests.Text.Rewriting.Json;

public sealed class JsonRewriteWriterAsyncTests
{
    [Test]
    public async Task PassThroughWhenNoRulesAsync()
    {
        var plan = new JsonRewritePlanBuilder().Build();

        await using var sink = new StringWriter();
        await using var writer = new JsonRewriteWriter(sink, plan);

        await writer.WriteAsync("{\"a\":1,\"b\":2}");
        await writer.FlushAsync();

        Assert.That(sink.ToString(), Is.EqualTo("{\"a\":1,\"b\":2}"));
    }

    [Test]
    public async Task ReplaceValueRewritesScalarAsync()
    {
        var plan = new JsonRewritePlanBuilder()
            .ReplaceValue("/user/name", "***")
            .Build();

        await using var sink = new StringWriter();
        await using var writer = new JsonRewriteWriter(sink, plan);

        await writer.WriteAsync("{\"user\":{\"name\":\"John\",\"id\":1}}");
        await writer.FlushAsync();

        using var doc = JsonDocument.Parse(sink.ToString());
        var user = doc.RootElement.GetProperty("user");
        Assert.That(user.GetProperty("name").GetString(), Is.EqualTo("***"));
        Assert.That(user.GetProperty("id").GetInt32(), Is.EqualTo(1));
    }

    [Test]
    public async Task RenamePropertyMovesValueAsync()
    {
        var plan = new JsonRewritePlanBuilder()
            .RenameProperty("/meta/id", "requestId")
            .Build();

        await using var sink = new StringWriter();
        await using var writer = new JsonRewriteWriter(sink, plan);

        await writer.WriteAsync("{\"meta\":{\"id\":\"abc\",\"other\":1}}");
        await writer.FlushAsync();

        using var doc = JsonDocument.Parse(sink.ToString());
        var meta = doc.RootElement.GetProperty("meta");

        Assert.That(meta.TryGetProperty("id", out _), Is.False);
        Assert.That(meta.GetProperty("requestId").GetString(), Is.EqualTo("abc"));
        Assert.That(meta.GetProperty("other").GetInt32(), Is.EqualTo(1));
    }

    [Test]
    public async Task MalformedJsonCanBeRepairedAsync()
    {
        var plan = new JsonRewritePlanBuilder().Build();

        await using var sink = new StringWriter();
        await using var writer = new JsonRewriteWriter(
            sink,
            plan,
            new()
            {
                OnMalformedJson = bad => bad + "}"
            });

        await writer.WriteAsync("{\"a\":1");
        await writer.FlushAsync();

        using var doc = JsonDocument.Parse(sink.ToString());
        Assert.That(doc.RootElement.GetProperty("a").GetInt32(), Is.EqualTo(1));
    }

    [Test]
    public async Task RequireThrowsWhenMissingAsync()
    {
        var plan = new JsonRewritePlanBuilder()
            .Require("/id")
            .Build();

        await using var sink = new StringWriter();
        await using var writer = new JsonRewriteWriter(sink, plan);

        Assert.ThrowsAsync<FormatException>(async () =>
        {
            await writer.WriteAsync("{}");
            await writer.FlushAsync();
        });
    }

    [Test]
    public async Task CaptureAsyncCollectsLiteral()
    {
        var captured = new List<string>();
        var plan = new JsonRewritePlanBuilder()
            .CaptureAsync(
                "/response/id",
                v =>
                {
                    captured.Add(v);

                    return ValueTask.CompletedTask;
                })
            .Build();

        await using var sink = new StringWriter();
        await using var writer = new JsonRewriteWriter(sink, plan);

        await writer.WriteAsync("{\"response\":{\"id\":7}}");
        await writer.FlushAsync();

        Assert.That(captured, Is.EqualTo(new[] { "7" }));
    }

    [Test]
    public async Task UnwrapPrefixAsync()
    {
        var plan = new JsonRewritePlanBuilder().Build();

        await using var sink = new StringWriter();
        await using var writer = new JsonRewriteWriter(
            sink,
            plan,
            new()
            {
                UnwrapPrefix = "data:",
                PrefixRequired = true
            });

        await writer.WriteAsync("data:{\"a\":1}");
        await writer.FlushAsync();

        Assert.That(sink.ToString(), Is.EqualTo("{\"a\":1}"));
    }

    [Test]
    public async Task ReplaceInStringPredicateRewritesValuesAsync()
    {
        var plan = new JsonRewritePlanBuilder()
            .ReplaceInString(v => v.Contains("secret", StringComparison.Ordinal), _ => "***")
            .Build();

        await using var sink = new StringWriter();
        await using var writer = new JsonRewriteWriter(sink, plan);

        await writer.WriteAsync("{\"token\":\"secret\",\"other\":\"open\"}");
        await writer.FlushAsync();

        using var doc = JsonDocument.Parse(sink.ToString());
        Assert.That(doc.RootElement.GetProperty("token").GetString(), Is.EqualTo("***"));
        Assert.That(doc.RootElement.GetProperty("other").GetString(), Is.EqualTo("open"));
    }
}