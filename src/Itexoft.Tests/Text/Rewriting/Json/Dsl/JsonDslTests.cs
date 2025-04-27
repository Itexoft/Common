// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Text.Json;
using Itexoft.Text.Rewriting.Json;
using Itexoft.Text.Rewriting.Json.Dsl;

namespace Itexoft.Tests.Text.Rewriting.Json.Dsl;

public sealed class JsonDslTests
{
    [Test]
    public void ReplaceInStringContextPredicateUsesPointer()
    {
        var plan = new JsonRewritePlanBuilder().ReplaceInString(
            ctx => ctx.Pointer == "/payload/message" && ctx.Value.Contains("token", StringComparison.Ordinal),
            ctx => $"{ctx.Pointer}:{ctx.Value.ToUpperInvariant()}").Build();

        using var sink = new StringWriter();
        using var writer = new JsonRewriteWriter(sink, plan);

        writer.Write("{\"payload\":{\"message\":\"token-abc\",\"other\":\"token\"},\"token\":\"token\"}");
        writer.Flush();

        using var doc = JsonDocument.Parse(sink.ToString());
        var payload = doc.RootElement.GetProperty("payload");
        Assert.That(payload.GetProperty("message").GetString(), Is.EqualTo("/payload/message:TOKEN-ABC"));
        Assert.That(payload.GetProperty("other").GetString(), Is.EqualTo("token"));
        Assert.That(doc.RootElement.GetProperty("token").GetString(), Is.EqualTo("token"));
    }

    [Test]
    public void KernelScopesHandlersPerSession()
    {
        var kernel = JsonKernel<JsonHandler>.Compile(dsl =>
        {
            dsl.Capture("/user/id", (h, v) => h.Captured.Add(v));

            dsl.ReplaceInString(
                "/payload",
                (h, v) =>
                {
                    h.PayloadSeen++;

                    return v.ToUpperInvariant();
                });
        });

        var sink1 = new StringWriter();
        var sink2 = new StringWriter();
        var handlers1 = new JsonHandler();
        var handlers2 = new JsonHandler();

        using (var session = kernel.CreateSession(sink1, handlers1))
        {
            session.Write("{\"user\":{\"id\":\"1\"},\"payload\":\"first\"}");
            session.Commit();
        }

        using (var session = kernel.CreateSession(sink2, handlers2))
        {
            session.Write("{\"user\":{\"id\":\"2\"},\"payload\":\"second\"}");
            session.Commit();
        }

        Assert.That(handlers1.Captured.Select(v => v.Trim('"')), Is.EqualTo((string[])["1"]));
        Assert.That(handlers2.Captured.Select(v => v.Trim('"')), Is.EqualTo((string[])["2"]));
        Assert.That(handlers1.PayloadSeen, Is.EqualTo(1));
        Assert.That(handlers2.PayloadSeen, Is.EqualTo(1));

        using var doc1 = JsonDocument.Parse(sink1.ToString());
        using var doc2 = JsonDocument.Parse(sink2.ToString());
        Assert.That(doc1.RootElement.GetProperty("payload").GetString(), Is.EqualTo("FIRST"));
        Assert.That(doc2.RootElement.GetProperty("payload").GetString(), Is.EqualTo("SECOND"));
    }

    [Test]
    public void SessionUsesFramingToAutoCommit()
    {
        var kernel = JsonKernel<JsonHandler>.Compile(dsl => { dsl.ReplaceValue("/flag", "y"); });

        var sink = new StringWriter();
        var framing = new DelimiterFraming("\n\n");

        using (var session = kernel.CreateSession(sink, new(), new() { Framing = framing }))
            session.Write("{\"flag\":\"n\"}\n\n{\"flag\":\"n\"}\n\n");

        var rendered = sink.ToString();
        Assert.That(rendered, Is.EqualTo("{\"flag\":\"y\"}{\"flag\":\"y\"}"));
    }

    [Test]
    public void FramingCutsFramesAcrossChunks()
    {
        var kernel = JsonKernel<JsonHandler>.Compile(dsl => { dsl.ReplaceValue("/flag", "y"); });

        var sink = new StringWriter();
        var framing = new DelimiterFraming("\n\n");

        using (var session = kernel.CreateSession(sink, new(), new() { Framing = framing }))
        {
            session.Write("{\"flag\":\"n\"}\n");
            session.Write("\n{\"flag\":\"n\"}\n");
            session.Write("\n");
        }

        var rendered = sink.ToString();
        Assert.That(rendered, Is.EqualTo("{\"flag\":\"y\"}{\"flag\":\"y\"}"));
    }

    private sealed class JsonHandler
    {
        public List<string> Captured { get; } = [];

        public int PayloadSeen { get; set; }
    }

    private sealed class DelimiterFraming(string delimiter) : IJsonFraming
    {
        public bool TryCutFrame(ref ReadOnlySpan<char> buffer, out ReadOnlySpan<char> frame)
        {
            var index = buffer.IndexOf(delimiter.AsSpan());

            if (index < 0)
            {
                frame = default;

                return false;
            }

            frame = buffer[..index];
            buffer = buffer[(index + delimiter.Length)..];

            return true;
        }

        public bool TryCutFrame(ref ReadOnlyMemory<char> buffer, out ReadOnlyMemory<char> frame)
        {
            var index = buffer.Span.IndexOf(delimiter.AsSpan());

            if (index < 0)
            {
                frame = default;

                return false;
            }

            frame = buffer[..index];
            buffer = buffer[(index + delimiter.Length)..];

            return true;
        }
    }
}
