// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Extensions;
using Itexoft.Text.Rewriting.Json.Dsl;
using Itexoft.Text.Rewriting.Json.Internal.Rules;
using Itexoft.Text.Rewriting.Primitives;

namespace Itexoft.Text.Rewriting.Json;

/// <summary>
/// Fluent builder for JSON rewrite rules.
/// </summary>
public sealed class JsonRewritePlanBuilder : RewritePlanBuilder<JsonRewritePlanBuilder, JsonRewritePlan>
{
    private readonly List<string?> ruleGroups = [];
    private readonly List<string?> ruleKinds = [];
    private readonly List<string?> ruleNames = [];
    private readonly List<JsonRewriteRule> rules = [];
    private readonly List<string?> ruleTargets = [];
    internal Func<object?>? HandlerResolver { get; set; }

    /// <summary>
    /// Replaces the value at the specified JSON Pointer with a fixed string.
    /// </summary>
    /// <param name="jsonPointer">JSON Pointer path (e.g. /user/name).</param>
    /// <param name="replacement">Value to inject.</param>
    /// <param name="name">Optional rule name.</param>
    /// <param name="group">Optional rule group.</param>
    public JsonRewritePlanBuilder ReplaceValue(string jsonPointer, string replacement, string? name = null, string? group = null)
    {
        jsonPointer.Required();

        this.AddRule(new ReplaceValueRule(jsonPointer, replacement), "ReplaceValue", jsonPointer, name, group);

        return this;
    }

    /// <summary>
    /// Renames a property at the given JSON Pointer.
    /// </summary>
    /// <param name="jsonPointer">JSON Pointer to the property.</param>
    /// <param name="newName">New property name.</param>
    /// <param name="name">Optional rule name.</param>
    /// <param name="group">Optional rule group.</param>
    public JsonRewritePlanBuilder RenameProperty(string jsonPointer, string newName, string? name = null, string? group = null)
    {
        jsonPointer.Required();

        this.AddRule(new RenamePropertyRule(jsonPointer, newName), "RenameProperty", jsonPointer, name, group);

        return this;
    }

    /// <summary>
    /// Requires that a node at the specified JSON Pointer exists and optionally satisfies a predicate.
    /// </summary>
    /// <param name="jsonPointer">JSON Pointer to validate.</param>
    /// <param name="predicate">Optional predicate that receives the node text.</param>
    /// <param name="errorMessage">Custom error message when validation fails.</param>
    /// <param name="name">Optional rule name.</param>
    /// <param name="group">Optional rule group.</param>
    public JsonRewritePlanBuilder Require(
        string jsonPointer,
        Func<string, bool>? predicate = null,
        string? errorMessage = null,
        string? name = null,
        string? group = null)
    {
        jsonPointer.Required();

        this.AddRule(new RequireRule(jsonPointer, predicate, errorMessage), "Require", jsonPointer, name, group);

        return this;
    }

    /// <summary>
    /// Requires that a node at the specified JSON Pointer exists and optionally satisfies an async predicate.
    /// </summary>
    public JsonRewritePlanBuilder RequireAsync(
        string jsonPointer,
        Func<string, ValueTask<bool>> predicateAsync,
        string? errorMessage = null,
        string? name = null,
        string? group = null)
    {
        jsonPointer.Required();
        predicateAsync.Required();

        this.AddRule(new RequireRule(jsonPointer, predicateAsync, errorMessage), "Require", jsonPointer, name, group);

        return this;
    }

    /// <summary>
    /// Captures the text of the node at the specified JSON Pointer.
    /// </summary>
    /// <param name="jsonPointer">JSON Pointer to capture.</param>
    /// <param name="onValue">Callback receiving the node text.</param>
    /// <param name="name">Optional rule name.</param>
    /// <param name="group">Optional rule group.</param>
    public JsonRewritePlanBuilder Capture(string jsonPointer, Action<string> onValue, string? name = null, string? group = null)
    {
        jsonPointer.Required();
        onValue.Required();

        this.AddRule(new CaptureRule(jsonPointer, onValue), "Capture", jsonPointer, name, group);

        return this;
    }

