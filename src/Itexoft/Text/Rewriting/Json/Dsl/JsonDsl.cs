// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Extensions;
using Itexoft.Text.Rewriting.Internal.Runtime;

namespace Itexoft.Text.Rewriting.Json.Dsl;

/// <summary>
/// Fluent DSL used to declare JSON rewrite rules.
/// </summary>
public sealed class JsonDsl<THandlers> where THandlers : class
{
    private readonly JsonRewritePlanBuilder builder;
    private readonly string? currentGroup;

    internal JsonDsl(JsonRewritePlanBuilder builder, HandlerScope<THandlers> scope, string? currentGroup = null)
    {
        this.builder = builder;
        this.Scope = scope;
        this.currentGroup = currentGroup;

        if (this.builder.HandlerResolver is null)
            this.builder.HandlerResolver = () => this.Scope.Current;
    }

    internal HandlerScope<THandlers> Scope { get; }

    /// <summary>
    /// Replaces the value at the specified JSON pointer.
    /// </summary>
    public JsonDsl<THandlers> ReplaceValue(string pointer, string replacement, string? name = null)
    {
        this.builder.ReplaceValue(pointer, replacement, name, this.currentGroup);

        return this;
    }

    /// <summary>
    /// Renames a property at the specified JSON pointer.
    /// </summary>
    public JsonDsl<THandlers> RenameProperty(string pointer, string newName, string? name = null)
    {
        this.builder.RenameProperty(pointer, newName, name, this.currentGroup);

        return this;
    }

    /// <summary>
    /// Requires that a value at the given pointer satisfies the predicate.
    /// </summary>
    public JsonDsl<THandlers> Require(
        string pointer,
        Func<THandlers, string, bool>? predicate = null,
        string? errorMessage = null,
        string? name = null)
    {
        Func<string, bool>? predicateWrapped = null;

        if (predicate is not null)
            predicateWrapped = value => predicate(this.Scope.Current ?? throw new InvalidOperationException("Handler scope is not set."), value);

        this.builder.Require(pointer, predicateWrapped, errorMessage, name, this.currentGroup);

        return this;
    }

    /// <summary>
    /// Requires that a value at the given pointer satisfies an async predicate.
    /// </summary>
    public JsonDsl<THandlers> RequireAsync(
        string pointer,
        Func<THandlers, string, ValueTask<bool>> predicateAsync,
        string? errorMessage = null,
        string? name = null)
    {
        predicateAsync.Required();

        this.builder.RequireAsync(
            pointer,
            value => predicateAsync(this.Scope.Current ?? throw new InvalidOperationException("Handler scope is not set."), value),
            errorMessage,
            name,
            this.currentGroup);

        return this;
    }

    /// <summary>
    /// Captures a value at the specified pointer using a synchronous callback.
    /// </summary>
    public JsonDsl<THandlers> Capture(string pointer, Action<THandlers, string> onValue, string? name = null)
    {
        onValue.Required();

        this.builder.Capture(
            pointer,
            value => onValue(this.Scope.Current ?? throw new InvalidOperationException("Handler scope is not set."), value),
            name,
            this.currentGroup);

        return this;
    }

    /// <summary>
    /// Captures a value at the specified pointer using an asynchronous callback.
    /// </summary>
    public JsonDsl<THandlers> CaptureAsync(string pointer, Func<THandlers, string, ValueTask> onValueAsync, string? name = null)
    {
        onValueAsync.Required();

        this.builder.CaptureAsync(
            pointer,
            value => onValueAsync(this.Scope.Current ?? throw new InvalidOperationException("Handler scope is not set."), value),
            name,
            this.currentGroup);

        return this;
    }

    public JsonDsl<THandlers> CaptureObject<T>(
        string rootPointer,
        JsonProjectionPlan<T> projection,
        Action<THandlers, T> onObject,
        string? name = null) where T : class
    {
        onObject.Required();

        this.builder.CaptureObject<THandlers, T>(rootPointer, projection, onObject, name, this.currentGroup);

        return this;
    }

    public JsonDsl<THandlers> CaptureObject<T>(
        string rootPointer,
        Action<THandlers, T> onObject,
        JsonProjectionOptions? options = null,
        string? name = null) where T : class, new()
    {
        onObject.Required();

        this.builder.CaptureObject<THandlers, T>(rootPointer, onObject, options, name, this.currentGroup);

        return this;
    }

    public JsonDsl<THandlers> CaptureMany<T>(string rootPointer, JsonProjectionPlan<T> projection, Action<THandlers, T> onItem, string? name = null)
        where T : class
    {
        onItem.Required();

        this.builder.CaptureMany<THandlers, T>(rootPointer, projection, (handler, model) => onItem(handler, model), name, this.currentGroup);

        return this;
    }

