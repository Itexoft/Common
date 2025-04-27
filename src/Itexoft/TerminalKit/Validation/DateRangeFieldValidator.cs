// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Globalization;

namespace Itexoft.TerminalKit.Validation;

internal sealed class DateRangeFieldValidator : ITerminalFormFieldValidator
{
    private readonly DateTime? _max;
    private readonly string? _message;
    private readonly DateTime? _min;

    public DateRangeFieldValidator(DateTime? min, DateTime? max, string? message = null)
    {
        this._min = min;
        this._max = max;
        this._message = message;
    }

    public string? Validate(TerminalFormFieldDefinition field, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (!DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.None, out var parsed))
            return $"'{value}' is not a valid date.";

        if (this._min.HasValue && parsed < this._min.Value)
            return this._message ?? $"Date must be on or after {this._min.Value:d}.";

        if (this._max.HasValue && parsed > this._max.Value)
            return this._message ?? $"Date must be on or before {this._max.Value:d}.";

        return null;
    }
}