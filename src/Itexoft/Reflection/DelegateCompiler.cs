// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Emit;
using Itexoft.Extensions;

namespace Itexoft.Reflection;

/// <summary>
/// Reflection-time delegate compiler that emits lightweight thunks for static methods to avoid reflection Invoke.
/// </summary>
public static class DelegateCompiler
{
    private static readonly ConcurrentDictionary<(MethodInfo Method, Type DelegateType), Delegate?> cache = new();

    public static bool TryCreate<TDelegate>(MethodInfo method, out TDelegate? del) where TDelegate : class
    {
        method.Required();

        var key = (method, typeof(TDelegate));

        if (cache.TryGetValue(key, out var cached))
        {
            del = cached as TDelegate;

            return del is not null;
        }

        var compiled = Compile(method, typeof(TDelegate));
        cache[key] = compiled;
        del = compiled as TDelegate;

        return del is not null;
    }

    private static Delegate? Compile(MethodInfo method, Type delegateType)
    {
        if (!delegateType.IsSubclassOf(typeof(Delegate)))
            return null;

        try
        {
            return method.CreateDelegate(delegateType);
        }
        catch
        {
            // fall through to IL emit
        }

        var invoke = delegateType.GetMethod("Invoke") ?? throw new InvalidOperationException("Delegate has no Invoke.");
        var parameters = invoke.GetParameters();
        var parameterTypes = parameters.Select(p => p.ParameterType).ToArray();
        var returnType = invoke.ReturnType;

        var dm = new DynamicMethod($"{method.Name}_Thunk", returnType, parameterTypes, typeof(DelegateCompiler).Module, true);

        var il = dm.GetILGenerator();

        for (short i = 0; i < parameters.Length; i++)
            il.Emit(OpCodes.Ldarg, i);

        il.Emit(method.IsVirtual ? OpCodes.Callvirt : OpCodes.Call, method);
        il.Emit(OpCodes.Ret);

        return dm.CreateDelegate(delegateType);
    }
}
