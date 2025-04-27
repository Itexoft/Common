// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.TerminalKit.Validation;

/// <summary>
/// Enforces a start/end date window for text-based date editors.
/// </summary>
public sealed class TerminalDateRangeAttribute : TerminalValidationAttribute
{
    /// <summary>
    /// Gets the inclusive minimum allowed date/time.
    /// </summary>
    public DateTime Min { get; init; } = DateTime.MinValue;

    /// <summary>
    /// Gets the inclusive maximum allowed date/time.
    /// </summary>
    public DateTime Max { get; init; } = DateTime.MaxValue;

    /// <summary>
    /// Gets or sets the custom validation message.
    /// </summary>
    public string? Message { get; init; }

    /// <inheritdoc />
    public override ITerminalFormFieldValidator CreateValidator() => new DateRangeFieldValidator(this.Min, this.Max, this.Message);
}