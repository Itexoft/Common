// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Reflection;
using System.Reflection.Emit;
using Itexoft.Extensions;

namespace Itexoft.Reflection;

public static class Interfaces
{
    public static TValue Overlay<TValue, THandler>(TValue input, THandler handler) where TValue : class where THandler : class =>
        InterfacesOverlay<TValue, THandler>.Wrap(input.Required(), handler.Required());

    private static class InterfacesOverlay<TValue, THandler> where TValue : class where THandler : class
    {
        private static readonly ModuleBuilder moduleBuilder = CreateModuleBuilder();
        private static readonly Lock buildLock = new();
        private static int typeId;

        public static readonly Func<TValue, THandler, TValue> Wrap;

        static InterfacesOverlay()
        {
            var valueType = typeof(TValue);
            var handlerType = typeof(THandler);

            if (!valueType.IsInterface)
                throw new InvalidOperationException($"Type {valueType} must be an interface.");

            var valueInterfaces = GetInterfaces(valueType);
            var handlerInterfaces = GetInterfaces(handlerType);

            if (!HasIntersection(valueInterfaces, handlerInterfaces))
                throw new InvalidOperationException($"{handlerType} does not implement any interface from {valueType}.");

            var proxyType = BuildProxyType(valueType, handlerType);
            Wrap = BuildConstructor(proxyType);
        }

        private static ModuleBuilder CreateModuleBuilder()
        {
            var name = new AssemblyName("Itexoft.Interfaces.Dynamic");
            var assembly = AssemblyBuilder.DefineDynamicAssembly(name, AssemblyBuilderAccess.Run);

            return assembly.DefineDynamicModule("Itexoft.Interfaces.Dynamic.Module");
        }

        private static Type BuildProxyType(Type valueType, Type handlerType)
        {
            var interfaces = GetInterfaces(valueType);
            var handlerInterfaces = new HashSet<Type>(GetInterfaces(handlerType));

            lock (buildLock)
            {
                var name = $"{valueType.Name}_{handlerType.Name}_Wrap_{Interlocked.Increment(ref typeId)}";

                var typeBuilder = moduleBuilder.DefineType(
                    name,
                    TypeAttributes.Sealed
                    | TypeAttributes.Public
                    | TypeAttributes.Class
                    | TypeAttributes.AutoClass
                    | TypeAttributes.AnsiClass
                    | TypeAttributes.BeforeFieldInit
                    | TypeAttributes.AutoLayout);

                foreach (var iface in interfaces)
                    typeBuilder.AddInterfaceImplementation(iface);

                var inputField = typeBuilder.DefineField("_input", valueType, FieldAttributes.Private | FieldAttributes.InitOnly);
                var handlerField = typeBuilder.DefineField("_handler", handlerType, FieldAttributes.Private | FieldAttributes.InitOnly);

                DefineConstructor(typeBuilder, inputField, handlerField);
                DefineMethods(typeBuilder, interfaces, handlerInterfaces, inputField, handlerField);

                return typeBuilder.CreateTypeInfo().AsType();
            }
        }

