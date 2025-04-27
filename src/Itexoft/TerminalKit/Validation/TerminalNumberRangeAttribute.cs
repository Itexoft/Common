// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.TerminalKit.Validation;

/// <summary>
/// Enforces numeric ranges for common integral and floating-point types.
/// </summary>
public sealed class TerminalNumberRangeAttribute : TerminalValidationAttribute
{
    private readonly ITerminalFormFieldValidator validator;

    /// <summary>
    /// Initializes the attribute for <see cref="byte" /> values.
    /// </summary>
    public TerminalNumberRangeAttribute(byte min, byte max, string? message = null) =>
        this.validator = new NumericRangeFieldValidator<byte>(min, max, message);

    /// <summary>
    /// Initializes the attribute for <see cref="sbyte" /> values.
    /// </summary>
    public TerminalNumberRangeAttribute(sbyte min, sbyte max, string? message = null) =>
        this.validator = new NumericRangeFieldValidator<sbyte>(min, max, message);

    /// <summary>
    /// Initializes the attribute for <see cref="short" /> values.
    /// </summary>
    public TerminalNumberRangeAttribute(short min, short max, string? message = null) =>
        this.validator = new NumericRangeFieldValidator<short>(min, max, message);

    /// <summary>
    /// Initializes the attribute for <see cref="ushort" /> values.
    /// </summary>
    public TerminalNumberRangeAttribute(ushort min, ushort max, string? message = null) =>
        this.validator = new NumericRangeFieldValidator<ushort>(min, max, message);

    /// <summary>
    /// Initializes the attribute for <see cref="int" /> values.
    /// </summary>
    public TerminalNumberRangeAttribute(int min, int max, string? message = null) =>
        this.validator = new NumericRangeFieldValidator<int>(min, max, message);

    /// <summary>
    /// Initializes the attribute for <see cref="uint" /> values.
    /// </summary>
    public TerminalNumberRangeAttribute(uint min, uint max, string? message = null) =>
        this.validator = new NumericRangeFieldValidator<uint>(min, max, message);

    /// <summary>
    /// Initializes the attribute for <see cref="long" /> values.
    /// </summary>
    public TerminalNumberRangeAttribute(long min, long max, string? message = null) =>
        this.validator = new NumericRangeFieldValidator<long>(min, max, message);

    /// <summary>
    /// Initializes the attribute for <see cref="ulong" /> values.
    /// </summary>
    public TerminalNumberRangeAttribute(ulong min, ulong max, string? message = null) =>
        this.validator = new NumericRangeFieldValidator<ulong>(min, max, message);

    /// <summary>
    /// Initializes the attribute for <see cref="float" /> values.
    /// </summary>
    public TerminalNumberRangeAttribute(float min, float max, string? message = null) =>
        this.validator = new NumericRangeFieldValidator<float>(min, max, message);

    /// <summary>
    /// Initializes the attribute for <see cref="double" /> values.
    /// </summary>
    public TerminalNumberRangeAttribute(double min, double max, string? message = null) =>
        this.validator = new NumericRangeFieldValidator<double>(min, max, message);

    /// <summary>
    /// Initializes the attribute for <see cref="decimal" /> values.
    /// </summary>
    public TerminalNumberRangeAttribute(decimal min, decimal max, string? message = null) =>
        this.validator = new NumericRangeFieldValidator<decimal>(min, max, message);

    /// <inheritdoc />
    public override ITerminalFormFieldValidator CreateValidator() => this.validator;
}
