// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Reflection;
using Itexoft.Extensions;
using Itexoft.Reflection;
using Itexoft.Text.Rewriting.Internal.Runtime;
using Itexoft.Text.Rewriting.Primitives;
using Itexoft.Text.Rewriting.Text.Attributes;
using Itexoft.Text.Rewriting.Text.Dsl;

namespace Itexoft.Text.Rewriting.Internal.Attributes;

internal static partial class AttributeCompiler<THandlers> where THandlers : class
{
    public static void ApplyText(Type type, TextDsl<THandlers> dsl)
    {
        type.Required();

        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);

        foreach (var method in methods)
        {
            foreach (var literal in method.GetCustomAttributes<TextLiteralRuleAttribute>())
            {
                EnsureStatic(method, nameof(TextLiteralRuleAttribute));
                ApplyTextRule(dsl, method, literal);
            }

            foreach (var regex in method.GetCustomAttributes<TextRegexRuleAttribute>())
            {
                EnsureStatic(method, nameof(TextRegexRuleAttribute));
                ApplyTextRule(dsl, method, regex);
            }

            foreach (var tail in method.GetCustomAttributes<TextTailRuleAttribute>())
            {
                EnsureStatic(method, nameof(TextTailRuleAttribute));
                ApplyTailRule(dsl, method, tail);
            }
        }
    }

    private static void ApplyTextRule(TextDsl<THandlers> dsl, MethodInfo method, TextLiteralRuleAttribute attribute)
    {
        var builder = dsl.Literal(attribute.Pattern, attribute.Comparison, attribute.Name).Priority(attribute.Priority);
        ApplyAction(builder, method, attribute.Action, attribute.Replacement);
    }

    private static void ApplyTextRule(TextDsl<THandlers> dsl, MethodInfo method, TextRegexRuleAttribute attribute)
    {
        var builder = dsl.Regex(attribute.Pattern, attribute.MaxMatchLength, attribute.Options, attribute.Name).Priority(attribute.Priority);
        ApplyAction(builder, method, attribute.Action, attribute.Replacement);
    }

    private static void ApplyTailRule(TextDsl<THandlers> dsl, MethodInfo method, TextTailRuleAttribute attribute)
    {
        var decision = BuildTailDecisionFactory(method, dsl.Scope);

        var matcher = decision is not null
            ? span => decision(dsl.Scope.Current ?? throw new InvalidOperationException("Handler scope is not set."), span).MatchLength
            : BuildTailMatcher(method, dsl.Scope);

        var builder = dsl.Tail(attribute.MaxMatchLength, matcher, attribute.Name).Priority(attribute.Priority);

        if (decision is null)
        {
            builder.Hook((_, _, _) => { });

            return;
        }

        builder.Replace((handler, id, span, metrics) =>
        {
            var result = decision(handler, span);

            if (result.MatchLength <= 0)
                return null;

            if (result.Action == MatchAction.Remove)
                return string.Empty;

            return result.Replacement;
        });
    }

    private static void ApplyAction(TextRuleBuilder<THandlers> builder, MethodInfo method, MatchAction action, string? replacement)
    {
        var onMatch = BuildMatchHandler(method);
        var onMatchAsync = BuildMatchHandlerAsync(method);

        if (action == MatchAction.Replace)
        {
            if (!string.IsNullOrEmpty(replacement))
            {
                builder.Replace(replacement);

                return;
            }

            var replacementFactory = BuildReplacementFactory(method);

            if (replacementFactory is not null)
            {
                builder.Replace(replacementFactory);

                return;
            }

            var replacementFactoryAsync = BuildReplacementFactoryAsync(method);

            if (replacementFactoryAsync is not null)
            {
                builder.Replace(replacementFactoryAsync);

                return;
            }

            throw new InvalidOperationException(
                $"Method {method.DeclaringType?.FullName}.{method.Name} must return string or ValueTask<string> when Action=Replace.");
        }

        if (action == MatchAction.Remove)
        {
            if (onMatchAsync is not null)
                builder.Remove(onMatchAsync);
            else if (onMatch is not null)
                builder.Remove(onMatch);
            else
            {
                throw new InvalidOperationException(
                    $"Method {method.DeclaringType?.FullName}.{method.Name} must return void or ValueTask when Action=Remove.");
            }

            return;
        }

        if (onMatchAsync is not null)
            builder.Hook(onMatchAsync);
        else if (onMatch is not null)
            builder.Hook(onMatch);
        else
        {
            throw new InvalidOperationException(
                $"Method {method.DeclaringType?.FullName}.{method.Name} must return void or ValueTask when Action=None.");
        }
    }

    private static Func<THandlers, int, ReadOnlySpan<char>, string?>? BuildReplacementFactory(MethodInfo method)
    {
        if (method.ReturnType != typeof(string))
            return null;

        if (DelegateCompiler.TryCreate(method, out Func<THandlers, int, ReadOnlySpan<char>, string?>? factoryWithId) && factoryWithId is not null)
            return factoryWithId;

        if (DelegateCompiler.TryCreate(method, out Func<THandlers, ReadOnlySpan<char>, string?>? factory) && factory is not null)
            return (h, _, span) => factory(h, span);

        return null;
    }

    private static Func<THandlers, int, ReadOnlyMemory<char>, ValueTask<string?>>? BuildReplacementFactoryAsync(MethodInfo method)
    {
        if (method.ReturnType != typeof(ValueTask<string?>) && method.ReturnType != typeof(ValueTask<string>))
            return null;

        if (DelegateCompiler.TryCreate(method, out Func<THandlers, int, ReadOnlyMemory<char>, ValueTask<string?>>? factoryWithId)
            && factoryWithId is not null)
            return factoryWithId;

        if (DelegateCompiler.TryCreate(method, out Func<THandlers, ReadOnlyMemory<char>, ValueTask<string?>>? factory) && factory is not null)
            return (h, _, memory) => factory(h, memory);

        return null;
    }

    private static Action<THandlers, int, ReadOnlySpan<char>>? BuildMatchHandler(MethodInfo method)
    {
        if (method.ReturnType != typeof(void))
            return null;

        if (DelegateCompiler.TryCreate(method, out Action<THandlers, int, ReadOnlySpan<char>>? handlerWithId) && handlerWithId is not null)
            return handlerWithId;

        if (DelegateCompiler.TryCreate(method, out Action<THandlers, ReadOnlySpan<char>>? handler) && handler is not null)
            return (h, _, span) => handler(h, span);

        return null;
    }

    private static Func<THandlers, int, ReadOnlyMemory<char>, ValueTask>? BuildMatchHandlerAsync(MethodInfo method)
    {
        if (method.ReturnType != typeof(ValueTask))
            return null;

        if (DelegateCompiler.TryCreate(method, out Func<THandlers, int, ReadOnlyMemory<char>, ValueTask>? handlerWithId) && handlerWithId is not null)
            return handlerWithId;

        if (DelegateCompiler.TryCreate(method, out Func<THandlers, ReadOnlyMemory<char>, ValueTask>? handler) && handler is not null)
            return (h, _, memory) => handler(h, memory);

        return null;
    }

    private static TailMatcher BuildTailMatcher(MethodInfo method, HandlerScope<THandlers> scope)
    {
        if (DelegateCompiler.TryCreate(method, out Func<THandlers, ReadOnlySpan<char>, int>? matcher) && matcher is not null)
            return span => matcher(scope.Current ?? throw new InvalidOperationException("Handler scope is not set."), span);

        if (DelegateCompiler.TryCreate(method, out TailMatcher? matcherNoHandler) && matcherNoHandler is not null)
            return matcherNoHandler;

        throw new InvalidOperationException("Tail matcher must return int.");
    }

    private static Func<THandlers, ReadOnlySpan<char>, TextTailDecision>? BuildTailDecisionFactory(MethodInfo method, HandlerScope<THandlers> scope)
    {
        if (method.ReturnType != typeof(TextTailDecision))
            return null;

        if (DelegateCompiler.TryCreate(method, out Func<THandlers, ReadOnlySpan<char>, TextTailDecision>? factory) && factory is not null)
            return (h, span) => factory(scope.Current ?? throw new InvalidOperationException("Handler scope is not set."), span);

        return null;
    }

    private static void EnsureStatic(MethodInfo method, string attributeName)
    {
        if (method.IsStatic)
            return;

        throw new InvalidOperationException($"Method {method.DeclaringType?.FullName}.{method.Name} with [{attributeName}] must be static.");
    }
}
