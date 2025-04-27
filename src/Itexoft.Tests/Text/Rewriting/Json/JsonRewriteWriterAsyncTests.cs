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

        var sink = new StringWriter();
        await using var sink1 = sink.ConfigureAwait(false);
        var writer = new JsonRewriteWriter(sink, plan);
        await using var writer1 = writer.ConfigureAwait(false);

        await writer.WriteAsync("{\"a\":1,\"b\":2}").ConfigureAwait(false);
        await writer.FlushAsync().ConfigureAwait(false);

        Assert.That(sink.ToString(), Is.EqualTo("{\"a\":1,\"b\":2}"));
    }

    [Test]
    public async Task ReplaceValueRewritesScalarAsync()
    {
        var plan = new JsonRewritePlanBuilder().ReplaceValue("/user/name", "***").Build();

        var sink = new StringWriter();
        await using var sink1 = sink.ConfigureAwait(false);
        var writer = new JsonRewriteWriter(sink, plan);
        await using var writer1 = writer.ConfigureAwait(false);

        await writer.WriteAsync("{\"user\":{\"name\":\"John\",\"id\":1}}").ConfigureAwait(false);
        await writer.FlushAsync().ConfigureAwait(false);

        using var doc = JsonDocument.Parse(sink.ToString());
        var user = doc.RootElement.GetProperty("user");
        Assert.That(user.GetProperty("name").GetString(), Is.EqualTo("***"));
        Assert.That(user.GetProperty("id").GetInt32(), Is.EqualTo(1));
    }

    [Test]
    public async Task RenamePropertyMovesValueAsync()
    {
        var plan = new JsonRewritePlanBuilder().RenameProperty("/meta/id", "requestId").Build();

        var sink = new StringWriter();
        await using var sink1 = sink.ConfigureAwait(false);
        var writer = new JsonRewriteWriter(sink, plan);
        await using var writer1 = writer.ConfigureAwait(false);

        await writer.WriteAsync("{\"meta\":{\"id\":\"abc\",\"other\":1}}").ConfigureAwait(false);
        await writer.FlushAsync().ConfigureAwait(false);

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

        var sink = new StringWriter();
        await using var sink1 = sink.ConfigureAwait(false);

        var writer = new JsonRewriteWriter(
            sink,
            plan,
            new()
            {
                OnMalformedJson = bad => bad + "}",
            });

        await using var writer1 = writer.ConfigureAwait(false);

        await writer.WriteAsync("{\"a\":1").ConfigureAwait(false);
        await writer.FlushAsync().ConfigureAwait(false);

        using var doc = JsonDocument.Parse(sink.ToString());
        Assert.That(doc.RootElement.GetProperty("a").GetInt32(), Is.EqualTo(1));
    }

    [Test]
    public async Task RequireThrowsWhenMissingAsync()
    {
        var plan = new JsonRewritePlanBuilder().Require("/id").Build();

        var sink = new StringWriter();
        await using var sink1 = sink.ConfigureAwait(false);
        var writer = new JsonRewriteWriter(sink, plan);
        await using var writer1 = writer.ConfigureAwait(false);

        Assert.ThrowsAsync<FormatException>(async () =>
        {
            await writer.WriteAsync("{}").ConfigureAwait(false);
            await writer.FlushAsync().ConfigureAwait(false);
        });
    }

    [Test]
    public async Task CaptureAsyncCollectsLiteral()
    {
        var captured = new List<string>();

        var plan = new JsonRewritePlanBuilder().CaptureAsync(
            "/response/id",
            v =>
            {
                captured.Add(v);

                return ValueTask.CompletedTask;
            }).Build();

        var sink = new StringWriter();
        await using var sink1 = sink.ConfigureAwait(false);
        var writer = new JsonRewriteWriter(sink, plan);
        await using var writer1 = writer.ConfigureAwait(false);

        await writer.WriteAsync("{\"response\":{\"id\":7}}").ConfigureAwait(false);
        await writer.FlushAsync().ConfigureAwait(false);

        Assert.That(captured, Is.EqualTo(new[] { "7" }));
    }

    [Test]
    public async Task UnwrapPrefixAsync()
    {
        var plan = new JsonRewritePlanBuilder().Build();

        var sink = new StringWriter();
        await using var sink1 = sink.ConfigureAwait(false);

        var writer = new JsonRewriteWriter(
            sink,
            plan,
            new()
            {
                UnwrapPrefix = "data:",
                PrefixRequired = true,
            });

        await using var writer1 = writer.ConfigureAwait(false);

        await writer.WriteAsync("data:{\"a\":1}").ConfigureAwait(false);
        await writer.FlushAsync().ConfigureAwait(false);

        Assert.That(sink.ToString(), Is.EqualTo("{\"a\":1}"));
    }

    [Test]
    public async Task ReplaceInStringPredicateRewritesValuesAsync()
    {
        var plan = new JsonRewritePlanBuilder().ReplaceInString(v => v.Contains("secret", StringComparison.Ordinal), _ => "***").Build();

        var sink = new StringWriter();
        await using var sink1 = sink.ConfigureAwait(false);
        var writer = new JsonRewriteWriter(sink, plan);
        await using var writer1 = writer.ConfigureAwait(false);

        await writer.WriteAsync("{\"token\":\"secret\",\"other\":\"open\"}").ConfigureAwait(false);
        await writer.FlushAsync().ConfigureAwait(false);

        using var doc = JsonDocument.Parse(sink.ToString());
        Assert.That(doc.RootElement.GetProperty("token").GetString(), Is.EqualTo("***"));
        Assert.That(doc.RootElement.GetProperty("other").GetString(), Is.EqualTo("open"));
    }
}