    public JsonDsl<THandlers> CaptureMany<T>(
        string rootPointer,
        Action<THandlers, T> onItem,
        JsonProjectionOptions? options = null,
        string? name = null) where T : class, new()
    {
        onItem.Required();

        this.builder.CaptureMany<THandlers, T>(rootPointer, onItem, options, name, this.currentGroup);

        return this;
    }

    public JsonDsl<THandlers> CaptureValue<T>(string pointer, Action<THandlers, T> onValue, Func<string, T>? converter = null, string? name = null)
    {
        onValue.Required();

        this.builder.CaptureValue<THandlers, T>(pointer, onValue, converter, name, this.currentGroup);

        return this;
    }

    /// <summary>
    /// Rewrites string values located at the specified pointer.
    /// </summary>
    public JsonDsl<THandlers> ReplaceInString(string pointer, Func<THandlers, string, string> replacer, string? name = null)
    {
        replacer.Required();

        this.builder.ReplaceInString(
            pointer,
            value => replacer(this.Scope.Current ?? throw new InvalidOperationException("Handler scope is not set."), value),
            name,
            this.currentGroup);

        return this;
    }

    /// <summary>
    /// Rewrites string values located at the specified pointer using an async replacer.
    /// </summary>
    public JsonDsl<THandlers> ReplaceInString(
        string pointer,
        Func<THandlers, JsonStringContext, ValueTask<string>> replacerAsync,
        string? name = null)
    {
        replacerAsync.Required();

        this.builder.ReplaceInString(
            pointer,
            ctx => replacerAsync(this.Scope.Current ?? throw new InvalidOperationException("Handler scope is not set."), ctx),
            name,
            this.currentGroup);

        return this;
    }

    /// <summary>
    /// Rewrites string values chosen by a predicate that inspects pointer and value.
    /// </summary>
    public JsonDsl<THandlers> ReplaceInString(
        Func<THandlers, JsonStringContext, bool> predicate,
        Func<THandlers, JsonStringContext, string> replacer,
        string? name = null)
    {
        predicate.Required();
        replacer.Required();

        this.builder.ReplaceInString(
            ctx => predicate(this.Scope.Current ?? throw new InvalidOperationException("Handler scope is not set."), ctx),
            ctx => replacer(this.Scope.Current ?? throw new InvalidOperationException("Handler scope is not set."), ctx),
            name,
            this.currentGroup);

        return this;
    }

    public JsonDsl<THandlers> ReplaceInString(
        Func<THandlers, JsonStringContext, ValueTask<bool>> predicate,
        Func<THandlers, JsonStringContext, ValueTask<string>> replacer,
        string? name = null)
    {
        predicate.Required();
        replacer.Required();

        this.builder.ReplaceInString(
            ctx => predicate(this.Scope.Current ?? throw new InvalidOperationException("Handler scope is not set."), ctx),
            ctx => replacer(this.Scope.Current ?? throw new InvalidOperationException("Handler scope is not set."), ctx),
            name,
            this.currentGroup);

        return this;
    }

    /// <summary>
    /// Rewrites string values located at the specified pointer gated by a predicate that can see pointer and value.
    /// </summary>
    public JsonDsl<THandlers> ReplaceInString(
        string pointer,
        Func<THandlers, JsonStringContext, bool> predicate,
        Func<THandlers, JsonStringContext, string> replacer,
        string? name = null)
    {
        predicate.Required();
        replacer.Required();

        this.builder.ReplaceInString(
            pointer,
            ctx => predicate(this.Scope.Current ?? throw new InvalidOperationException("Handler scope is not set."), ctx),
            ctx => replacer(this.Scope.Current ?? throw new InvalidOperationException("Handler scope is not set."), ctx),
            name,
            this.currentGroup);

        return this;
    }

    public JsonDsl<THandlers> ReplaceInString(
        string pointer,
        Func<THandlers, JsonStringContext, ValueTask<bool>> predicate,
        Func<THandlers, JsonStringContext, ValueTask<string>> replacer,
        string? name = null)
    {
        predicate.Required();
        replacer.Required();

        this.builder.ReplaceInString(
            pointer,
            ctx => predicate(this.Scope.Current ?? throw new InvalidOperationException("Handler scope is not set."), ctx),
            ctx => replacer(this.Scope.Current ?? throw new InvalidOperationException("Handler scope is not set."), ctx),
            name,
            this.currentGroup);

        return this;
    }

    /// <summary>
    /// Groups nested rules under a named feature flag.
    /// </summary>
    public void Group(string group, Action<JsonDsl<THandlers>> configure)
    {
        configure.Required();

        configure(new(this.builder, this.Scope, group));
    }
}