    /// <summary>
    /// Captures the text of the node at the specified JSON Pointer using an async callback.
    /// </summary>
    /// <param name="jsonPointer">JSON Pointer to capture.</param>
    /// <param name="onValueAsync">Async callback receiving the node text.</param>
    /// <param name="name">Optional rule name.</param>
    /// <param name="group">Optional rule group.</param>
    public JsonRewritePlanBuilder CaptureAsync(string jsonPointer, Func<string, ValueTask> onValueAsync, string? name = null, string? group = null)
    {
        jsonPointer.Required();
        onValueAsync.Required();

        this.AddRule(new CaptureAsyncRule(jsonPointer, onValueAsync), "CaptureAsync", jsonPointer, name, group);

        return this;
    }

    public JsonRewritePlanBuilder CaptureObject<T>(
        string rootPointer,
        JsonProjectionPlan<T> projection,
        Action<T> onObject,
        string? name = null,
        string? group = null) where T : class
    {
        rootPointer.Required();
        projection.Required();
        onObject.Required();

        this.AddRule(new CaptureObjectRule<T>(rootPointer, projection, onObject), "CaptureObject", rootPointer, name, group);

        return this;
    }

    public JsonRewritePlanBuilder CaptureObject<T>(
        string rootPointer,
        Action<T> onObject,
        JsonProjectionOptions? options = null,
        string? name = null,
        string? group = null) where T : class, new()
    {
        rootPointer.Required();
        onObject.Required();

        var projection = JsonProjectionPlan<T>.FromConventions(options);

        return this.CaptureObject(rootPointer, projection, onObject, name, group);
    }

    public JsonRewritePlanBuilder CaptureObject<THandlers, T>(
        string rootPointer,
        JsonProjectionPlan<T> projection,
        Action<THandlers, T> onObject,
        string? name = null,
        string? group = null) where THandlers : class where T : class
    {
        onObject.Required();

        var handlerResolver = this.HandlerResolver ?? throw new InvalidOperationException("Handler resolver is not configured.");

        return this.CaptureObject(
            rootPointer,
            projection,
            model =>
            {
                var handler = handlerResolver() ?? throw new InvalidOperationException("Handler scope is not set.");
                onObject((THandlers)handler, model);
            },
            name,
            group);
    }

    public JsonRewritePlanBuilder CaptureObject<THandlers, T>(
        string rootPointer,
        Action<THandlers, T> onObject,
        JsonProjectionOptions? options = null,
        string? name = null,
        string? group = null) where THandlers : class where T : class, new()
    {
        onObject.Required();

        var projection = JsonProjectionPlan<T>.FromConventions(options);

        return this.CaptureObject(rootPointer, projection, onObject, name, group);
    }

    public JsonRewritePlanBuilder CaptureMany<T>(
        string rootPointer,
        JsonProjectionPlan<T> projection,
        Action<T> onItem,
        string? name = null,
        string? group = null) where T : class
    {
        rootPointer.Required();
        projection.Required();
        onItem.Required();

        this.AddRule(new CaptureManyRule<T>(rootPointer, projection, onItem), "CaptureMany", rootPointer, name, group);

        return this;
    }

    public JsonRewritePlanBuilder CaptureMany<T>(
        string rootPointer,
        Action<T> onItem,
        JsonProjectionOptions? options = null,
        string? name = null,
        string? group = null) where T : class, new()
    {
        rootPointer.Required();
        onItem.Required();

        var projection = JsonProjectionPlan<T>.FromConventions(options);

        return this.CaptureMany(rootPointer, projection, onItem, name, group);
    }

    public JsonRewritePlanBuilder CaptureMany<THandlers, T>(
        string rootPointer,
        JsonProjectionPlan<T> projection,
        Action<THandlers, T> onItem,
        string? name = null,
        string? group = null) where THandlers : class where T : class
    {
        onItem.Required();

        var handlerResolver = this.HandlerResolver ?? throw new InvalidOperationException("Handler resolver is not configured.");

        return this.CaptureMany(
            rootPointer,
            projection,
            model =>
            {
                var handler = handlerResolver() ?? throw new InvalidOperationException("Handler scope is not set.");
                onItem((THandlers)handler, model);
            },
            name,
            group);
    }