        private static void DefineConstructor(TypeBuilder typeBuilder, FieldBuilder inputField, FieldBuilder handlerField)
        {
            var ctorBuilder = typeBuilder.DefineConstructor(
                MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName | MethodAttributes.HideBySig,
                CallingConventions.Standard,
                [inputField.FieldType, handlerField.FieldType]);

            var il = ctorBuilder.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes)!);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Stfld, inputField);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Stfld, handlerField);
            il.Emit(OpCodes.Ret);
        }

        private static void DefineMethods(
            TypeBuilder typeBuilder,
            IReadOnlyList<Type> interfaces,
            HashSet<Type> handlerInterfaces,
            FieldBuilder inputField,
            FieldBuilder handlerField)
        {
            var methodBuilders = new Dictionary<MethodKey, MethodBuilder>();
            var overrides = new HashSet<MethodInfo>();

            foreach (var iface in interfaces)
            {
                foreach (var method in iface.GetMethods(BindingFlags.Instance | BindingFlags.Public))
                {
                    var key = new MethodKey(method);

                    if (!methodBuilders.TryGetValue(key, out var builder))
                    {
                        var useHandler = method.DeclaringType is not null && handlerInterfaces.Contains(method.DeclaringType);
                        builder = DefineProxyMethod(typeBuilder, method, useHandler, inputField, handlerField);
                        methodBuilders.Add(key, builder);
                    }

                    if (overrides.Add(method))
                        typeBuilder.DefineMethodOverride(builder, method);
                }
            }
        }

        private static MethodBuilder DefineProxyMethod(
            TypeBuilder typeBuilder,
            MethodInfo interfaceMethod,
            bool useHandler,
            FieldBuilder inputField,
            FieldBuilder handlerField)
        {
            var attributes = MethodAttributes.Public
                             | MethodAttributes.Virtual
                             | MethodAttributes.HideBySig
                             | MethodAttributes.Final
                             | MethodAttributes.NewSlot;

            if (interfaceMethod.IsSpecialName)
                attributes |= MethodAttributes.SpecialName;

            var methodBuilder = typeBuilder.DefineMethod(interfaceMethod.Name, attributes, CallingConventions.HasThis);
            Dictionary<Type, Type>? genericMap = null;
            GenericTypeParameterBuilder[]? methodGenerics = null;

            if (interfaceMethod.IsGenericMethodDefinition)
            {
                var genericArgs = interfaceMethod.GetGenericArguments();
                methodGenerics = methodBuilder.DefineGenericParameters(genericArgs.Select(arg => arg.Name).ToArray());
                genericMap = new(genericArgs.Length);

                for (var i = 0; i < genericArgs.Length; i++)
                    genericMap.Add(genericArgs[i], methodGenerics[i]);

                ApplyGenericConstraints(genericArgs, methodGenerics, genericMap);
            }

            var returnType = ReplaceGenericArguments(interfaceMethod.ReturnType, genericMap);
            var parameters = interfaceMethod.GetParameters();
            var parameterTypes = parameters.Select(p => ReplaceGenericArguments(p.ParameterType, genericMap)).ToArray();
            var returnRequired = MapCustomModifiers(interfaceMethod.ReturnParameter.GetRequiredCustomModifiers(), genericMap);
            var returnOptional = MapCustomModifiers(interfaceMethod.ReturnParameter.GetOptionalCustomModifiers(), genericMap);
            var parameterRequired = new Type[parameterTypes.Length][];
            var parameterOptional = new Type[parameterTypes.Length][];

            for (var i = 0; i < parameters.Length; i++)
            {
                parameterRequired[i] = MapCustomModifiers(parameters[i].GetRequiredCustomModifiers(), genericMap);
                parameterOptional[i] = MapCustomModifiers(parameters[i].GetOptionalCustomModifiers(), genericMap);
            }

            methodBuilder.SetSignature(returnType, returnRequired, returnOptional, parameterTypes, parameterRequired, parameterOptional);

            var returnParameter = interfaceMethod.ReturnParameter;
            var returnBuilder = methodBuilder.DefineParameter(0, returnParameter.Attributes, returnParameter.Name);
            CopyCustomAttributes(returnParameter.GetCustomAttributesData(), returnBuilder.SetCustomAttribute);

            CopyCustomAttributes(interfaceMethod.GetCustomAttributesData(), methodBuilder.SetCustomAttribute);

            for (var i = 0; i < parameters.Length; i++)
            {
                var parameter = parameters[i];
                var builder = methodBuilder.DefineParameter(i + 1, parameter.Attributes, parameter.Name);

                if (parameter.HasDefaultValue)
                    builder.SetConstant(parameter.DefaultValue);

                CopyCustomAttributes(parameter.GetCustomAttributesData(), builder.SetCustomAttribute);
            }

            var il = methodBuilder.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, useHandler ? handlerField : inputField);

            for (var i = 0; i < parameterTypes.Length; i++)
                il.Emit(OpCodes.Ldarg, i + 1);

            var targetMethod = interfaceMethod;

            if (methodGenerics is not null)
                targetMethod = interfaceMethod.MakeGenericMethod(methodGenerics);

            il.Emit(OpCodes.Callvirt, targetMethod);
            il.Emit(OpCodes.Ret);

            return methodBuilder;
        }

        private static void ApplyGenericConstraints(Type[] genericArgs, GenericTypeParameterBuilder[] builders, IReadOnlyDictionary<Type, Type> map)
        {
            for (var i = 0; i < genericArgs.Length; i++)
            {
                var arg = genericArgs[i];
                var builder = builders[i];
                builder.SetGenericParameterAttributes(arg.GenericParameterAttributes);

                Type? baseConstraint = null;
                List<Type>? interfaceConstraints = null;

                foreach (var constraint in arg.GetGenericParameterConstraints())
                {
                    var mapped = ReplaceGenericArguments(constraint, map);

                    if (mapped.IsClass)
                        baseConstraint = mapped;
                    else if (mapped.IsInterface)
                        (interfaceConstraints ??= []).Add(mapped);
                }

                if (baseConstraint is not null)
                    builder.SetBaseTypeConstraint(baseConstraint);

                if (interfaceConstraints is { Count: > 0 })
                    builder.SetInterfaceConstraints(interfaceConstraints.ToArray());

                CopyCustomAttributes(arg.GetCustomAttributesData(), builder.SetCustomAttribute);
            }
        }

        private static Type ReplaceGenericArguments(Type type, IReadOnlyDictionary<Type, Type>? map)
        {
            if (map is null || map.Count == 0)
                return type;

            if (type.IsGenericParameter && map.TryGetValue(type, out var mapped))
                return mapped;

            if (type.IsByRef)
                return ReplaceGenericArguments(type.GetElementType()!, map).MakeByRefType();

            if (type.IsPointer)
                return ReplaceGenericArguments(type.GetElementType()!, map).MakePointerType();

            if (type.IsArray)
            {
                var element = ReplaceGenericArguments(type.GetElementType()!, map);

                return type.GetArrayRank() == 1 ? element.MakeArrayType() : element.MakeArrayType(type.GetArrayRank());
            }

            if (!type.IsGenericType)
                return type;

            var definition = type.GetGenericTypeDefinition();
            var arguments = type.GetGenericArguments().Select(arg => ReplaceGenericArguments(arg, map)).ToArray();

            return definition.MakeGenericType(arguments);
        }

        private static Type[] MapCustomModifiers(Type[] modifiers, IReadOnlyDictionary<Type, Type>? map)
        {
            if (modifiers.Length == 0)
                return Array.Empty<Type>();

            if (map is null || map.Count == 0)
                return modifiers;

            var mapped = new Type[modifiers.Length];

            for (var i = 0; i < modifiers.Length; i++)
                mapped[i] = ReplaceGenericArguments(modifiers[i], map);

            return mapped;
        }

        private static void CopyCustomAttributes(IList<CustomAttributeData> attributes, Action<CustomAttributeBuilder> apply)
        {
            foreach (var t in attributes)
            {
                if (TryBuildCustomAttribute(t, out var builder))
                    apply(builder);
            }
        }

        private static bool TryBuildCustomAttribute(CustomAttributeData data, out CustomAttributeBuilder builder)
        {
            try
            {
                var ctorArgs = new object?[data.ConstructorArguments.Count];

                for (var i = 0; i < ctorArgs.Length; i++)
                    ctorArgs[i] = GetAttributeValue(data.ConstructorArguments[i]);

                if (data.NamedArguments.Count == 0)
                {
                    builder = new(data.Constructor, ctorArgs);

                    return true;
                }

                var namedProps = new List<PropertyInfo>();
                var propValues = new List<object?>();
                var namedFields = new List<FieldInfo>();
                var fieldValues = new List<object?>();

                foreach (var named in data.NamedArguments)
                {
                    var value = GetAttributeValue(named.TypedValue);

                    if (named.IsField && named.MemberInfo is FieldInfo field)
                    {
                        namedFields.Add(field);
                        fieldValues.Add(value);
                    }
                    else if (named.MemberInfo is PropertyInfo prop)
                    {
                        namedProps.Add(prop);
                        propValues.Add(value);
                    }
                }

                builder = new(data.Constructor, ctorArgs, namedProps.ToArray(), propValues.ToArray(), namedFields.ToArray(), fieldValues.ToArray());

                return true;
            }
            catch
            {
                builder = null!;

                return false;
            }
        }

        private static object? GetAttributeValue(CustomAttributeTypedArgument argument)
        {
            var value = argument.Value;

            if (value is null)
                return null;

            var type = argument.ArgumentType;

            if (type.IsArray && value is IReadOnlyCollection<CustomAttributeTypedArgument> items)
            {
                var elementType = type.GetElementType()!;
                var array = Array.CreateInstance(elementType, items.Count);
                var index = 0;

                foreach (var item in items)
                    array.SetValue(GetAttributeValue(item), index++);

                return array;
            }

            if (type.IsEnum)
                return Enum.ToObject(type, value);

            return value;
        }

        private static Type[] GetInterfaces(Type type)
        {
            var interfaces = type.GetInterfaces();

            if (!type.IsInterface)
                return interfaces;

            if (interfaces.Length == 0)
                return [type];

            var result = new Type[interfaces.Length + 1];
            result[0] = type;
            interfaces.CopyTo(result, 1);

            return result;
        }

        private static Func<TValue, THandler, TValue> BuildConstructor(Type proxyType)
        {
            var ctor = proxyType.GetConstructor([typeof(TValue), typeof(THandler)]);

            if (ctor is null)
                throw new InvalidOperationException($"Constructor for {proxyType} was not found.");

            var dm = new DynamicMethod(
                $"{proxyType.Name}_Ctor",
                typeof(TValue),
                [typeof(TValue), typeof(THandler)],
                typeof(InterfacesOverlay<TValue, THandler>).Module,
                true);

            var il = dm.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Newobj, ctor);
            il.Emit(OpCodes.Ret);

            return (Func<TValue, THandler, TValue>)dm.CreateDelegate(typeof(Func<TValue, THandler, TValue>));
        }

        private static bool HasIntersection(IReadOnlyList<Type> left, IReadOnlyList<Type> right)
        {
            if (left.Count == 0 || right.Count == 0)
                return false;

            foreach (var candidate in left)
            {
                foreach (var t in right)
                {
                    if (candidate == t)
                        return true;
                }
            }

            return false;
        }

        private static void AddTypeSequence(ref HashCode hash, Type[] types)
        {
            hash.Add(types.Length);

            foreach (var t in types)
                hash.Add(t);
        }

        private static bool TypeSequenceEquals(Type[] left, Type[] right)
        {
            if (ReferenceEquals(left, right))
                return true;

            if (left.Length != right.Length)
                return false;

            for (var i = 0; i < left.Length; i++)
            {
                if (left[i] != right[i])
                    return false;
            }

            return true;
        }

        private readonly struct MethodKey : IEquatable<MethodKey>
        {
            private readonly string name;
            private readonly int genericArity;
            private readonly Type returnType;
            private readonly Type[] returnRequired;
            private readonly Type[] returnOptional;
            private readonly ParameterKey[] parameters;
            private readonly int hash;

            public MethodKey(MethodInfo method)
            {
                this.name = method.Name;
                this.genericArity = method.IsGenericMethodDefinition ? method.GetGenericArguments().Length : 0;
                this.returnType = method.ReturnType;
                this.returnRequired = method.ReturnParameter.GetRequiredCustomModifiers();
                this.returnOptional = method.ReturnParameter.GetOptionalCustomModifiers();

                var parameters = method.GetParameters();

                if (parameters.Length == 0)
                    this.parameters = Array.Empty<ParameterKey>();
                else
                {
                    this.parameters = new ParameterKey[parameters.Length];

                    for (var i = 0; i < parameters.Length; i++)
                        this.parameters[i] = new(parameters[i]);
                }

                this.hash = this.ComputeHash();
            }

            public bool Equals(MethodKey other)
            {
                if (!string.Equals(this.name, other.name, StringComparison.Ordinal))
                    return false;

                if (this.genericArity != other.genericArity)
                    return false;

                if (this.returnType != other.returnType)
                    return false;

                if (!TypeSequenceEquals(this.returnRequired, other.returnRequired))
                    return false;

                if (!TypeSequenceEquals(this.returnOptional, other.returnOptional))
                    return false;

                if (this.parameters.Length != other.parameters.Length)
                    return false;

                for (var i = 0; i < this.parameters.Length; i++)
                {
                    if (!this.parameters[i].Equals(other.parameters[i]))
                        return false;
                }

                return true;
            }

            public override bool Equals(object? obj) => obj is MethodKey other && this.Equals(other);

            public override int GetHashCode() => this.hash;

            private int ComputeHash()
            {
                var hash = new HashCode();
                hash.Add(this.name, StringComparer.Ordinal);
                hash.Add(this.genericArity);
                hash.Add(this.returnType);
                AddTypeSequence(ref hash, this.returnRequired);
                AddTypeSequence(ref hash, this.returnOptional);

                foreach (var t in this.parameters)
                    hash.Add(t);

                return hash.ToHashCode();
            }
        }

        private readonly struct ParameterKey : IEquatable<ParameterKey>
        {
            private readonly Type type;
            private readonly Type[] required;
            private readonly Type[] optional;
            private readonly int hash;

            public ParameterKey(ParameterInfo parameter)
            {
                this.type = parameter.ParameterType;
                this.required = parameter.GetRequiredCustomModifiers();
                this.optional = parameter.GetOptionalCustomModifiers();
                this.hash = this.ComputeHash();
            }

            public bool Equals(ParameterKey other) =>
                this.type == other.type && TypeSequenceEquals(this.required, other.required) && TypeSequenceEquals(this.optional, other.optional);

            public override bool Equals(object? obj) => obj is ParameterKey other && this.Equals(other);

            public override int GetHashCode() => this.hash;

            private int ComputeHash()
            {
                var hash = new HashCode();
                hash.Add(this.type);
                AddTypeSequence(ref hash, this.required);
                AddTypeSequence(ref hash, this.optional);

                return hash.ToHashCode();
            }
        }
    }
}
