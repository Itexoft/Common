// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using Itexoft.Extensions;
using Itexoft.Reflection;
using Itexoft.Text.Rewriting.Internal.Runtime;
using Itexoft.Text.Rewriting.Json;
using Itexoft.Text.Rewriting.Json.Attributes;
using Itexoft.Text.Rewriting.Json.Dsl;

namespace Itexoft.Text.Rewriting.Internal.Attributes;

internal static partial class AttributeCompiler<THandlers> where THandlers : class
{
    private static readonly ConcurrentDictionary<Type, object> jsonProjectionCache = new();
    private static readonly JsonProjectionOptions defaultProjectionOptions = new();

    public static void ApplyJson(Type type, JsonDsl<THandlers> dsl)
    {
        type.Required();

        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);

        foreach (var method in methods)
        {
            foreach (var attr in method.GetCustomAttributes<JsonReplaceValueAttribute>())
            {
                EnsureStatic(method, nameof(JsonReplaceValueAttribute));
                dsl.ReplaceValue(attr.Pointer, attr.Replacement, attr.Name);
            }

            foreach (var attr in method.GetCustomAttributes<JsonRenamePropertyAttribute>())
            {
                EnsureStatic(method, nameof(JsonRenamePropertyAttribute));
                dsl.RenameProperty(attr.Pointer, attr.NewName, attr.Name);
            }

            foreach (var attr in method.GetCustomAttributes<JsonRequireAttribute>())
            {
                EnsureStatic(method, nameof(JsonRequireAttribute));

                var predicate = BuildRequirePredicate(method, dsl.Scope);
                var predicateAsync = BuildRequirePredicateAsync(method, dsl.Scope);

                if (predicateAsync is not null)
                    dsl.RequireAsync(attr.Pointer, predicateAsync, attr.ErrorMessage, attr.Name);
                else if (predicate is not null)
                    dsl.Require(attr.Pointer, predicate, attr.ErrorMessage, attr.Name);
                else
                {
                    throw new InvalidOperationException(
                        $"Method {method.DeclaringType?.FullName}.{method.Name} must return bool or ValueTask<bool> for JsonRequire.");
                }
            }

            foreach (var attr in method.GetCustomAttributes<JsonCaptureAttribute>())
            {
                EnsureStatic(method, nameof(JsonCaptureAttribute));

                var capture = BuildCapture(method, dsl.Scope);
                var captureAsync = BuildCaptureAsync(method, dsl.Scope);

                if (captureAsync is not null)
                    dsl.CaptureAsync(attr.Pointer, captureAsync, attr.Name);
                else if (capture is not null)
                    dsl.Capture(attr.Pointer, capture, attr.Name);
                else
                {
                    throw new InvalidOperationException(
                        $"Method {method.DeclaringType?.FullName}.{method.Name} must return void or ValueTask for JsonCapture.");
                }
            }

            foreach (var attr in method.GetCustomAttributes<JsonReplaceInStringAttribute>())
            {
                EnsureStatic(method, nameof(JsonReplaceInStringAttribute));

                var replacer = BuildStringReplacer(method, dsl.Scope, attr.Pointer);

                if (attr.Pointer is not null)
                {
                    if (replacer.pointerReplacer is not null)
                        dsl.ReplaceInString(attr.Pointer, replacer.pointerReplacer, attr.Name);
                    else if (replacer.pointerReplacerAsync is not null)
                        dsl.ReplaceInString(attr.Pointer, (h, ctx) => replacer.pointerReplacerAsync(h, ctx.Value), attr.Name);
                }
                else
                {
                    if (replacer.contextReplacer is not null && replacer.contextPredicate is not null)
                        dsl.ReplaceInString(replacer.contextPredicate, replacer.contextReplacer, attr.Name);
                    else if (replacer.contextReplacerAsync is not null)
                    {
                        var predicateAsync = replacer.contextPredicateAsync
                                             ?? (replacer.contextPredicate is not null
                                                 ? new Func<THandlers, JsonStringContext, ValueTask<bool>>((h, ctx) => new(
                                                     replacer.contextPredicate(h, ctx)))
                                                 : (_, _) => new(true));

                        dsl.ReplaceInString(predicateAsync, replacer.contextReplacerAsync, attr.Name);
                    }
                }

                if (replacer.pointerReplacer is null
                    && replacer.pointerReplacerAsync is null
                    && replacer.contextReplacer is null
                    && replacer.contextReplacerAsync is null)
                {
                    throw new InvalidOperationException(
                        $"Method {method.DeclaringType?.FullName}.{method.Name} must return string or ValueTask<string> for JsonReplaceInString.");
                }
            }

            foreach (var attr in method.GetCustomAttributes<JsonCaptureObjectAttribute>())
                ApplyJsonCaptureObjectRule(dsl, method, attr);
        }
    }

    private static Func<THandlers, string, bool>? BuildRequirePredicate(MethodInfo method, HandlerScope<THandlers> scope)
    {
        if (method.ReturnType != typeof(bool))
            return null;

        if (DelegateCompiler.TryCreate(method, out Func<THandlers, string, bool>? predicate) && predicate is not null)
            return (h, value) => predicate(scope.Current ?? throw new InvalidOperationException("Handler scope is not set."), value);

        return null;
    }

    private static Func<THandlers, string, ValueTask<bool>>? BuildRequirePredicateAsync(MethodInfo method, HandlerScope<THandlers> scope)
    {
        if (method.ReturnType != typeof(ValueTask<bool>))
            return null;

        if (DelegateCompiler.TryCreate(method, out Func<THandlers, string, ValueTask<bool>>? predicate) && predicate is not null)
            return (h, value) => predicate(scope.Current ?? throw new InvalidOperationException("Handler scope is not set."), value);

        return null;
    }

    private static Action<THandlers, string>? BuildCapture(MethodInfo method, HandlerScope<THandlers> scope)
    {
        if (method.ReturnType != typeof(void))
            return null;

        if (DelegateCompiler.TryCreate(method, out Action<THandlers, string>? capture) && capture is not null)
            return (h, value) => capture(scope.Current ?? throw new InvalidOperationException("Handler scope is not set."), value);

        return null;
    }

    private static Func<THandlers, string, ValueTask>? BuildCaptureAsync(MethodInfo method, HandlerScope<THandlers> scope)
    {
        if (method.ReturnType != typeof(ValueTask))
            return null;

        if (DelegateCompiler.TryCreate(method, out Func<THandlers, string, ValueTask>? capture) && capture is not null)
            return (h, value) => capture(scope.Current ?? throw new InvalidOperationException("Handler scope is not set."), value);

        return null;
    }

    private static ( Func<THandlers, string, string>? pointerReplacer, Func<THandlers, string, ValueTask<string>>? pointerReplacerAsync,
        Func<THandlers, JsonStringContext, bool>? contextPredicate, Func<THandlers, JsonStringContext, ValueTask<bool>>? contextPredicateAsync,
        Func<THandlers, JsonStringContext, string>? contextReplacer, Func<THandlers, JsonStringContext, ValueTask<string>>? contextReplacerAsync)
        BuildStringReplacer(MethodInfo method, HandlerScope<THandlers> scope, string? pointer)
    {
        if (method.ReturnType != typeof(string) && method.ReturnType != typeof(ValueTask<string>))
            return (null, null, null, null, null, null);

        if (pointer is not null)
        {
            if (DelegateCompiler.TryCreate(method, out Func<THandlers, string, string>? replacer) && replacer is not null)
                return (replacer, null, null, null, null, null);

            if (DelegateCompiler.TryCreate(method, out Func<THandlers, string, string, string>? replacerWithPointer)
                && replacerWithPointer is not null)
                return ((h, value) => replacerWithPointer(h, pointer, value), null, null, null, null, null);

            if (DelegateCompiler.TryCreate(method, out Func<string, string>? replacerNoHandler) && replacerNoHandler is not null)
                return ((_, value) => replacerNoHandler(value), null, null, null, null, null);

            if (DelegateCompiler.TryCreate(method, out Func<string, string, string>? replacerNoHandlerWithPointer)
                && replacerNoHandlerWithPointer is not null)
                return ((_, value) => replacerNoHandlerWithPointer(pointer, value), null, null, null, null, null);

            if (DelegateCompiler.TryCreate(method, out Func<THandlers, string, ValueTask<string>>? replacerAsync) && replacerAsync is not null)
                return (null, replacerAsync, null, null, null, null);

            if (DelegateCompiler.TryCreate(method, out Func<THandlers, string, string, ValueTask<string>>? replacerAsyncWithPointer)
                && replacerAsyncWithPointer is not null)
                return (null, (h, value) => replacerAsyncWithPointer(h, pointer, value), null, null, null, null);

            if (DelegateCompiler.TryCreate(method, out Func<string, ValueTask<string>>? replacerNoHandlerAsync) && replacerNoHandlerAsync is not null)
                return (null, (_, value) => replacerNoHandlerAsync(value), null, null, null, null);

            if (DelegateCompiler.TryCreate(method, out Func<string, string, ValueTask<string>>? replacerNoHandlerWithPointerAsync)
                && replacerNoHandlerWithPointerAsync is not null)
                return (null, (_, value) => replacerNoHandlerWithPointerAsync(pointer, value), null, null, null, null);
        }
        else
        {
            if (DelegateCompiler.TryCreate(method, out Func<THandlers, JsonStringContext, string>? replacerContext) && replacerContext is not null)
                return (null, null, (_, _) => true, null, replacerContext, null);

            if (DelegateCompiler.TryCreate(method, out Func<JsonStringContext, string>? replacerContextNoHandler)
                && replacerContextNoHandler is not null)
                return (null, null, (_, _) => true, null, (_, ctx) => replacerContextNoHandler(ctx), null);

            if (DelegateCompiler.TryCreate(method, out Func<THandlers, JsonStringContext, ValueTask<string>>? replacerContextAsync)
                && replacerContextAsync is not null)
                return (null, null, null, (_, _) => new(true), null, replacerContextAsync);

            if (DelegateCompiler.TryCreate(method, out Func<JsonStringContext, ValueTask<string>>? replacerContextNoHandlerAsync)
                && replacerContextNoHandlerAsync is not null)
                return (null, null, null, (_, _) => new(true), null, (_, ctx) => replacerContextNoHandlerAsync(ctx));
        }

        return (null, null, null, null, null, null);
    }

    private static void ApplyJsonCaptureObjectRule(JsonDsl<THandlers> dsl, MethodInfo method, JsonCaptureObjectAttribute attribute)
    {
        var parameters = method.GetParameters();

        if (!method.IsStatic || parameters.Length != 2)
        {
            throw new InvalidOperationException(
                $"Method {
                    method.DeclaringType?.FullName
                }.{
                    method.Name
                } must be static with signature (THandlers, {
                    attribute.ModelType.Name
                }) returning void or ValueTask.");
        }

        if (parameters[0].ParameterType != typeof(THandlers))
        {
            throw new InvalidOperationException(
                $"Method {method.DeclaringType?.FullName}.{method.Name} must have first parameter of type {typeof(THandlers).FullName}.");
        }

        if (parameters[1].ParameterType != attribute.ModelType)
        {
            throw new InvalidOperationException(
                $"Method {method.DeclaringType?.FullName}.{method.Name} must have second parameter of type {attribute.ModelType.FullName}.");
        }

        if (!attribute.ModelType.IsClass)
            throw new InvalidOperationException($"Model type {attribute.ModelType.FullName} must be a class.");

        var handler = BuildCaptureObjectHandler(method, attribute.ModelType);
        var projection = GetProjectionPlan(attribute.ModelType, []);

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

        var lambda = Expression.Lambda(typeof(Action<,>).MakeGenericType(typeof(THandlers), modelType), getResult, handlerParam, modelParam);

        return lambda.Compile();
    }

    private static void InvokeCaptureObject(JsonDsl<THandlers> dsl, string rootPointer, object projection, Delegate onObject, Type modelType)
    {
        var method = typeof(JsonDsl<THandlers>).GetMethods(BindingFlags.Instance | BindingFlags.Public).First(m =>
        {
            if (m.Name != "CaptureObject")
                return false;

            var args = m.GetParameters();

            if (m.GetGenericArguments().Length != 1 || args.Length < 3)
                return false;

            return args[1].ParameterType.IsGenericType && args[1].ParameterType.GetGenericTypeDefinition() == typeof(JsonProjectionPlan<>);
        });

        var generic = method.MakeGenericMethod(modelType);
        generic.Invoke(dsl, [rootPointer, projection, onObject, null]);
    }

    private static object GetProjectionPlan(Type modelType, HashSet<Type> visited)
    {
        if (jsonProjectionCache.TryGetValue(modelType, out var cached))
            return cached;

        var plan = BuildProjectionPlan(modelType, defaultProjectionOptions, visited);
        jsonProjectionCache[modelType] = plan;

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

                mapMember.Invoke(builder, [member, pointer, converter]);
            }
            else if (JsonProjectionTypeInspector.IsObject(memberType))
            {
                var childPlan = GetProjectionPlan(memberType, visited);
                var propertyLambda = CreatePropertyLambda(modelType, member, memberType);

                if (pointerAttribute?.Path is not null)
                    mapObjectAt?.MakeGenericMethod(memberType).Invoke(builder, [propertyLambda, childPlan, pointer]);
                else
                    mapObject?.MakeGenericMethod(memberType).Invoke(builder, [propertyLambda, childPlan]);
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
        var iface = converterType.GetInterfaces().FirstOrDefault(i => i.IsGenericType
                                                                      && i.GetGenericTypeDefinition() == typeof(IJsonScalarConverter<>)
                                                                      && i.GetGenericArguments()[0] == targetType);

        if (iface is null)
        {
            throw new InvalidOperationException(
                $"Converter type {converterType.FullName} must implement IJsonScalarConverter<{targetType.FullName}>.");
        }

        var instance = Activator.CreateInstance(converterType)
                       ?? throw new InvalidOperationException($"Cannot create converter of type {converterType.FullName}.");

        var method = iface.GetMethod("Convert", [typeof(string)]) ?? converterType.GetMethod("Convert", [typeof(string)]);

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
            _ => throw new InvalidOperationException("Projection target must be a property or field access."),
        };

        var delegateType = typeof(Func<,>).MakeGenericType(declaringType, memberType);

        return Expression.Lambda(delegateType, access, parameter);
    }
}
