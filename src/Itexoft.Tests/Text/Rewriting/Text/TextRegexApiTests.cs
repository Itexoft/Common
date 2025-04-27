// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Text.RegularExpressions;
using Itexoft.Text.Rewriting.Internal.Runtime;
using Itexoft.Text.Rewriting.Primitives;
using Itexoft.Text.Rewriting.Text;
using Itexoft.Text.Rewriting.Text.Dsl;
using Itexoft.Text.Rewriting.Text.Internal.Matching;

namespace Itexoft.Tests.Text.Rewriting.Text;

public sealed class TextRegexApiTests
{
    [Test]
    public void RegexOverloadKeepsProvidedInstance()
    {
        var regex = new Regex("abc", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
        var plan = this.BuildDslPlan(dsl => dsl.Regex(regex, 8).Replace("x"));

        var rule = plan.regexRules.Single();

        Assert.That(rule.Regex, Is.SameAs(regex));
        Assert.That(rule.Regex.Options, Is.EqualTo(regex.Options));
        Assert.That(rule.Regex.MatchTimeout, Is.EqualTo(regex.MatchTimeout));
    }

    [Test]
    public void RegexStringOverloadUsesDefaultOptions()
    {
        var plan = this.BuildDslPlan(dsl => dsl.Regex("abc", 8).Replace("x"));
        var options = plan.regexRules.Single().Regex.Options;
        var expected = RegexOptions.CultureInvariant | RegexOptions.Compiled;

        Assert.That(options & expected, Is.EqualTo(expected));
    }

    [Test]
    public void PlanBuilderStringRegexUsesDefaultOptions()
    {
        var builder = new TextRewritePlanBuilder();
        builder.HookRegex("abc", 8, (_, _) => { });
        var plan = builder.Build();
        var options = plan.regexRules.Single().Regex.Options;
        var expected = RegexOptions.CultureInvariant | RegexOptions.Compiled;

        Assert.That(options & expected, Is.EqualTo(expected));
    }

    [Test]
    public void HookRegexStringAndRegexOverloadsMatchEqually()
    {
        var regex = new Regex("abc", RegexOptions.CultureInvariant | RegexOptions.Compiled);
        var stringCount = 0;
        var regexCount = 0;

        var stringPlan = this.BuildPlan(b => b.HookRegex("abc", 8, (_, _) => stringCount++));
        var regexPlan = this.BuildPlan(b => b.HookRegex(regex, 8, (_, _) => regexCount++));

        var input = "abc";
        var stringOutput = this.Execute(stringPlan, input);
        var regexOutput = this.Execute(regexPlan, input);

        Assert.That(stringOutput, Is.EqualTo(regexOutput));
        Assert.That(stringCount, Is.EqualTo(1));
        Assert.That(regexCount, Is.EqualTo(1));
    }

    [Test]
    public void RemoveRegexStringAndRegexOverloadsMatchEqually()
    {
        var regex = new Regex("a", RegexOptions.CultureInvariant | RegexOptions.Compiled);

        var stringPlan = this.BuildPlan(b => b.RemoveRegex("a", 4));
        var regexPlan = this.BuildPlan(b => b.RemoveRegex(regex, 4));

        var input = "a";

        Assert.That(this.Execute(stringPlan, input), Is.EqualTo(this.Execute(regexPlan, input)));
    }

    [Test]
    public void ReplaceRegexStringAndRegexOverloadsMatchEqually()
    {
        var regex = new Regex("a", RegexOptions.CultureInvariant | RegexOptions.Compiled);

        var stringPlan = this.BuildPlan(b => b.ReplaceRegex("a", 4, "b"));
        var regexPlan = this.BuildPlan(b => b.ReplaceRegex(regex, 4, "b"));

        var input = "a";

        Assert.That(this.Execute(stringPlan, input), Is.EqualTo("b"));
        Assert.That(this.Execute(regexPlan, input), Is.EqualTo("b"));
    }

    [Test]
    public void AddRegexRuleStringAndRegexOverloadsMatchEqually()
    {
        var regex = new Regex("a", RegexOptions.CultureInvariant | RegexOptions.Compiled);
        var replacementRule = new StandardRuleEntry(MatchAction.Replace, 0, 0, 4, "z", null, null, null, null, null, null);

        var stringPlan = this.BuildPlan(b => b.AddRegexRule("a", 4, replacementRule));

        var regexPlan = this.BuildPlan(b => b.AddRegexRule(
            regex,
            4,
            new StandardRuleEntry(MatchAction.Replace, 0, 0, 4, "z", null, null, null, null, null, null)));

        var input = "a";
        var expected = "z";

        Assert.That(this.Execute(stringPlan, input), Is.EqualTo(expected));
        Assert.That(this.Execute(regexPlan, input), Is.EqualTo(expected));
    }

    private TextRewritePlan BuildDslPlan(Action<TextDsl<StubHandlers>> configure)
    {
        var builder = new TextRewritePlanBuilder();
        var scope = new HandlerScope<StubHandlers>();
        var names = new List<string?>();
        var groups = new List<string?>();
        var dsl = new TextDsl<StubHandlers>(builder, scope, names, groups);

        configure(dsl);

        return builder.Build();
    }

    private TextRewritePlan BuildPlan(Action<TextRewritePlanBuilder> configure)
    {
        var builder = new TextRewritePlanBuilder();
        configure(builder);

        return builder.Build();
    }

    private string Execute(TextRewritePlan plan, string input)
    {
        using var writer = new StringWriter();
        using var rewrite = new TextRewriteWriter(writer, plan, new() { FlushBehavior = FlushBehavior.Commit });

        rewrite.Write(input);
        rewrite.Flush();

        return writer.ToString();
    }

    private sealed class StubHandlers { }
}
