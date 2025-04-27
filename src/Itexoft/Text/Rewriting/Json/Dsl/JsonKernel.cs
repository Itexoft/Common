// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Extensions;
using Itexoft.Text.Rewriting.Internal.Attributes;
using Itexoft.Text.Rewriting.Internal.Runtime;
using Itexoft.Text.Rewriting.Primitives;

namespace Itexoft.Text.Rewriting.Json.Dsl;

/// <summary>
/// Compiles JSON rewrite rules into a reusable plan and spawns sessions bound to handlers.
/// </summary>
public sealed class JsonKernel<THandlers> where THandlers : class
{
    private readonly RuleDescriptor[] descriptors;
    private readonly JsonRewritePlan plan;
    private readonly RuleInfo[] rules;
    private readonly HandlerScope<THandlers> scope;

    private JsonKernel(JsonRewritePlan plan, HandlerScope<THandlers> scope, RuleInfo[] rules, RuleDescriptor[] descriptors)
    {
        this.plan = plan;
        this.scope = scope;
        this.rules = rules;
        this.descriptors = descriptors;
    }

    /// <summary>
    /// Compiles a plan using the fluent DSL.
    /// </summary>
    public static JsonKernel<THandlers> Compile(Action<JsonDsl<THandlers>> configure)
    {
        configure.Required();

        var scope = new HandlerScope<THandlers>();
        var builder = new JsonRewritePlanBuilder();
        var dsl = new JsonDsl<THandlers>(builder, scope);

        configure(dsl);

        var plan = builder.Build();
        var ruleInfos = BuildRuleInfos(plan.RuleCount, plan.RuleNames, plan.RuleGroups);
        var descriptors = BuildRuleDescriptors(plan, ruleInfos);

        return new(plan, scope, ruleInfos, descriptors);
    }

    /// <summary>
    /// Compiles a plan using rules discovered on the provided types.
    /// </summary>
    public static JsonKernel<THandlers> CompileFromAttributes(params Type[] ruleContainers)
    {
        ruleContainers.Required();

        var scope = new HandlerScope<THandlers>();
        var builder = new JsonRewritePlanBuilder();
        var dsl = new JsonDsl<THandlers>(builder, scope);

        foreach (var type in ruleContainers)
            AttributeCompiler<THandlers>.ApplyJson(type, dsl);

        var plan = builder.Build();
        var ruleInfos = BuildRuleInfos(plan.RuleCount, plan.RuleNames, plan.RuleGroups);
        var descriptors = BuildRuleDescriptors(plan, ruleInfos);

        return new(plan, scope, ruleInfos, descriptors);
    }

    /// <summary>
    /// Creates a stateful session bound to the provided handler instance and output.
    /// </summary>
    public JsonSession<THandlers> CreateSession(TextWriter output, THandlers handlers, JsonKernelOptions? options = null)
    {
        output.Required();
        handlers.Required();

        return new(this.plan, this.scope, this.rules, output, handlers, options);
    }

    public IReadOnlyList<RuleDescriptor> Describe() => this.descriptors;

    private static RuleInfo[] BuildRuleInfos(int ruleCount, string?[] names, string?[] groups)
    {
        var result = new RuleInfo[ruleCount];

        for (var i = 0; i < ruleCount; i++)
        {
            var name = i < names.Length ? names[i] : null;
            var group = i < groups.Length ? groups[i] : null;
            result[i] = new(i, name, group);
        }

        return result;
    }

    private static RuleDescriptor[] BuildRuleDescriptors(JsonRewritePlan plan, RuleInfo[] ruleInfos)
    {
        var descriptors = new RuleDescriptor[plan.RuleCount];

        for (var i = 0; i < plan.RuleCount; i++)
        {
            var kind = plan.RuleKinds.Length > i ? plan.RuleKinds[i] ?? "Unknown" : "Unknown";
            var target = plan.RuleTargets.Length > i ? plan.RuleTargets[i] : null;
            var action = kind is "ReplaceValue" or "ReplaceInString" or "RenameProperty" ? MatchAction.Replace : MatchAction.None;

            descriptors[i] = new(i, ruleInfos[i].Name, ruleInfos[i].Group, "Json", kind, 0, i, action, 0, target);
        }

        return descriptors;
    }
}
