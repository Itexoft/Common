// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Globalization;

namespace Itexoft.TerminalKit.Validation;

internal sealed class NumericRangeFieldValidator<TNumeric>(TNumeric min, TNumeric max, string? message = null) : ITerminalFormFieldValidator
    where TNumeric : struct, IComparable<TNumeric>, IConvertible
{
    public string? Validate(TerminalFormFieldDefinition field, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        TNumeric parsed;

        try
        {
            parsed = (TNumeric)Convert.ChangeType(value, typeof(TNumeric), CultureInfo.InvariantCulture);
        }
        catch
        {
            return $"'{value}' is not a valid {typeof(TNumeric).Name}.";
        }

        if (parsed.CompareTo(min) < 0 || parsed.CompareTo(max) > 0)
            return message ?? $"Value must be between {min} and {max}.";

        return null;
    }
}
