// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Collections;
using System.Reflection;
using Itexoft.Extensions;

namespace Itexoft.Text.Rewriting.Json;

internal static class JsonProjectionTypeInspector
{
    internal static IEnumerable<MemberInfo> GetProjectionMembers(Type type)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public;

        foreach (var property in type.GetProperties(flags))
        {
            if (property.GetIndexParameters().Length != 0)
                continue;

            if (property.GetMethod is null || property.SetMethod is null)
                continue;

            yield return property;
        }

        foreach (var field in type.GetFields(flags))
        {
            if (field.IsStatic)
                continue;

            yield return field;
        }
    }

    internal static Type GetMemberType(MemberInfo member) =>
        member switch
        {
            PropertyInfo property => property.PropertyType,
            FieldInfo field => field.FieldType,
            _ => throw new InvalidOperationException("Projection target must be a property or field access."),
        };

    internal static bool IsScalar(Type type)
    {
        if (type == typeof(string) || type.IsEnum)
            return true;

        if (JsonScalarConverterRegistry.Resolve(type) is not null)
            return true;

        var underlying = Nullable.GetUnderlyingType(type);

        if (underlying is not null)
            return IsScalar(underlying);

        return false;
    }

    internal static bool IsObject(Type type)
    {
        if (type == typeof(string) || type.IsValueType || type.IsEnum)
            return false;

        if (typeof(IEnumerable).IsAssignableFrom(type))
            return false;

        return type.IsClass;
    }

    internal static string GetDefaultPointer(MemberInfo member, JsonProjectionOptions options)
    {
        member.Required();
        options.Required();

        return "/" + JsonPropertyNameHelper.Convert(member.Name, options.PropertyNameStyle);
    }
}
