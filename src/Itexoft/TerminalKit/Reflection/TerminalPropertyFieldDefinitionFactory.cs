// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Globalization;
using System.Reflection;
using Itexoft.Extensions;
using Itexoft.TerminalKit.Validation;

namespace Itexoft.TerminalKit.Reflection;

internal static class TerminalPropertyFieldDefinitionFactory
{
    public static TerminalFormFieldDefinition? TryCreate(PropertyInfo property, object? target)
    {
        property.Required();

        var visibility = property.GetCustomAttribute<TerminalVisibilityAttribute>();
        var isPublic = (property.GetMethod?.IsPublic ?? false) || (property.SetMethod?.IsPublic ?? false);

        if (!(visibility?.IsVisible ?? isPublic))
            return null;

        if (property.GetCustomAttribute<TerminalReadOnlyAttribute>()?.IsReadOnly == true)
            return null;

        if (!property.CanWrite)
            return null;

        if (property.IsSpecialName || property.GetIndexParameters().Length > 0)
            return null;

        var editor = ResolveEditor(property.PropertyType, out var options);

        if (editor == null)
            return null;

        var label = ResolveLabel(property);
        var validators = ResolveValidators(property);
        var resolvedType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

        if (!property.IsDefined(typeof(TerminalNumberRangeAttribute), true)
            && TryCreateDefaultNumericValidator(resolvedType, out var numericValidator)
            && numericValidator != null)
            validators.Add(numericValidator);
        else if (TryCreateParsingValidator(resolvedType, out var parsingValidator) && parsingValidator != null)
            validators.Add(parsingValidator);

        var isRequired = !IsNullable(property.PropertyType);

        return new()
        {
            Key = DataBindingKey.From(property.Name),
            Label = label,
            Editor = editor.Value,
            Options = options,
            IsRequired = isRequired,
            IsReadOnly = property.IsDefined(typeof(TerminalReadOnlyAttribute), true) || !property.CanWrite,
            Validators = validators.ToArray(),
        };
    }

    private static TerminalFormFieldEditor? ResolveEditor(Type propertyType, out IReadOnlyList<string> options)
    {
        propertyType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

        if (propertyType.IsEnum)
        {
            options = Enum.GetNames(propertyType);

            return TerminalFormFieldEditor.Select;
        }

        if (propertyType == typeof(bool))
        {
            options = [bool.TrueString, bool.FalseString];

            return TerminalFormFieldEditor.Select;
        }

        options = [];

        if (propertyType == typeof(string) || propertyType == typeof(Guid))
            return TerminalFormFieldEditor.Text;

        if (propertyType == typeof(DateTime) || propertyType == typeof(DateOnly))
            return TerminalFormFieldEditor.Text;

        if (propertyType.IsPrimitive || propertyType == typeof(decimal))
            return TerminalFormFieldEditor.Text;

        return null;
    }

    private static string ResolveLabel(MemberInfo member)
    {
        var display = member.GetCustomAttribute<TerminalDisplayAttribute>();

        return string.IsNullOrWhiteSpace(display?.Label) ? member.Name : display!.Label!;
    }

    private static List<ITerminalFormFieldValidator> ResolveValidators(PropertyInfo property)
    {
        var attributes = property.GetCustomAttributes<TerminalValidationAttribute>(inherit: true);
        var validators = new List<ITerminalFormFieldValidator>();

        foreach (var attribute in attributes)
        {
            var validator = attribute.CreateValidator();

            if (validator != null)
                validators.Add(validator);
        }

        return validators;
    }

    private static bool IsNullable(Type type)
    {
        if (!type.IsValueType)
            return true;

        return Nullable.GetUnderlyingType(type) != null;
    }

    private static bool TryCreateDefaultNumericValidator(Type type, out ITerminalFormFieldValidator? validator)
    {
        validator = type switch
        {
            var t when t == typeof(byte) => new NumericRangeFieldValidator<byte>(byte.MinValue, byte.MaxValue),
            var t when t == typeof(sbyte) => new NumericRangeFieldValidator<sbyte>(sbyte.MinValue, sbyte.MaxValue),
            var t when t == typeof(short) => new NumericRangeFieldValidator<short>(short.MinValue, short.MaxValue),
            var t when t == typeof(ushort) => new NumericRangeFieldValidator<ushort>(ushort.MinValue, ushort.MaxValue),
            var t when t == typeof(int) => new NumericRangeFieldValidator<int>(int.MinValue, int.MaxValue),
            var t when t == typeof(uint) => new NumericRangeFieldValidator<uint>(uint.MinValue, uint.MaxValue),
            var t when t == typeof(long) => new NumericRangeFieldValidator<long>(long.MinValue, long.MaxValue),
            var t when t == typeof(ulong) => new NumericRangeFieldValidator<ulong>(ulong.MinValue, ulong.MaxValue),
            var t when t == typeof(float) => new NumericRangeFieldValidator<float>(float.MinValue, float.MaxValue),
            var t when t == typeof(double) => new NumericRangeFieldValidator<double>(double.MinValue, double.MaxValue),
            var t when t == typeof(decimal) => new NumericRangeFieldValidator<decimal>(decimal.MinValue, decimal.MaxValue),
            _ => null,
        };

        return validator != null;
    }

    private static bool TryCreateParsingValidator(Type type, out ITerminalFormFieldValidator? validator)
    {
        validator = null;

        if (type == typeof(DateTime))
        {
            validator = new DelegateFieldValidator((field, value) =>
            {
                if (string.IsNullOrWhiteSpace(value))
                    return null;

                return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _)
                    ? null
                    : "Use ISO 8601 date/time, e.g. 2025-11-12T00:17:40Z.";
            });

            return true;
        }

        if (type == typeof(DateOnly))
        {
            validator = new DelegateFieldValidator((field, value) =>
            {
                if (string.IsNullOrWhiteSpace(value))
                    return null;

                return DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
                    ? null
                    : "Use ISO date format, e.g. 2025-11-12.";
            });

            return true;
        }

        if (type == typeof(Guid))
        {
            validator = new DelegateFieldValidator((field, value) =>
            {
                if (string.IsNullOrWhiteSpace(value))
                    return null;

                return Guid.TryParse(value, out _) ? null : "Enter a valid GUID (xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx).";
            });

            return true;
        }

        return false;
    }
}
