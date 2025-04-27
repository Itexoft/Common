// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Formats.Configuration.Ini;

internal static class IniValueParsing
{
    public static bool TryParseBoolean(ReadOnlySpan<char> span, out bool value)
    {
        span = IniParsing.TrimOws(span, out _, out _);

        if (EqualsIgnoreCase(span, "yes") || EqualsIgnoreCase(span, "true") || span.SequenceEqual("1"))
        {
            value = true;

            return true;
        }

        if (EqualsIgnoreCase(span, "no") || EqualsIgnoreCase(span, "false") || span.SequenceEqual("0"))
        {
            value = false;

            return true;
        }

        value = false;

        return false;
    }

    public static bool TryParseInt32(ReadOnlySpan<char> span, out int value)
    {
        if (!TryParseInt64(span, out var longValue))
        {
            value = 0;

            return false;
        }

        if (longValue is < int.MinValue or > int.MaxValue)
        {
            value = 0;

            return false;
        }

        value = (int)longValue;

        return true;
    }

    public static bool TryParseInt64(ReadOnlySpan<char> span, out long value)
    {
        span = IniParsing.TrimOws(span, out _, out _);

        if (span.IsEmpty)
        {
            value = 0;

            return false;
        }

        var index = 0;
        var sign = 1;

        if (span[0] == '-')
        {
            sign = -1;
            index = 1;
        }
        else if (span[0] == '+')
            index = 1;

        if (index >= span.Length)
        {
            value = 0;

            return false;
        }

        long acc = 0;

        for (; index < span.Length; index++)
        {
            var digit = span[index] - '0';

            if ((uint)digit > 9)
            {
                value = 0;

                return false;
            }

            acc = acc * 10 + digit;
        }

        value = acc * sign;

        return true;
    }

    public static bool TryParseNumber(ReadOnlySpan<char> span, out long value)
    {
        span = IniParsing.TrimOws(span, out _, out _);

        if (span.Length >= 2 && span[0] == '0' && (span[1] == 'x' || span[1] == 'X'))
            return TryParseHex(span[2..], out value);

        return TryParseInt64(span, out value);
    }

    private static bool TryParseHex(ReadOnlySpan<char> span, out long value)
    {
        if (span.IsEmpty)
        {
            value = 0;

            return false;
        }

        long acc = 0;
        var hasDigits = false;

        foreach (var ch in span)
        {
            var digit = ParseHexDigit(ch);

            if (digit < 0)
                break;

            hasDigits = true;
            acc = (acc << 4) + digit;
        }

        value = acc;

        return hasDigits;
    }

    private static int ParseHexDigit(char value)
    {
        if (value is >= '0' and <= '9')
            return value - '0';

        if (value is >= 'a' and <= 'f')
            return value - 'a' + 10;

        if (value is >= 'A' and <= 'F')
            return value - 'A' + 10;

        return -1;
    }

    private static bool EqualsIgnoreCase(ReadOnlySpan<char> span, string value)
    {
        if (span.Length != value.Length)
            return false;

        for (var i = 0; i < span.Length; i++)
        {
            var a = span[i];
            var b = value[i];

            if (a == b)
                continue;

            if (char.ToUpperInvariant(a) != char.ToUpperInvariant(b))
                return false;
        }

        return true;
    }
}
