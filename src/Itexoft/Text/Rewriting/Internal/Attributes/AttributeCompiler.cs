// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using Itexoft.Reflection;
using Itexoft.Text.Rewriting.Internal.Runtime;
using Itexoft.Text.Rewriting.Json;
using Itexoft.Text.Rewriting.Json.Attributes;
using Itexoft.Text.Rewriting.Json.Dsl;
using Itexoft.Text.Rewriting.Primitives;
using Itexoft.Text.Rewriting.Text.Attributes;
using Itexoft.Text.Rewriting.Text.Dsl;

namespace Itexoft.Text.Rewriting.Internal.Attributes;

internal static class AttributeCompiler<THandlers>
    where THandlers : class
{
    private static readonly ConcurrentDictionary<Type, object> JsonProjectionCache = new();
    private static readonly JsonProjectionOptions DefaultProjectionOptions = new();

    public static void ApplyText(Type type, TextDsl<THandlers> dsl)
    {
        ArgumentNullException.ThrowIfNull(type);

        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        foreach (var method in methods)
        {
            foreach (var literal in method.GetCustomAttributes<TextLiteralRuleAttribute>())
                ApplyTextRule(dsl, method, literal);

            foreach (var regex in method.GetCustomAttributes<TextRegexRuleAttribute>())
                ApplyTextRule(dsl, method, regex);

            foreach (var tail in method.GetCustomAttributes<TextTailRuleAttribute>())
                ApplyTailRule(dsl, method, tail);
        }
    }

    public static void ApplyJson(Type type, JsonDsl<THandlers> dsl)
    {
        ArgumentNullException.ThrowIfNull(type);

        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        foreach (var method in methods)
        {
            foreach (var attr in method.GetCustomAttributes<JsonReplaceValueAttribute>())
                dsl.ReplaceValue(attr.Pointer, attr.Replacement, attr.Name);

            foreach (var attr in method.GetCustomAttributes<JsonRenamePropertyAttribute>())
                dsl.RenameProperty(attr.Pointer, attr.NewName, attr.Name);

            foreach (var attr in method.GetCustomAttributes<JsonRequireAttribute>())
            {
                var predicate = BuildRequirePredicate(method, dsl.Scope);
                var predicateAsync = BuildRequirePredicateAsync(method, dsl.Scope);
                if (predicateAsync is not null)
                    dsl.RequireAsync(attr.Pointer, predicateAsync, attr.ErrorMessage, attr.Name);
                else
                    dsl.Require(attr.Pointer, predicate, attr.ErrorMessage, attr.Name);
            }

            foreach (var attr in method.GetCustomAttributes<JsonCaptureAttribute>())
            {
                var capture = BuildCapture(method, dsl.Scope);
                var captureAsync = BuildCaptureAsync(method, dsl.Scope);
                if (captureAsync is not null)
                    dsl.CaptureAsync(attr.Pointer, captureAsync, attr.Name);
                else if (capture is not null)
                    dsl.Capture(attr.Pointer, capture, attr.Name);
            }

            foreach (var attr in method.GetCustomAttributes<JsonReplaceInStringAttribute>())
            {
                var replacer = BuildStringReplacer(method, dsl.Scope, attr.Pointer);
                if (attr.Pointer is not null)
                {
                    if (replacer.pointerReplacer is not null)
                        dsl.ReplaceInString(attr.Pointer, replacer.pointerReplacer, attr.Name);
                    else if (replacer.pointerReplacerAsync is not null)
                        dsl.ReplaceInString(
                            attr.Pointer,
                            (h, ctx) => replacer.pointerReplacerAsync(h, ctx.Value),
                            attr.Name);
                }
                else
                {
                    if (replacer.contextReplacer is not null && replacer.contextPredicate is not null)
                    {
                        dsl.ReplaceInString(replacer.contextPredicate, replacer.contextReplacer, attr.Name);
                    }
                    else if (replacer.contextReplacerAsync is not null)
                    {
                        Func<THandlers, JsonStringContext, ValueTask<bool>> predicateAsync =
                            replacer.contextPredicateAsync
                            ?? (replacer.contextPredicate is not null
                                ? (h, ctx) => new ValueTask<bool>(replacer.contextPredicate(h, ctx))
                                : (_, _) => new ValueTask<bool>(true));
                        dsl.ReplaceInString(predicateAsync, replacer.contextReplacerAsync, attr.Name);
                    }
                }
            }

            foreach (var attr in method.GetCustomAttributes<JsonCaptureObjectAttribute>())
                ApplyJsonCaptureObjectRule(dsl, method, attr);
        }
    }

    private static void ApplyTextRule(TextDsl<THandlers> dsl, MethodInfo method, TextLiteralRuleAttribute attribute)
    {
        var builder = dsl.Literal(attribute.Pattern, attribute.Comparison, attribute.Name).Priority(attribute.Priority);
        ApplyAction(builder, method, attribute.Action, attribute.Replacement);
    }

    private static void ApplyTextRule(TextDsl<THandlers> dsl, MethodInfo method, TextRegexRuleAttribute attribute)
    {
        var builder = dsl.Regex(attribute.Pattern, attribute.MaxMatchLength, attribute.Options, attribute.Name)
            .Priority(attribute.Priority);
        ApplyAction(builder, method, attribute.Action, attribute.Replacement);
    }

    private static void ApplyTailRule(TextDsl<THandlers> dsl, MethodInfo method, TextTailRuleAttribute attribute)
    {
        var decision = BuildTailDecisionFactory(method, dsl.Scope);
        var matcher =
            decision is not null
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
        }

        if (action == MatchAction.Remove)
        {
            var onMatch = BuildMatchHandler(method);
            var onMatchAsync = BuildMatchHandlerAsync(method);
            if (onMatchAsync is not null)
                builder.Remove(onMatchAsync);
            else
                builder.Remove(onMatch);

            return;
        }

        {
            var onMatch = BuildMatchHandler(method);
            var onMatchAsync = BuildMatchHandlerAsync(method);
            if (onMatchAsync is not null)
                builder.Hook(onMatchAsync);
            else
                builder.Hook(onMatch ?? ((_, _, _) => { }));
        }
    }

    private static Func<THandlers, int, ReadOnlySpan<char>, string?>? BuildReplacementFactory(MethodInfo method)
    {
        if (method.ReturnType != typeof(string))
            return null;

        if (DelegateCompiler.TryCreate(method, out Func<THandlers, int, ReadOnlySpan<char>, string?>? factoryWithId))
            return factoryWithId;

        if (DelegateCompiler.TryCreate(method, out Func<THandlers, ReadOnlySpan<char>, string?>? factory))
            return (h, _, span) => factory(h, span);

        return null;
    }

    private static Func<THandlers, int, ReadOnlyMemory<char>, ValueTask<string?>>? BuildReplacementFactoryAsync(MethodInfo method)
    {
        if (method.ReturnType != typeof(ValueTask<string?>) && method.ReturnType != typeof(ValueTask<string>))
            return null;

        if (DelegateCompiler.TryCreate(method, out Func<THandlers, int, ReadOnlyMemory<char>, ValueTask<string?>>? factoryWithId))
            return factoryWithId;

        if (DelegateCompiler.TryCreate(method, out Func<THandlers, ReadOnlyMemory<char>, ValueTask<string?>>? factory))
            return (h, _, memory) => factory(h, memory);

        return null;
    }

    private static Action<THandlers, int, ReadOnlySpan<char>>? BuildMatchHandler(MethodInfo method)
    {
        if (method.ReturnType != typeof(void))
            return null;

        if (DelegateCompiler.TryCreate(method, out Action<THandlers, int, ReadOnlySpan<char>>? handlerWithId))
            return handlerWithId;

        if (DelegateCompiler.TryCreate(method, out Action<THandlers, ReadOnlySpan<char>>? handler))
            return (h, _, span) => handler(h, span);

        return null;
    }

    private static Func<THandlers, int, ReadOnlyMemory<char>, ValueTask>? BuildMatchHandlerAsync(MethodInfo method)
    {
        if (method.ReturnType != typeof(ValueTask))
            return null;

        if (DelegateCompiler.TryCreate(method, out Func<THandlers, int, ReadOnlyMemory<char>, ValueTask>? handlerWithId))
            return handlerWithId;

        if (DelegateCompiler.TryCreate(method, out Func<THandlers, ReadOnlyMemory<char>, ValueTask>? handler))
            return (h, _, memory) => handler(h, memory);

        return null;
    }

    private static TailMatcher BuildTailMatcher(MethodInfo method, HandlerScope<THandlers> scope)
    {
        if (DelegateCompiler.TryCreate(method, out Func<THandlers, ReadOnlySpan<char>, int>? matcher))
            return span => matcher(scope.Current ?? throw new InvalidOperationException("Handler scope is not set."), span);

        if (DelegateCompiler.TryCreate(method, out TailMatcher? matcherNoHandler))
            return matcherNoHandler;

        throw new InvalidOperationException("Tail matcher must return int.");
    }

    private static Func<THandlers, ReadOnlySpan<char>, TextTailDecision>? BuildTailDecisionFactory(
        MethodInfo method,
        HandlerScope<THandlers> scope)
    {
        if (method.ReturnType != typeof(TextTailDecision))
            return null;

        if (DelegateCompiler.TryCreate(method, out Func<THandlers, ReadOnlySpan<char>, TextTailDecision>? factory))
            return (h, span) => factory(scope.Current ?? throw new InvalidOperationException("Handler scope is not set."), span);

        return null;
    }

    private static Func<THandlers, string, bool>? BuildRequirePredicate(MethodInfo method, HandlerScope<THandlers> scope)
    {
        if (method.ReturnType != typeof(bool))
            return null;

        if (DelegateCompiler.TryCreate(method, out Func<THandlers, string, bool>? predicate))
            return (h, value) => predicate(scope.Current ?? throw new InvalidOperationException("Handler scope is not set."), value);

        return null;
    }

    private static Func<THandlers, string, ValueTask<bool>>? BuildRequirePredicateAsync(MethodInfo method, HandlerScope<THandlers> scope)
    {
        if (method.ReturnType != typeof(ValueTask<bool>))
            return null;

        if (DelegateCompiler.TryCreate(method, out Func<THandlers, string, ValueTask<bool>>? predicate))
            return (h, value) => predicate(scope.Current ?? throw new InvalidOperationException("Handler scope is not set."), value);

        return null;
    }

    private static Action<THandlers, string>? BuildCapture(MethodInfo method, HandlerScope<THandlers> scope)
    {
        if (method.ReturnType != typeof(void))
            return null;

        if (DelegateCompiler.TryCreate(method, out Action<THandlers, string>? capture))
            return (h, value) => capture(scope.Current ?? throw new InvalidOperationException("Handler scope is not set."), value);

        return null;
    }

    private static Func<THandlers, string, ValueTask>? BuildCaptureAsync(MethodInfo method, HandlerScope<THandlers> scope)
    {
        if (method.ReturnType != typeof(ValueTask))
            return null;

        if (DelegateCompiler.TryCreate(method, out Func<THandlers, string, ValueTask>? capture))
            return (h, value) => capture(scope.Current ?? throw new InvalidOperationException("Handler scope is not set."), value);

        return null;
    }

    private static (
        Func<THandlers, string, string>? pointerReplacer,
        Func<THandlers, string, ValueTask<string>>? pointerReplacerAsync,
        Func<THandlers, JsonStringContext, bool>? contextPredicate,
        Func<THandlers, JsonStringContext, ValueTask<bool>>? contextPredicateAsync,
        Func<THandlers, JsonStringContext, string>? contextReplacer,
        Func<THandlers, JsonStringContext, ValueTask<string>>? contextReplacerAsync)
        BuildStringReplacer(MethodInfo method, HandlerScope<THandlers> scope, string? pointer)
    {
        if (method.ReturnType != typeof(string) && method.ReturnType != typeof(ValueTask<string>))
            return (null, null, null, null, null, null);

        if (pointer is not null)
        {
            if (DelegateCompiler.TryCreate(method, out Func<THandlers, string, string>? replacer))
                return (replacer, null, null, null, null, null);

            if (DelegateCompiler.TryCreate(method, out Func<THandlers, string, string, string>? replacerWithPointer))
                return ((h, value) => replacerWithPointer(h, pointer, value), null, null, null, null, null);

            if (DelegateCompiler.TryCreate(method, out Func<string, string>? replacerNoHandler))
                return ((_, value) => replacerNoHandler(value), null, null, null, null, null);

            if (DelegateCompiler.TryCreate(method, out Func<string, string, string>? replacerNoHandlerWithPointer))
                return ((_, value) => replacerNoHandlerWithPointer(pointer, value), null, null, null, null, null);

            if (DelegateCompiler.TryCreate(method, out Func<THandlers, string, ValueTask<string>>? replacerAsync))
                return (null, replacerAsync, null, null, null, null);

            if (DelegateCompiler.TryCreate(method, out Func<THandlers, string, string, ValueTask<string>>? replacerAsyncWithPointer))
                return (null, (h, value) => replacerAsyncWithPointer(h, pointer, value), null, null, null, null);

            if (DelegateCompiler.TryCreate(method, out Func<string, ValueTask<string>>? replacerNoHandlerAsync))
                return (null, (_, value) => replacerNoHandlerAsync(value), null, null, null, null);

            if (DelegateCompiler.TryCreate(method, out Func<string, string, ValueTask<string>>? replacerNoHandlerWithPointerAsync))
                return (null, (_, value) => replacerNoHandlerWithPointerAsync(pointer, value), null, null, null, null);
        }
        else
        {
            if (DelegateCompiler.TryCreate(method, out Func<THandlers, JsonStringContext, string>? replacerContext))
                return (null, null, (_, _) => true, null, replacerContext, null);

            if (DelegateCompiler.TryCreate(method, out Func<JsonStringContext, string>? replacerContextNoHandler))
                return (null, null, (_, _) => true, null, (_, ctx) => replacerContextNoHandler(ctx), null);

            if (DelegateCompiler.TryCreate(method, out Func<THandlers, JsonStringContext, ValueTask<string>>? replacerContextAsync))
                return (null, null, null, (_, _) => new(true), null, replacerContextAsync);

            if (DelegateCompiler.TryCreate(method, out Func<JsonStringContext, ValueTask<string>>? replacerContextNoHandlerAsync))
                return (null, null, null, (_, _) => new(true), null, (_, ctx) => replacerContextNoHandlerAsync(ctx));
        }

        return (null, null, null, null, null, null);
    }

    private static void ApplyJsonCaptureObjectRule(JsonDsl<THandlers> dsl, MethodInfo method, JsonCaptureObjectAttribute attribute)
    {
        var parameters = method.GetParameters();

        if (!method.IsStatic || parameters.Length != 2)
            throw new InvalidOperationException(
                $"Method {method.DeclaringType?.FullName}.{method.Name} must be static with signature (THandlers, {attribute.ModelType.Name}) returning void or ValueTask.");

        if (parameters[0].ParameterType != typeof(THandlers))
            throw new InvalidOperationException(
                $"Method {method.DeclaringType?.FullName}.{method.Name} must have first parameter of type {typeof(THandlers).FullName}.");

        if (parameters[1].ParameterType != attribute.ModelType)
            throw new InvalidOperationException(
                $"Method {method.DeclaringType?.FullName}.{method.Name} must have second parameter of type {attribute.ModelType.FullName}.");

        if (!attribute.ModelType.IsClass)
            throw new InvalidOperationException($"Model type {attribute.ModelType.FullName} must be a class.");

        var handler = BuildCaptureObjectHandler(method, attribute.ModelType);
        var projection = GetProjectionPlan(attribute.ModelType, new());

        InvokeCaptureObject(dsl, attribute.RootPointer, projection, handler, attribute.ModelType);
    }

    private static Delegate BuildCaptureObjectHandler(MethodInfo method, Type modelType)
    {
        if (method.ReturnType == typeof(void))
        {
            var delegateType = typeof(Action<,>).MakeGenericType(typeof(THandlers), modelType);

            return method.CreateDelegate(delegateType);
        }

        if (method.ReturnType == typeof(ValueTask))
        {
            var delegateType = typeof(Func<,,>).MakeGenericType(typeof(THandlers), modelType, typeof(ValueTask));
            var asyncDelegate = method.CreateDelegate(delegateType);

            return WrapAsyncCapture(asyncDelegate, modelType);
        }

        throw new InvalidOperationException($"Method {method.DeclaringType?.FullName}.{method.Name} must return void or ValueTask.");
    }

    private static Delegate WrapAsyncCapture(Delegate asyncDelegate, Type modelType)
    {
        var handlerParam = Expression.Parameter(typeof(THandlers), "h");
        var modelParam = Expression.Parameter(modelType, "m");
        var invokeAsync = Expression.Invoke(Expression.Constant(asyncDelegate), handlerParam, modelParam);
        var awaiterCall = Expression.Call(invokeAsync, typeof(ValueTask).GetMethod(nameof(ValueTask.GetAwaiter), Type.EmptyTypes)!);
        var getResult = Expression.Call(
            awaiterCall,
            awaiterCall.Type.GetMethod(nameof(TaskAwaiter.GetResult)) ?? awaiterCall.Type.GetMethod("GetResult")!);
        var lambda = Expression.Lambda(
            typeof(Action<,>).MakeGenericType(typeof(THandlers), modelType),
            getResult,
            handlerParam,
            modelParam);

        return lambda.Compile();
    }

    private static void InvokeCaptureObject(
        JsonDsl<THandlers> dsl,
        string rootPointer,
        object projection,
        Delegate onObject,
        Type modelType)
    {
        var method = typeof(JsonDsl<THandlers>).GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .First(m =>
            {
                if (m.Name != "CaptureObject")
                    return false;

                var args = m.GetParameters();

                if (m.GetGenericArguments().Length != 1 || args.Length < 3)
                    return false;

                return args[1].ParameterType.IsGenericType
                       && args[1].ParameterType.GetGenericTypeDefinition() == typeof(JsonProjectionPlan<>);
            });
        var generic = method.MakeGenericMethod(modelType);
        generic.Invoke(dsl, new[] { rootPointer, projection, onObject, null });
    }

    private static object GetProjectionPlan(Type modelType, HashSet<Type> visited)
    {
        if (JsonProjectionCache.TryGetValue(modelType, out var cached))
            return cached;

        var plan = BuildProjectionPlan(modelType, DefaultProjectionOptions, visited);
        JsonProjectionCache[modelType] = plan;

        return plan;
    }

    private static object BuildProjectionPlan(Type modelType, JsonProjectionOptions options, HashSet<Type> visited)
    {
        if (!visited.Add(modelType))
            throw new InvalidOperationException($"Projection cycle detected for type {modelType.FullName}.");

        var builderType = typeof(JsonProjectionBuilder<>).MakeGenericType(modelType);
        var builder = Activator.CreateInstance(builderType, options)
                      ?? throw new InvalidOperationException($"Cannot create projection builder for {modelType.FullName}.");
        var mapMember = builderType.GetMethod("MapMember", BindingFlags.Instance | BindingFlags.NonPublic)
                        ?? throw new InvalidOperationException("MapMember method not found on projection builder.");
        var mapObject = builderType.GetMethod("MapObject", BindingFlags.Instance | BindingFlags.Public);
        var mapObjectAt = builderType.GetMethod("MapObjectAt", BindingFlags.Instance | BindingFlags.NonPublic);
        foreach (var member in JsonProjectionTypeInspector.GetProjectionMembers(modelType))
        {
            var memberType = JsonProjectionTypeInspector.GetMemberType(member);
            var pointerAttribute = member.GetCustomAttribute<JsonPointerAttribute>();
            var pointer = pointerAttribute?.Path ?? JsonProjectionTypeInspector.GetDefaultPointer(member, options);

            if (JsonProjectionTypeInspector.IsScalar(memberType))
            {
                var converter = pointerAttribute?.Converter is null
                    ? JsonScalarConverterRegistry.Resolve(memberType)
                    : CreateConverterFromAttribute(pointerAttribute.Converter, memberType);

                if (converter is null)
                    throw new InvalidOperationException($"No converter registered for {memberType.FullName}.");

                mapMember.Invoke(builder, new object[] { member, pointer, converter });
            }
            else if (JsonProjectionTypeInspector.IsObject(memberType))
            {
                var childPlan = GetProjectionPlan(memberType, visited);
                var propertyLambda = CreatePropertyLambda(modelType, member, memberType);
                if (pointerAttribute?.Path is not null)
                    mapObjectAt?.MakeGenericMethod(memberType).Invoke(builder, new[] { propertyLambda, childPlan, pointer });
                else
                    mapObject?.MakeGenericMethod(memberType).Invoke(builder, new[] { propertyLambda, childPlan });
            }
            else
            {
                throw new InvalidOperationException(
                    $"Unsupported projection member type {memberType.FullName} on {member.DeclaringType?.FullName}.{member.Name}.");
            }
        }

        visited.Remove(modelType);

        var build = builderType.GetMethod("Build", BindingFlags.Instance | BindingFlags.Public)
                    ?? throw new InvalidOperationException("Build method not found on projection builder.");

        return build.Invoke(builder, null)!;
    }

    private static Func<string, object> CreateConverterFromAttribute(Type converterType, Type targetType)
    {
        var iface = converterType
            .GetInterfaces()
            .FirstOrDefault(i =>
                i.IsGenericType
                && i.GetGenericTypeDefinition() == typeof(IJsonScalarConverter<>)
                && i.GetGenericArguments()[0] == targetType);

        if (iface is null)
            throw new InvalidOperationException(
                $"Converter type {converterType.FullName} must implement IJsonScalarConverter<{targetType.FullName}>.");

        var instance = Activator.CreateInstance(converterType)
                       ?? throw new InvalidOperationException($"Cannot create converter of type {converterType.FullName}.");
        var method = iface.GetMethod("Convert", new[] { typeof(string) }) ?? converterType.GetMethod("Convert", new[] { typeof(string) });

        if (method is null)
            throw new InvalidOperationException("Converter must expose Convert(string) method.");

        var value = Expression.Parameter(typeof(string), "value");
        var call = Expression.Call(Expression.Convert(Expression.Constant(instance), converterType), method, value);
        var lambda = Expression.Lambda<Func<string, object>>(Expression.Convert(call, typeof(object)), value);

        return lambda.Compile();
    }

    private static LambdaExpression CreatePropertyLambda(Type declaringType, MemberInfo member, Type memberType)
    {
        var parameter = Expression.Parameter(declaringType, "x");
        Expression access = member switch
        {
            PropertyInfo property => Expression.Property(parameter, property),
            FieldInfo field => Expression.Field(parameter, field),
            _ => throw new InvalidOperationException("Projection target must be a property or field access.")
        };

        var delegateType = typeof(Func<,>).MakeGenericType(declaringType, memberType);

        return Expression.Lambda(delegateType, access, parameter);
    }
}