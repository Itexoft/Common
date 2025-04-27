// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Globalization;

namespace Itexoft.TerminalKit.Validation;

internal sealed class DateRangeFieldValidator(DateTime? min, DateTime? max, string? message = null) : ITerminalFormFieldValidator
{
    public string? Validate(TerminalFormFieldDefinition field, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (!DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.None, out var parsed))
            return $"'{value}' is not a valid date.";

        if (min.HasValue && parsed < min.Value)
            return message ?? $"Date must be on or after {min.Value:d}.";

        if (max.HasValue && parsed > max.Value)
            return message ?? $"Date must be on or before {max.Value:d}.";

        return null;
    }
}
