// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Globalization;

namespace Itexoft.TerminalKit.Validation;

internal sealed class NumericRangeFieldValidator<TNumeric> : ITerminalFormFieldValidator
    where TNumeric : struct, IComparable<TNumeric>, IConvertible
{
    private readonly TNumeric _max;
    private readonly string? _message;
    private readonly TNumeric _min;

    public NumericRangeFieldValidator(TNumeric min, TNumeric max, string? message = null)
    {
        this._min = min;
        this._max = max;
        this._message = message;
    }

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

        if (parsed.CompareTo(this._min) < 0 || parsed.CompareTo(this._max) > 0)
            return this._message ?? $"Value must be between {this._min} and {this._max}.";

        return null;
    }
}