    public JsonRewritePlanBuilder CaptureMany<THandlers, T>(
        string rootPointer,
        Action<THandlers, T> onItem,
        JsonProjectionOptions? options = null,
        string? name = null,
        string? group = null) where THandlers : class where T : class, new()
    {
        onItem.Required();

        var projection = JsonProjectionPlan<T>.FromConventions(options);

        return this.CaptureMany(rootPointer, projection, onItem, name, group);
    }

    public JsonRewritePlanBuilder CaptureValue<T>(
        string pointer,
        Action<T> onValue,
        Func<string, T>? converter = null,
        string? name = null,
        string? group = null)
    {
        pointer.Required();
        onValue.Required();

        var converterFunc = converter ?? JsonScalarConverterRegistry.Resolve<T>();

        if (converterFunc is null)
            throw new InvalidOperationException($"No converter registered for {typeof(T).FullName}.");

        this.AddRule(new CaptureRule(pointer, literal => onValue(converterFunc(DecodeScalarLiteral(literal)))), "CaptureValue", pointer, name, group);

        return this;
    }

    public JsonRewritePlanBuilder CaptureValue<THandlers, T>(
        string pointer,
        Action<THandlers, T> onValue,
        Func<string, T>? converter = null,
        string? name = null,
        string? group = null) where THandlers : class
    {
        onValue.Required();

        var converterFunc = converter ?? JsonScalarConverterRegistry.Resolve<T>();

        if (converterFunc is null)
            throw new InvalidOperationException($"No converter registered for {typeof(T).FullName}.");

        var handlerResolver = this.HandlerResolver ?? throw new InvalidOperationException("Handler resolver is not configured.");

        this.AddRule(
            new CaptureRule(
                pointer,
                literal =>
                {
                    var handler = handlerResolver() ?? throw new InvalidOperationException("Handler scope is not set.");
                    onValue((THandlers)handler, converterFunc(DecodeScalarLiteral(literal)));
                }),
            "CaptureValue",
            pointer,
            name,
            group);

        return this;
    }

    /// <summary>
    /// Replaces string values that satisfy a predicate.
    /// </summary>
    /// <param name="predicate">Predicate deciding whether to replace.</param>
    /// <param name="replacer">Replacement factory.</param>
    /// <param name="name">Optional rule name.</param>
    /// <param name="group">Optional rule group.</param>
    public JsonRewritePlanBuilder ReplaceInString(
        Func<JsonStringContext, bool> predicate,
        Func<JsonStringContext, string> replacer,
        string? name = null,
        string? group = null)
    {
        predicate.Required();
        replacer.Required();

        this.AddRule(new ReplaceInStringRule(predicate, replacer), "ReplaceInString", null, name, group);

        return this;
    }

    /// <summary>
    /// Replaces string values located at the specified JSON Pointer.
    /// </summary>
    /// <param name="jsonPointer">JSON Pointer to match.</param>
    /// <param name="replacer">Replacement factory.</param>
    /// <param name="name">Optional rule name.</param>
    /// <param name="group">Optional rule group.</param>
    public JsonRewritePlanBuilder ReplaceInString(string jsonPointer, Func<string, string> replacer, string? name = null, string? group = null)
    {
        jsonPointer.Required();
        replacer.Required();

        this.AddRule(new ReplaceInStringRule(jsonPointer, replacer), "ReplaceInString", jsonPointer, name, group);

        return this;
    }

    /// <summary>
    /// Replaces string values located at the specified JSON Pointer asynchronously.
    /// </summary>
    public JsonRewritePlanBuilder ReplaceInString(
        string jsonPointer,
        Func<JsonStringContext, ValueTask<string>> replacerAsync,
        string? name = null,
        string? group = null)
    {
        jsonPointer.Required();
        replacerAsync.Required();

        this.AddRule(new ReplaceInStringRule(jsonPointer, replacerAsync), "ReplaceInString", jsonPointer, name, group);

        return this;
    }

