// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Extensions;
using Itexoft.Text.Rewriting.Internal.Attributes;
using Itexoft.Text.Rewriting.Internal.Runtime;
using Itexoft.Text.Rewriting.Primitives;

namespace Itexoft.Text.Rewriting.Text.Dsl;

/// <summary>
/// Compiles text rewrite rules into an immutable plan and produces streaming sessions bound to handler instances.
/// </summary>
public sealed class TextKernel<THandlers> where THandlers : class
{
    private readonly RuleDescriptor[] descriptors;
    private readonly TextRewritePlan plan;
    private readonly RuleInfo[] rules;
    private readonly HandlerScope<THandlers> scope;

    private TextKernel(TextRewritePlan plan, HandlerScope<THandlers> scope, RuleInfo[] rules, RuleDescriptor[] descriptors)
    {
        this.plan = plan;
        this.scope = scope;
        this.rules = rules;
        this.descriptors = descriptors;
    }

    /// <summary>
    /// Compiles a plan using the fluent DSL.
    /// </summary>
    public static TextKernel<THandlers> Compile(Action<TextDsl<THandlers>> configure, TextCompileOptions? options = null)
    {
        configure.Required();

        options ??= new();

        var scope = new HandlerScope<THandlers>();
        var builder = new TextRewritePlanBuilder();
        var names = new List<string?>();
        var groups = new List<string?>();
        var dsl = new TextDsl<THandlers>(builder, scope, names, groups);

        configure(dsl);

        var plan = builder.Build(options);
        var ruleInfos = BuildRuleInfos(plan.RuleCount, names, groups);
        var descriptors = BuildRuleDescriptors(plan, ruleInfos);

        return new(plan, scope, ruleInfos, descriptors);
    }

    /// <summary>
    /// Compiles a plan using rules discovered on the provided types.
    /// </summary>
    public static TextKernel<THandlers> CompileFromAttributes(params Type[] ruleContainers)
    {
        ruleContainers.Required();

        var scope = new HandlerScope<THandlers>();
        var builder = new TextRewritePlanBuilder();
        var names = new List<string?>();
        var groups = new List<string?>();
        var dsl = new TextDsl<THandlers>(builder, scope, names, groups);

        foreach (var type in ruleContainers)
            AttributeCompiler<THandlers>.ApplyText(type, dsl);

        var plan = builder.Build();
        var ruleInfos = BuildRuleInfos(plan.RuleCount, names, groups);
        var descriptors = BuildRuleDescriptors(plan, ruleInfos);

        return new(plan, scope, ruleInfos, descriptors);
    }

    /// <summary>
    /// Creates a stateful text session bound to a handler instance and output.
    /// </summary>
    public TextSession<THandlers> CreateSession(TextWriter output, THandlers handlers, TextRuntimeOptions? options = null)
    {
        output.Required();

        if (handlers is null)
            throw new ArgumentNullException(nameof(handlers));

        return new(this.plan, this.scope, this.rules, output, handlers, options);
    }

    public IReadOnlyList<RuleDescriptor> Describe() => this.descriptors;

    private static RuleInfo[] BuildRuleInfos(int ruleCount, List<string?> names, List<string?> groups)
    {
        if (names.Count < ruleCount)
        {
            while (names.Count < ruleCount)
                names.Add(null);
        }

        if (groups.Count < ruleCount)
        {
            while (groups.Count < ruleCount)
                groups.Add(null);
        }

        var result = new RuleInfo[ruleCount];

        for (var i = 0; i < ruleCount; i++)
            result[i] = new(i, i < names.Count ? names[i] : null, i < groups.Count ? groups[i] : null);

        return result;
    }

    private static RuleDescriptor[] BuildRuleDescriptors(TextRewritePlan plan, RuleInfo[] ruleInfos)
    {
        var descriptors = new RuleDescriptor[plan.RuleCount];

        for (var i = 0; i < plan.RuleCount; i++)
        {
            var rule = plan.Rules[i];
            var kind = plan.ruleKinds[i] ?? "Custom";

            descriptors[i] = new(
                i,
                ruleInfos[i].Name,
                ruleInfos[i].Group,
                "Text",
                kind,
                rule.Priority,
                rule.Order,
                rule.Action,
                rule.MaxMatchLength,
                plan.ruleTargets[i]);
        }

        return descriptors;
    }
}
