// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Text.Rewriting.Json;

namespace Itexoft.Tests.Text.Rewriting.Json;

public sealed class JsonRewritePlanProjectionTests
{
    [Test]
    public void CaptureObjectUsesAutoProjectionAtRoot()
    {
        var captured = new List<ResponseModel>();
        var plan = new JsonRewritePlanBuilder().CaptureObject<ResponseModel>("/", captured.Add).Build();

        using var sink = new StringWriter();
        using var writer = new JsonRewriteWriter(sink, plan);

        writer.Write("""{"message":"hi","id":7}""");
        writer.Flush();

        Assert.That(captured, Has.Count.EqualTo(1));
        Assert.That(captured[0].Message, Is.EqualTo("hi"));
        Assert.That(captured[0].Id, Is.EqualTo(7));
    }

    [Test]
    public void CaptureObjectAtNestedPath()
    {
        var captured = new List<ResponseModel>();
        var plan = new JsonRewritePlanBuilder().CaptureObject<ResponseModel>("/response", captured.Add).Build();

        using var sink = new StringWriter();
        using var writer = new JsonRewriteWriter(sink, plan);

        writer.Write("""{"response":{"message":"nested","id":3}}""");
        writer.Flush();

        Assert.That(captured, Has.Count.EqualTo(1));
        Assert.That(captured[0].Message, Is.EqualTo("nested"));
        Assert.That(captured[0].Id, Is.EqualTo(3));
    }

    [Test]
    public void CaptureManyProjectsEachArrayElement()
    {
        var captured = new List<NestedItem>();
        var plan = new JsonRewritePlanBuilder().CaptureMany<NestedItem>("/items", captured.Add).Build();

        using var sink = new StringWriter();
        using var writer = new JsonRewriteWriter(sink, plan);

        writer.Write("""{"items":[{"value":1},{"value":2}]}""");
        writer.Flush();

        Assert.That(captured, Has.Count.EqualTo(2));
        Assert.That(captured.Select(x => x.Value), Is.EquivalentTo((int[])[1, 2]));
    }

    [Test]
    public void CaptureValueParsesScalar()
    {
        var values = new List<int>();
        var plan = new JsonRewritePlanBuilder().CaptureValue<int>("/value", values.Add).Build();

        using var sink = new StringWriter();
        using var writer = new JsonRewriteWriter(sink, plan);

        writer.Write("""{"value":42}""");
        writer.Flush();

        Assert.That(values, Is.EqualTo((int[])[42]));
    }

    [Test]
    public void CaptureValueCapturesMultipleArrayItems()
    {
        var values = new List<string>();
        var pointers = new List<string>();

        var plan = new JsonRewritePlanBuilder().ReplaceInString(
            _ => true,
            ctx =>
            {
                pointers.Add($"{ctx.Pointer}:{ctx.Value}");

                return ctx.Value;
            }).CaptureValue<string>("/items/0", values.Add).CaptureValue<string>("/items/1", values.Add).Build();

        using var sink = new StringWriter();
        using var writer = new JsonRewriteWriter(sink, plan);

        writer.Write("""{"items":["a","b"]}""");
        writer.Flush();

        Assert.That(pointers, Is.EqualTo((string[])["/items/0:a", "/items/1:b"]));
        Assert.That(values, Is.EqualTo((string[])["a", "b"]));
    }

    [Test]
    public void CaptureValueThrowsOnInvalidLiteral()
    {
        var plan = new JsonRewritePlanBuilder().CaptureValue<int>("/value", _ => { }).Build();

        using var sink = new StringWriter();
        using var writer = new JsonRewriteWriter(sink, plan);

        Assert.Throws<FormatException>(() =>
        {
            writer.Write("""{"value":"oops"}""");
            writer.Flush();
        });
    }

    private sealed class ResponseModel
    {
        public string Message { get; set; } = string.Empty;

        public int Id { get; set; }
    }

    private sealed class NestedItem
    {
        public int Value { get; set; }
    }
}