    /// <summary>
    /// Replaces string values that satisfy a predicate based only on value.
    /// </summary>
    /// <param name="predicate">Predicate deciding whether to replace.</param>
    /// <param name="replacer">Replacement factory.</param>
    /// <param name="name">Optional rule name.</param>
    /// <param name="group">Optional rule group.</param>
    public JsonRewritePlanBuilder ReplaceInString(
        Func<string, bool> predicate,
        Func<string, string> replacer,
        string? name = null,
        string? group = null)
    {
        predicate.Required();
        replacer.Required();

        this.AddRule(new ReplaceInStringRule(ctx => predicate(ctx.Value), ctx => replacer(ctx.Value)), "ReplaceInString", null, name, group);

        return this;
    }

    public JsonRewritePlanBuilder ReplaceInString(
        Func<JsonStringContext, ValueTask<bool>> predicateAsync,
        Func<JsonStringContext, ValueTask<string>> replacerAsync,
        string? name = null,
        string? group = null)
    {
        predicateAsync.Required();
        replacerAsync.Required();

        this.AddRule(new ReplaceInStringRule(predicateAsync, replacerAsync), "ReplaceInString", null, name, group);

        return this;
    }

    public JsonRewritePlanBuilder ReplaceInString(
        string jsonPointer,
        Func<JsonStringContext, ValueTask<bool>> predicateAsync,
        Func<JsonStringContext, ValueTask<string>> replacerAsync,
        string? name = null,
        string? group = null)
    {
        jsonPointer.Required();
        predicateAsync.Required();
        replacerAsync.Required();

        this.AddRule(new ReplaceInStringRule(jsonPointer, predicateAsync, replacerAsync), "ReplaceInString", jsonPointer, name, group);

        return this;
    }

    /// <summary>
    /// Replaces string values located at the specified JSON Pointer using an additional predicate.
    /// </summary>
    /// <param name="jsonPointer">JSON Pointer to match.</param>
    /// <param name="predicate">Predicate that receives pointer and value.</param>
    /// <param name="replacer">Replacement factory.</param>
    /// <param name="name">Optional rule name.</param>
    /// <param name="group">Optional rule group.</param>
    public JsonRewritePlanBuilder ReplaceInString(
        string jsonPointer,
        Func<JsonStringContext, bool> predicate,
        Func<JsonStringContext, string> replacer,
        string? name = null,
        string? group = null)
    {
        jsonPointer.Required();
        predicate.Required();
        replacer.Required();

        this.AddRule(new ReplaceInStringRule(jsonPointer, predicate, replacer), "ReplaceInString", jsonPointer, name, group);

        return this;
    }

    /// <summary>
    /// Builds an immutable plan from the configured rules.
    /// </summary>
    public override JsonRewritePlan Build()
    {
        var rulesArray = this.rules.ToArray();
        var names = this.ruleNames.ToArray();
        var groups = this.ruleGroups.ToArray();
        var kinds = this.ruleKinds.ToArray();
        var targets = this.ruleTargets.ToArray();

        return new(rulesArray, names, groups, kinds, targets);
    }

    private void AddRule(JsonRewriteRule rule, string kind, string? target, string? name, string? group)
    {
        rule.Required();
        kind.Required();

        var ruleId = this.rules.Count;
        rule.AssignMetadata(ruleId, group);

        this.rules.Add(rule);
        this.ruleNames.Add(name);
        this.ruleGroups.Add(group);
        this.ruleKinds.Add(kind);
        this.ruleTargets.Add(target);
    }

    private static string DecodeScalarLiteral(string literal)
    {
        var span = literal.AsSpan().Trim();

        if (span.IsEmpty)
            throw new FormatException("Value is empty.");

        if (span[0] == '"')
        {
            if (JsonStringReader.TryReadString(span, out var value))
                return value;

            throw new FormatException("Invalid JSON string literal.");
        }

        if (span[0] == '{' || span[0] == '[')
            throw new FormatException("Expected scalar JSON value.");

        return span.ToString();
    }
}
