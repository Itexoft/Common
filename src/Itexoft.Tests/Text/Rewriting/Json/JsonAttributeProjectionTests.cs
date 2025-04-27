// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Text.Rewriting.Json.Attributes;
using Itexoft.Text.Rewriting.Json.Dsl;

namespace Itexoft.Tests.Text.Rewriting.Json;

public sealed class JsonAttributeProjectionTests
{
    [Test]
    public void JsonPointerAttributeOverridesPath()
    {
        var kernel = JsonKernel<PointerHandlers>.CompileFromAttributes(typeof(PointerHandlers));
        var handlers = new PointerHandlers();

        using var session = kernel.CreateSession(new StringWriter(), handlers);

        session.Write("""{"custom_id":15,"user_name":"bob"}""");
        session.Commit();

        Assert.That(handlers.Captured, Is.Not.Null);
        Assert.That(handlers.Captured!.Id, Is.EqualTo(15));
        Assert.That(handlers.Captured.UserName, Is.EqualTo("bob"));
    }

    [Test]
    public void InvalidConverterTypeThrows() =>
        Assert.Throws<InvalidOperationException>(() => JsonKernel<PointerHandlers>.CompileFromAttributes(typeof(BadConverterHandlers)));

    [Test]
    public void InvalidJsonCaptureObjectSignatureThrows() =>
        Assert.Throws<InvalidOperationException>(() => JsonKernel<PointerHandlers>.CompileFromAttributes(typeof(BadSignatureHandlers)));

    private sealed class PointerHandlers
    {
        public PointerModel? Captured { get; private set; }

        [JsonCaptureObject("/", typeof(PointerModel))]
        public static void Capture(PointerHandlers handlers, PointerModel model) => handlers.Captured = model;
    }

    private sealed class PointerModel
    {
        [JsonPointer("/custom_id")] public int Id { get; set; }

        public string UserName { get; set; } = string.Empty;
    }

    private sealed class BadConverterHandlers
    {
        [JsonCaptureObject("/", typeof(BadConverterModel))]
        public static void Capture(BadConverterHandlers handlers, BadConverterModel model) { }
    }

    private sealed class BadConverterModel
    {
        [JsonPointer("/value", Converter = typeof(BadConverter))]
        public int Value { get; set; }
    }

    private sealed class BadConverter { }

    private sealed class BadSignatureHandlers
    {
        [JsonCaptureObject("/", typeof(PointerModel))]
        public static void Capture(PointerModel model) { }
    }
}
