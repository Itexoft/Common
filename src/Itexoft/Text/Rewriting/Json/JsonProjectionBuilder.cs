// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Linq.Expressions;
using System.Reflection;
using Itexoft.Extensions;

namespace Itexoft.Text.Rewriting.Json;

public sealed class JsonProjectionBuilder<T>(JsonProjectionOptions? options = null) where T : class
{
    private readonly List<JsonProjectionBinding<T>> bindings = [];
    private readonly JsonProjectionOptions options = options ?? new JsonProjectionOptions();
    private readonly HashSet<string> pointers = new(StringComparer.Ordinal);

    public JsonProjectionBuilder<T> Map<TValue>(
        Expression<Func<T, TValue>> target,
        Expression<Func<T, TValue>> source,
        Func<string, TValue>? converter = null)
    {
        source.Required();

        var pointer = this.BuildPointer(source.Body);

        return this.Map(target, pointer, converter);
    }

    public JsonProjectionBuilder<T> Map<TValue>(Expression<Func<T, TValue>> property, string pointer, Func<string, TValue>? converter = null)
    {
        property.Required();
        pointer.Required();

        var member = ResolveMember(property.Body);
        var setter = CompileUntypedSetter(member);
        var targetPointer = NormalizePointer(pointer);
        var converterFunc = converter != null ? new(s => converter(s)!) : JsonScalarConverterRegistry.Resolve(typeof(TValue));

        if (converterFunc is null)
            throw new InvalidOperationException($"No converter registered for {typeof(TValue).FullName}.");

        this.AddBinding(
            targetPointer,
            new(
                targetPointer,
                (target, value) =>
                {
                    var converted = converterFunc(value.Text);
                    setter(target, converted);
                }));

        return this;
    }

    public JsonProjectionBuilder<T> MapObject<TChild>(Expression<Func<T, TChild?>> property, JsonProjectionPlan<TChild> childPlan)
        where TChild : class
    {
        property.Required();
        childPlan.Required();

        var member = ResolveMember(property.Body);
        var setter = CompileUntypedSetter(member);
        var getter = CompileUntypedGetter(member);
        var propertyPointer = CombinePointer("/", "/" + JsonPropertyNameHelper.Convert(member.Name, this.options.PropertyNameStyle));

        foreach (var binding in childPlan.BindingArray)
        {
            var pointer = NormalizePointer(CombinePointer(propertyPointer, binding.Pointer));

            this.AddBinding(
                pointer,
                new(
                    pointer,
                    (target, value) =>
                    {
                        if (getter(target) is not TChild child)
                        {
                            child = childPlan.CreateInstance();
                            setter(target, child);
                        }

                        binding.Apply(child, value);
                    }));
        }

        return this;
    }

    internal JsonProjectionBuilder<T> MapObjectAt<TChild>(
        Expression<Func<T, TChild?>> property,
        JsonProjectionPlan<TChild> childPlan,
        string propertyPointer) where TChild : class
    {
        property.Required();
        childPlan.Required();
        propertyPointer.Required();

        var member = ResolveMember(property.Body);
        var setter = CompileUntypedSetter(member);
        var getter = CompileUntypedGetter(member);
        var pointerBase = NormalizePointer(propertyPointer);

        foreach (var binding in childPlan.BindingArray)
        {
            var pointer = NormalizePointer(CombinePointer(pointerBase, binding.Pointer));

            this.AddBinding(
                pointer,
                new(
                    pointer,
                    (target, value) =>
                    {
                        if (getter(target) is not TChild child)
                        {
                            child = childPlan.CreateInstance();
                            setter(target, child);
                        }

                        binding.Apply(child, value);
                    }));
        }

        return this;
    }

    public JsonProjectionPlan<T> Build() =>
        new(
            this.bindings.ToArray(),
            () => Activator.CreateInstance<T>() ?? throw new InvalidOperationException($"Cannot create instance of {typeof(T).FullName}."));

    internal JsonProjectionBuilder<T> MapMember(MemberInfo member, string pointer, Func<string, object> converter)
    {
        member.Required();
        pointer.Required();
        converter.Required();

        var targetPointer = NormalizePointer(pointer);
        var setter = CompileUntypedSetter(member);

        this.AddBinding(
            targetPointer,
            new(
                targetPointer,
                (target, value) =>
                {
                    var converted = converter(value.Text);
                    setter(target, converted);
                }));

        return this;
    }

    internal void MapFromConventions(HashSet<Type> visited)
    {
        visited.Required();
        this.MapType(typeof(T), "/", visited);
    }

    internal void MapFromConventions() => this.MapFromConventions([]);

    private void AddBinding(string pointer, JsonProjectionBinding<T> binding)
    {
        if (!this.pointers.Add(pointer))
            throw new InvalidOperationException($"Duplicate projection pointer: {pointer}.");

        this.bindings.Add(binding);
    }

    private void MapType(Type type, string basePointer, HashSet<Type> visited)
    {
        if (!visited.Add(type))
            throw new InvalidOperationException($"Projection cycle detected for type {type.FullName}.");

        var members = JsonProjectionTypeInspector.GetProjectionMembers(type);

        foreach (var member in members)
        {
            var memberType = JsonProjectionTypeInspector.GetMemberType(member);
            var pointer = CombinePointer(basePointer, JsonProjectionTypeInspector.GetDefaultPointer(member, this.options));

            if (JsonProjectionTypeInspector.IsScalar(memberType))
            {
                var setter = CompileUntypedSetter(member);

                var converter = JsonScalarConverterRegistry.Resolve(memberType)
                                ?? throw new InvalidOperationException($"No converter registered for {memberType.FullName}.");

                this.AddBinding(
                    pointer,
                    new(
                        pointer,
                        (target, value) =>
                        {
                            var converted = converter(value.Text);
                            setter(target, converted);
                        }));
            }
            else if (JsonProjectionTypeInspector.IsObject(memberType))
            {
                var method = typeof(JsonProjectionBuilder<T>).GetMethod("AddObjectBindings", BindingFlags.Instance | BindingFlags.NonPublic)
                             ?? throw new InvalidOperationException("AddObjectBindings method not found.");

                method.MakeGenericMethod(memberType).Invoke(this, [member, pointer, visited]);
            }
            else
            {
                throw new InvalidOperationException(
                    $"Unsupported projection member type {memberType.FullName} on {member.DeclaringType?.FullName}.{member.Name}.");
            }
        }

        visited.Remove(type);
    }

