// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Collections;
using System.Runtime.CompilerServices;

namespace Itexoft.Extensions;

public static class RequiredExtensions
{
    public static TValue Required<TValue>(this TValue? value, [CallerArgumentExpression(nameof(value))] string? name = null) =>
        value is null ? throw new ArgumentNullException(name) : value;

    public static string RequiredNotWhiteSpace(this string? value, [CallerArgumentExpression(nameof(value))] string? name = null)
    {
        if (value is null)
            throw new ArgumentNullException(name);

        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value cannot be empty or whitespace.", name);

        return value;
    }

    public static int RequiredPositive(this int value, [CallerArgumentExpression(nameof(value))] string? name = null)
    {
        if (value <= 0)
            throw new ArgumentOutOfRangeException(name, value, "Value must be positive.");

        return value;
    }

    public static int RequiredPositiveOrZero(this int value, [CallerArgumentExpression(nameof(value))] string? name = null)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(name, value, "Value must be zero or positive.");

        return value;
    }

    public static int RequiredNegative(this int value, [CallerArgumentExpression(nameof(value))] string? name = null)
    {
        if (value >= 0)
            throw new ArgumentOutOfRangeException(name, value, "Value must be negative.");

        return value;
    }

    public static int RequiredNegativeOrZero(this int value, [CallerArgumentExpression(nameof(value))] string? name = null)
    {
        if (value > 0)
            throw new ArgumentOutOfRangeException(name, value, "Value must be zero or negative.");

        return value;
    }

    public static int RequiredZero(this int value, [CallerArgumentExpression(nameof(value))] string? name = null)
    {
        if (value != 0)
            throw new ArgumentOutOfRangeException(name, value, "Value must be zero.");

        return value;
    }

    public static long RequiredPositive(this long value, [CallerArgumentExpression(nameof(value))] string? name = null)
    {
        if (value <= 0)
            throw new ArgumentOutOfRangeException(name, value, "Value must be positive.");

        return value;
    }

    public static long RequiredPositiveOrZero(this long value, [CallerArgumentExpression(nameof(value))] string? name = null)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(name, value, "Value must be zero or positive.");

        return value;
    }

    public static long RequiredNegative(this long value, [CallerArgumentExpression(nameof(value))] string? name = null)
    {
        if (value >= 0)
            throw new ArgumentOutOfRangeException(name, value, "Value must be negative.");

        return value;
    }

    public static long RequiredNegativeOrZero(this long value, [CallerArgumentExpression(nameof(value))] string? name = null)
    {
        if (value > 0)
            throw new ArgumentOutOfRangeException(name, value, "Value must be zero or negative.");

        return value;
    }

    public static long RequiredZero(this long value, [CallerArgumentExpression(nameof(value))] string? name = null)
    {
        if (value != 0)
            throw new ArgumentOutOfRangeException(name, value, "Value must be zero.");

        return value;
    }

    public static double RequiredPositive(this double value, [CallerArgumentExpression(nameof(value))] string? name = null)
    {
        if (double.IsNaN(value) || value <= 0)
            throw new ArgumentOutOfRangeException(name, value, "Value must be positive and not NaN.");

        return value;
    }

    public static double RequiredPositiveOrZero(this double value, [CallerArgumentExpression(nameof(value))] string? name = null)
    {
        if (double.IsNaN(value) || value < 0)
            throw new ArgumentOutOfRangeException(name, value, "Value must be zero or positive and not NaN.");

        return value;
    }

    public static double RequiredNegative(this double value, [CallerArgumentExpression(nameof(value))] string? name = null)
    {
        if (double.IsNaN(value) || value >= 0)
            throw new ArgumentOutOfRangeException(name, value, "Value must be negative and not NaN.");

        return value;
    }

    public static double RequiredNegativeOrZero(this double value, [CallerArgumentExpression(nameof(value))] string? name = null)
    {
        if (double.IsNaN(value) || value > 0)
            throw new ArgumentOutOfRangeException(name, value, "Value must be zero or negative and not NaN.");

        return value;
    }

    public static double RequiredZero(this double value, [CallerArgumentExpression(nameof(value))] string? name = null)
    {
        if (double.IsNaN(value) || value != 0)
            throw new ArgumentOutOfRangeException(name, value, "Value must be zero and not NaN.");

        return value;
    }

    public static TimeSpan RequiredPositive(this TimeSpan value, [CallerArgumentExpression(nameof(value))] string? name = null)
    {
        if (value <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(name, value, "Value must be positive.");

        return value;
    }

    public static TimeSpan RequiredPositiveOrZero(this TimeSpan value, [CallerArgumentExpression(nameof(value))] string? name = null)
    {
        if (value < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(name, value, "Value must be zero or positive.");

        return value;
    }

    public static TimeSpan RequiredNegative(this TimeSpan value, [CallerArgumentExpression(nameof(value))] string? name = null)
    {
        if (value >= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(name, value, "Value must be negative.");

        return value;
    }

    public static TimeSpan RequiredNegativeOrZero(this TimeSpan value, [CallerArgumentExpression(nameof(value))] string? name = null)
    {
        if (value > TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(name, value, "Value must be zero or negative.");

        return value;
    }

    public static TimeSpan RequiredZero(this TimeSpan value, [CallerArgumentExpression(nameof(value))] string? name = null)
    {
        if (value != TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(name, value, "Value must be zero.");

        return value;
    }

    public static int RequiredInRange(this int value, int min, int max, [CallerArgumentExpression(nameof(value))] string? name = null)
    {
        if (min > max)
            throw new ArgumentOutOfRangeException(nameof(min), min, "Min is greater than max.");

        if (value < min || value > max)
            throw new ArgumentOutOfRangeException(name, value, "Value is out of range.");

        return value;
    }

    public static long RequiredInRange(this long value, long min, long max, [CallerArgumentExpression(nameof(value))] string? name = null)
    {
        if (min > max)
            throw new ArgumentOutOfRangeException(nameof(min), min, "Min is greater than max.");

        if (value < min || value > max)
            throw new ArgumentOutOfRangeException(name, value, "Value is out of range.");

        return value;
    }

    public static double RequiredInRange(this double value, double min, double max, [CallerArgumentExpression(nameof(value))] string? name = null)
    {
        if (min > max)
            throw new ArgumentOutOfRangeException(nameof(min), min, "Min is greater than max.");

        if (double.IsNaN(value) || value < min || value > max)
            throw new ArgumentOutOfRangeException(name, value, "Value is out of range.");

        return value;
    }

    public static TimeSpan RequiredInRange(
        this TimeSpan value,
        TimeSpan min,
        TimeSpan max,
        [CallerArgumentExpression(nameof(value))] string? name = null)
    {
        if (min > max)
            throw new ArgumentOutOfRangeException(nameof(min), min, "Min is greater than max.");

        if (value < min || value > max)
            throw new ArgumentOutOfRangeException(name, value, "Value is out of range.");

        return value;
    }
}

public static class RequiredCollectionsExtensions
{
    public static TValue RequiredNotEmpty<TValue, T>(this TValue? value, [CallerArgumentExpression(nameof(value))] string? name = null)
        where TValue : ICollection<T>
    {
        if (value is null)
            throw new ArgumentNullException(name);

        if (value.Count == 0)
            throw new ArgumentException("Collection cannot be empty.", name);

        return value;
    }

    public static TValue RequiredNotEmpty<TValue>(this TValue? value, [CallerArgumentExpression(nameof(value))] string? name = null)
        where TValue : ICollection
    {
        if (value is null)
            throw new ArgumentNullException(name);

        if (value.Count == 0)
            throw new ArgumentException("Collection cannot be empty.", name);

        return value;
    }
}

public static class RequiredGreaterOrLessOrEqualExtensions
{
    public static int RequiredGreaterOrEqual(this int value, int min, [CallerArgumentExpression(nameof(value))] string? name = null)
    {
        if (value < min)
            throw new ArgumentOutOfRangeException(name, value, "Value is out of range.");

        return value;
    }

    public static int RequiredGreater(this int value, int min, [CallerArgumentExpression(nameof(value))] string? name = null)
    {
        if (value <= min)
            throw new ArgumentOutOfRangeException(name, value, "Value is out of range.");

        return value;
    }

    public static int RequiredLessOrEqual(this int value, int max, [CallerArgumentExpression(nameof(value))] string? name = null)
    {
        if (value > max)
            throw new ArgumentOutOfRangeException(name, value, "Value is out of range.");

        return value;
    }

    public static int RequiredLess(this int value, int max, [CallerArgumentExpression(nameof(value))] string? name = null)
    {
        if (value >= max)
            throw new ArgumentOutOfRangeException(name, value, "Value is out of range.");

        return value;
    }

    public static long RequiredGreaterOrEqual(this long value, long min, [CallerArgumentExpression(nameof(value))] string? name = null)
    {
        if (value < min)
            throw new ArgumentOutOfRangeException(name, value, "Value is out of range.");

        return value;
    }

    public static long RequiredGreater(this long value, long min, [CallerArgumentExpression(nameof(value))] string? name = null)
    {
        if (value <= min)
            throw new ArgumentOutOfRangeException(name, value, "Value is out of range.");

        return value;
    }

    public static long RequiredLessOrEqual(this long value, long max, [CallerArgumentExpression(nameof(value))] string? name = null)
    {
        if (value > max)
            throw new ArgumentOutOfRangeException(name, value, "Value is out of range.");

        return value;
    }

    public static long RequiredLess(this long value, long max, [CallerArgumentExpression(nameof(value))] string? name = null)
    {
        if (value >= max)
            throw new ArgumentOutOfRangeException(name, value, "Value is out of range.");

        return value;
    }

    public static double RequiredGreaterOrEqual(this double value, double min, [CallerArgumentExpression(nameof(value))] string? name = null)
    {
        if (double.IsNaN(value) || value < min)
            throw new ArgumentOutOfRangeException(name, value, "Value is out of range.");

        return value;
    }

    public static double RequiredGreater(this double value, double min, [CallerArgumentExpression(nameof(value))] string? name = null)
    {
        if (double.IsNaN(value) || value <= min)
            throw new ArgumentOutOfRangeException(name, value, "Value is out of range.");

        return value;
    }

    public static double RequiredLessOrEqual(this double value, double max, [CallerArgumentExpression(nameof(value))] string? name = null)
    {
        if (double.IsNaN(value) || value > max)
            throw new ArgumentOutOfRangeException(name, value, "Value is out of range.");

        return value;
    }

    public static double RequiredLess(this double value, double max, [CallerArgumentExpression(nameof(value))] string? name = null)
    {
        if (double.IsNaN(value) || value >= max)
            throw new ArgumentOutOfRangeException(name, value, "Value is out of range.");

        return value;
    }

    public static TimeSpan RequiredGreaterOrEqual(this TimeSpan value, TimeSpan min, [CallerArgumentExpression(nameof(value))] string? name = null)
    {
        if (value < min)
            throw new ArgumentOutOfRangeException(name, value, "Value is out of range.");

        return value;
    }

    public static TimeSpan RequiredGreater(this TimeSpan value, TimeSpan min, [CallerArgumentExpression(nameof(value))] string? name = null)
    {
        if (value <= min)
            throw new ArgumentOutOfRangeException(name, value, "Value is out of range.");

        return value;
    }

    public static TimeSpan RequiredLessOrEqual(this TimeSpan value, TimeSpan max, [CallerArgumentExpression(nameof(value))] string? name = null)
    {
        if (value > max)
            throw new ArgumentOutOfRangeException(name, value, "Value is out of range.");

        return value;
    }

    public static TimeSpan RequiredLess(this TimeSpan value, TimeSpan max, [CallerArgumentExpression(nameof(value))] string? name = null)
    {
        if (value >= max)
            throw new ArgumentOutOfRangeException(name, value, "Value is out of range.");

        return value;
    }
}

public static class RequiredReadOnlyCollectionsExtensions
{
    public static TValue RequiredNotEmpty<TValue, T>(this TValue? value, [CallerArgumentExpression(nameof(value))] string? name = null)
        where TValue : IReadOnlyCollection<T>
    {
        if (value is null)
            throw new ArgumentNullException(name);

        if (value.Count == 0)
            throw new ArgumentException("Collection cannot be empty.", name);

        return value;
    }
}
