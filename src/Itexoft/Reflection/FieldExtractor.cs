// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Reflection;
using System.Reflection.Emit;
using Itexoft.Extensions;

namespace Itexoft.Reflection;

public static class FieldExtractor
{
    public static Func<TObject, TValue?> BuildGetter<TObject, TValue>(string? name = null) where TValue : class // temp
    {
        var tokenType = typeof(TObject);
        var sourceType = typeof(TValue);

        var field = tokenType.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .FirstOrDefault(f => (name == null || f.Name == name) && sourceType.IsAssignableFrom(f.FieldType)).Required();

        var dm = new DynamicMethod($"{typeof(TValue).Name}.{field.Name}", sourceType, [tokenType], tokenType.Module, true);

        var il = dm.GetILGenerator();
        il.Emit(OpCodes.Ldarga_S, 0);
        il.Emit(OpCodes.Ldfld, field);

        if (field.FieldType != sourceType)
            il.Emit(OpCodes.Castclass, sourceType);

        il.Emit(OpCodes.Ret);

        return (Func<TObject, TValue?>)dm.CreateDelegate(typeof(Func<TObject, TValue?>));
    }
}