    private void AddObjectBindings<TChild>(MemberInfo member, string pointerBase, HashSet<Type> visited) where TChild : class
    {
        member.Required();
        pointerBase.Required();
        visited.Required();

        var setter = CompileUntypedSetter(member);
        var getter = CompileUntypedGetter(member);
        var builder = new JsonProjectionBuilder<TChild>(this.options);
        builder.MapFromConventions(visited);
        var childPlan = builder.Build();

        foreach (var binding in childPlan.BindingArray)
        {
            var combinedPointer = NormalizePointer(CombinePointer(pointerBase, binding.Pointer));

            this.AddBinding(
                combinedPointer,
                new(
                    combinedPointer,
                    (target, value) =>
                    {
                        if (getter(target) is not TChild child)
                        {
                            child = childPlan.CreateInstance();
                            setter(target, child);
                        }

                        binding.Apply(child, value);
                    }));
        }
    }

    private static string NormalizePointer(string pointer)
    {
        if (string.IsNullOrWhiteSpace(pointer))
            throw new ArgumentException("Pointer is required.", nameof(pointer));

        if (!pointer.StartsWith("/", StringComparison.Ordinal))
            throw new ArgumentException("Pointer must start with '/'.", nameof(pointer));

        return pointer;
    }

    private static string CombinePointer(string left, string right)
    {
        if (string.IsNullOrEmpty(left))
            left = "/";

        if (string.IsNullOrEmpty(right))
            right = "/";

        if (!left.StartsWith("/", StringComparison.Ordinal))
            left = "/" + left;

        if (!right.StartsWith("/", StringComparison.Ordinal))
            right = "/" + right;

        if (string.Equals(right, "/", StringComparison.Ordinal))
            return left;

        if (string.Equals(left, "/", StringComparison.Ordinal))
            return right;

        if (left.EndsWith("/", StringComparison.Ordinal))
            left = left.TrimEnd('/');

        return left + right;
    }

    private static MemberInfo ResolveMember(Expression body)
    {
        if (body is UnaryExpression unary && unary.NodeType == ExpressionType.Convert)
            body = unary.Operand;

        if (body is MemberExpression member && member.Member.MemberType is MemberTypes.Property or MemberTypes.Field)
            return member.Member;

        throw new InvalidOperationException("Projection target must be a property or field access.");
    }

    private static Action<T, object?> CompileUntypedSetter(MemberExpression memberExpr) =>
        CompileUntypedSetter(memberExpr.Member);

    private static Func<T, object?> CompileUntypedGetter(MemberExpression memberExpr) =>
        CompileUntypedGetter(memberExpr.Member);

    private static Action<T, object?> CompileUntypedSetter(MemberInfo member)
    {
        var target = Expression.Parameter(typeof(T), "target");
        var value = Expression.Parameter(typeof(object), "value");

        Expression access = member switch
        {
            PropertyInfo property when property.CanWrite => Expression.Property(target, property),
            FieldInfo field => Expression.Field(target, field),
            _ => throw new InvalidOperationException("Projection target must be writable."),
        };

        var convertedValue = Expression.Convert(value, access.Type);
        var assign = Expression.Assign(access, convertedValue);
        var lambda = Expression.Lambda<Action<T, object?>>(assign, target, value);

        return lambda.Compile();
    }

    private static Func<T, object?> CompileUntypedGetter(MemberInfo member)
    {
        var target = Expression.Parameter(typeof(T), "target");

        Expression access = member switch
        {
            PropertyInfo property when property.CanRead => Expression.Property(target, property),
            FieldInfo field => Expression.Field(target, field),
            _ => throw new InvalidOperationException("Projection target must be readable."),
        };

        var convert = Expression.Convert(access, typeof(object));
        var lambda = Expression.Lambda<Func<T, object?>>(convert, target);

        return lambda.Compile();
    }

    private string BuildPointer(Expression expression)
    {
        var segments = new Stack<string>();
        expression = UnwrapConvert(expression);

        while (true)
        {
            if (expression is MemberExpression member && member.Member.MemberType is MemberTypes.Property or MemberTypes.Field)
            {
                segments.Push(JsonPropertyNameHelper.Convert(member.Member.Name, this.options.PropertyNameStyle));

                expression = UnwrapConvert(
                    member.Expression ?? throw new InvalidOperationException("Projection source must be a chain of property or field accesses."));

                continue;
            }

            if (expression is ParameterExpression)
                break;

            throw new InvalidOperationException("Projection source must be a chain of property or field accesses.");
        }

        if (segments.Count == 0)
            throw new InvalidOperationException("Projection source must be a chain of property or field accesses.");

        return "/" + string.Join("/", segments);
    }

    private static Expression UnwrapConvert(Expression expression)
    {
        while (expression is UnaryExpression unary && unary.NodeType == ExpressionType.Convert)
            expression = unary.Operand;

        return expression;
    }
}
