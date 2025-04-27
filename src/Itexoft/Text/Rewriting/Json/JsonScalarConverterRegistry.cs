// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Collections.Concurrent;
using System.Globalization;

namespace Itexoft.Text.Rewriting.Json;

internal static class JsonScalarConverterRegistry
{
    private static readonly ConcurrentDictionary<Type, Func<string, object>> cache = new();

    private static readonly Dictionary<Type, Func<string, object>> standard = new()
    {
        { typeof(string), value => value },
        { typeof(bool), value => ParseBool(value) },
        { typeof(int), value => ParseInt(value) },
        { typeof(long), value => ParseLong(value) },
        { typeof(double), value => ParseDouble(value) },
        { typeof(float), value => ParseFloat(value) },
        { typeof(decimal), value => ParseDecimal(value) },
        { typeof(Guid), value => ParseGuid(value) },
        { typeof(DateTime), value => ParseDateTime(value) },
        { typeof(DateTimeOffset), value => ParseDateTimeOffset(value) },
    };

    internal static Func<string, T>? Resolve<T>()
    {
        var func = Resolve(typeof(T));

        if (func is null)
            return null;

        return value => (T)func(value);
    }

    internal static Func<string, object>? Resolve(Type targetType)
    {
        if (cache.TryGetValue(targetType, out var cached))
            return cached;

        var created = Create(targetType);

        if (created is not null)
            cache[targetType] = created;

        return created;
    }

    private static Func<string, object>? Create(Type targetType)
    {
        if (JsonScalarConverterRegistry.standard.TryGetValue(targetType, out var standard))
            return standard;

        var nullable = Nullable.GetUnderlyingType(targetType);

        if (nullable is not null)
        {
            var inner = Resolve(nullable);

            if (inner is null)
                return null;

            return value =>
            {
                if (string.Equals(value, "null", StringComparison.OrdinalIgnoreCase))
                    return null!;

                return inner(value);
            };
        }

        if (targetType.IsEnum)
        {
            return value =>
            {
                try
                {
                    return Enum.Parse(targetType, value, true);
                }
                catch (Exception ex)
                {
                    throw new FormatException($"Value '{value}' is not valid for enum {targetType.FullName}.", ex);
                }
            };
        }

        return null;
    }

    private static object ParseBool(string value)
    {
        if (bool.TryParse(value, out var result))
            return result;

        throw new FormatException($"Value '{value}' is not a valid boolean.");
    }

    private static object ParseInt(string value)
    {
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
            return result;

        throw new FormatException($"Value '{value}' is not a valid integer.");
    }

    private static object ParseLong(string value)
    {
        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
            return result;

        throw new FormatException($"Value '{value}' is not a valid long.");
    }

    private static object ParseDouble(string value)
    {
        if (double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var result))
            return result;

        throw new FormatException($"Value '{value}' is not a valid double.");
    }

    private static object ParseFloat(string value)
    {
        if (float.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var result))
            return result;

        throw new FormatException($"Value '{value}' is not a valid float.");
    }

    private static object ParseDecimal(string value)
    {
        if (decimal.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var result))
            return result;

        throw new FormatException($"Value '{value}' is not a valid decimal.");
    }

    private static object ParseGuid(string value)
    {
        if (Guid.TryParse(value, out var result))
            return result;

        throw new FormatException($"Value '{value}' is not a valid GUID.");
    }

    private static object ParseDateTime(string value)
    {
        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var result))
            return result;

        throw new FormatException($"Value '{value}' is not a valid DateTime.");
    }

    private static object ParseDateTimeOffset(string value)
    {
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var result))
            return result;

        throw new FormatException($"Value '{value}' is not a valid DateTimeOffset.");
    }
}
