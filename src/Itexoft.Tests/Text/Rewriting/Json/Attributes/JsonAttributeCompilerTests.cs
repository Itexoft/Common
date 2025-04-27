// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Text.Json;
using Itexoft.Text.Rewriting.Json.Attributes;
using Itexoft.Text.Rewriting.Json.Dsl;

namespace Itexoft.Tests.Text.Rewriting.Json.Attributes;

public sealed class JsonAttributeCompilerTests
{
    [Test]
    public void CompileFromAttributesAppliesRules()
    {
        var kernel = JsonKernel<JsonRuleHandlers>.CompileFromAttributes(typeof(JsonRules));
        var handlers = new JsonRuleHandlers();
        using var sink = new StringWriter();

        using (var session = kernel.CreateSession(sink, handlers))
        {
            session.Write("{\"required\":\"ok\",\"capture\":\"v\",\"payload\":\"secret\"}");
            session.Commit();
        }

        Assert.That(handlers.RequiredChecks, Is.EqualTo(1));
        Assert.That(handlers.Captured.Select(v => v.Trim('"')), Is.EqualTo((string[])["v"]));
        Assert.That(handlers.Replacements, Is.EqualTo(1));

        using var doc = JsonDocument.Parse(sink.ToString());
        Assert.That(doc.RootElement.GetProperty("payload").GetString(), Is.EqualTo("SECRET"));
    }

    [Test]
    public void CompileFromAttributesRejectsNonStaticRules()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => JsonKernel<JsonRuleHandlers>.CompileFromAttributes(typeof(JsonInstanceRules)));

        Assert.That(ex!.Message, Does.Contain(nameof(JsonInstanceRules.NonStatic)));
    }

    [Test]
    public void CompileFromAttributesRejectsInvalidCaptureSignature()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => JsonKernel<JsonRuleHandlers>.CompileFromAttributes(typeof(JsonBrokenRules)));

        Assert.That(ex!.Message, Does.Contain(nameof(JsonBrokenRules.CaptureWrong)));
    }

    private sealed class JsonRuleHandlers
    {
        public int RequiredChecks { get; set; }

        public List<string> Captured { get; } = [];

        public int Replacements { get; set; }
    }

    private static class JsonRules
    {
        [JsonRequire("/required")]
        public static bool EnsureRequired(JsonRuleHandlers handlers, string value)
        {
            handlers.RequiredChecks++;

            return true;
        }

        [JsonCapture("/capture")]
        public static void Capture(JsonRuleHandlers handlers, string value) => handlers.Captured.Add(value);

        [JsonReplaceInString(Pointer = "/payload")]
        public static string Rewrite(JsonRuleHandlers handlers, string value)
        {
            handlers.Replacements++;

            return value.ToUpperInvariant();
        }
    }

    private sealed class JsonInstanceRules
    {
        [JsonRequire("/required")]
        public bool NonStatic(JsonRuleHandlers handlers, string value) => true;
    }

    private static class JsonBrokenRules
    {
        [JsonCapture("/bad")]
        public static int CaptureWrong(JsonRuleHandlers handlers, string value) => 0;
    }
}
