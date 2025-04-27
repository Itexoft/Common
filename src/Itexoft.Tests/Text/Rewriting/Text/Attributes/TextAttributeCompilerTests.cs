// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Text.Rewriting.Primitives;
using Itexoft.Text.Rewriting.Text.Attributes;
using Itexoft.Text.Rewriting.Text.Dsl;

namespace Itexoft.Tests.Text.Rewriting.Text.Attributes;

public sealed class TextAttributeCompilerTests
{
    [Test]
    public void CompileFromAttributesAppliesLiteralRegexAndTail()
    {
        var kernel = TextKernel<TextRuleHandlers>.CompileFromAttributes(typeof(TextRules));
        var handlers = new TextRuleHandlers();

        using var sink = new StringWriter();

        using (var session = kernel.CreateSession(sink, handlers, new() { FlushBehavior = FlushBehavior.Commit }))
        {
            session.Write("secret id=42 END");
            session.Flush();
        }

        Assert.That(handlers.SecretHits, Is.EqualTo(1));
        Assert.That(handlers.IdHits, Is.EqualTo(1));
        Assert.That(sink.ToString(), Is.EqualTo("***  !"));
    }

    [Test]
    public void CompileFromAttributesRejectsNonStaticMethods()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => TextKernel<TextRuleHandlers>.CompileFromAttributes(typeof(InstanceRules)));

        Assert.That(ex!.Message, Does.Contain(nameof(InstanceRules.NonStatic)));
    }

    [Test]
    public void CompileFromAttributesRejectsReplaceWithWrongReturnType()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => TextKernel<TextRuleHandlers>.CompileFromAttributes(typeof(BrokenReplaceRules)));

        Assert.That(ex!.Message, Does.Contain(nameof(BrokenReplaceRules.InvalidReplace)));
    }

    private sealed class TextRuleHandlers
    {
        public int SecretHits { get; set; }

        public int IdHits { get; set; }

        public int TailHits { get; set; }
    }

    private static class TextRules
    {
        [TextLiteralRule("secret", Action = MatchAction.Replace)]
        public static string OnSecret(TextRuleHandlers handlers, int ruleId, ReadOnlySpan<char> match)
        {
            handlers.SecretHits++;

            return "***";
        }

        [TextRegexRule(@"id=\d+", 16, Action = MatchAction.Remove)]
        public static void OnId(TextRuleHandlers handlers, int ruleId, ReadOnlySpan<char> match) => handlers.IdHits++;

        [TextTailRule(8)]
        public static TextTailDecision Tail(TextRuleHandlers handlers, ReadOnlySpan<char> tail)
        {
            if (tail.EndsWith("END", StringComparison.Ordinal))
            {
                handlers.TailHits++;

                return new(3, MatchAction.Replace, "!");
            }

            return new(0, MatchAction.None, null);
        }
    }

    private sealed class InstanceRules
    {
        [TextLiteralRule("x")]
        public void NonStatic(TextRuleHandlers handlers, int id, ReadOnlySpan<char> match) => handlers.SecretHits++;
    }

    private static class BrokenReplaceRules
    {
        [TextLiteralRule("x", Action = MatchAction.Replace)]
        public static int InvalidReplace(TextRuleHandlers handlers, int id, ReadOnlySpan<char> match) => 0;
    }
}
