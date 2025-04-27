// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Net.Http.Internal;

internal static class NetHttpParsing
{
    public static int IndexOfHeaderTerminator(ReadOnlySpan<byte> span)
    {
        if (span.Length < 4)
            return -1;

        for (var i = 0; i <= span.Length - 4; i++)
        {
            if (span[i] == (byte)'\r' && span[i + 1] == (byte)'\n' && span[i + 2] == (byte)'\r' && span[i + 3] == (byte)'\n')
                return i;
        }

        return -1;
    }

    public static bool TryReadLine(ref ReadOnlySpan<byte> span, out ReadOnlySpan<byte> line)
    {
        for (var i = 0; i + 1 < span.Length; i++)
        {
            if (span[i] != (byte)'\r' || span[i + 1] != (byte)'\n')
                continue;

            line = span[..i];
            span = span[(i + 2)..];

            return true;
        }

        line = default;

        return false;
    }

    public static ReadOnlySpan<byte> TrimOws(ReadOnlySpan<byte> span)
    {
        var start = 0;
        var end = span.Length;

        while (start < end && IsWhitespace(span[start]))
            start++;

        while (end > start && IsWhitespace(span[end - 1]))
            end--;

        return span[start..end];
    }

    public static ReadOnlySpan<char> TrimOws(ReadOnlySpan<char> span)
    {
        var start = 0;
        var end = span.Length;

        while (start < end && IsWhitespace(span[start]))
            start++;

        while (end > start && IsWhitespace(span[end - 1]))
            end--;

        return span[start..end];
    }

    public static bool TryParseInt64(ReadOnlySpan<char> span, out long result)
    {
        result = 0;
        span = TrimOws(span);

        if (span.IsEmpty)
            return false;

        var sign = 1;
        var index = 0;

        if (span[0] == '-')
        {
            sign = -1;
            index = 1;
        }
        else if (span[0] == '+')
            index = 1;

        if (index >= span.Length)
            return false;

        long acc = 0;

        for (; index < span.Length; index++)
        {
            var digit = span[index] - '0';

            if ((uint)digit > 9)
                return false;

            acc = acc * 10 + digit;
        }

        result = acc * sign;

        return true;
    }

    public static bool TryParseInt32(ReadOnlySpan<byte> span, out int result)
    {
        result = 0;
        span = TrimOws(span);

        if (span.IsEmpty)
            return false;

        var sign = 1;
        var index = 0;

        if (span[0] == (byte)'-')
        {
            sign = -1;
            index = 1;
        }
        else if (span[0] == (byte)'+')
            index = 1;

        if (index >= span.Length)
            return false;

        var acc = 0;

        for (; index < span.Length; index++)
        {
            var digit = span[index] - (byte)'0';

            if ((uint)digit > 9)
                return false;

            acc = acc * 10 + digit;
        }

        result = acc * sign;

        return true;
    }

    public static bool TryParseInt64(ReadOnlySpan<byte> span, out long result)
    {
        result = 0;
        span = TrimOws(span);

        if (span.IsEmpty)
            return false;

        var sign = 1;
        var index = 0;

        if (span[0] == (byte)'-')
        {
            sign = -1;
            index = 1;
        }
        else if (span[0] == (byte)'+')
            index = 1;

        if (index >= span.Length)
            return false;

        long acc = 0;

        for (; index < span.Length; index++)
        {
            var digit = span[index] - (byte)'0';

            if ((uint)digit > 9)
                return false;

            acc = acc * 10 + digit;
        }

        result = acc * sign;

        return true;
    }

    public static bool TryParseHexInt64(ReadOnlySpan<byte> span, out long result)
    {
        result = 0;
        span = TrimOws(span);

        if (span.IsEmpty)
            return false;

        long acc = 0;
        var hasDigits = false;

        foreach (var b in span)
        {
            var digit = ParseHex(b);

            if (digit < 0)
                break;

            hasDigits = true;
            acc = (acc << 4) + digit;
        }

        result = acc;

        return hasDigits;
    }

    public static bool ContainsToken(ReadOnlySpan<char> value, ReadOnlySpan<char> token)
    {
        while (true)
        {
            value = TrimOws(value);

            if (value.IsEmpty)
                return false;

            var comma = value.IndexOf(',');
            var part = comma >= 0 ? value[..comma] : value;
            part = TrimOws(part);

            var semi = part.IndexOf(';');

            if (semi >= 0)
                part = TrimOws(part[..semi]);

            if (EqualsIgnoreCase(part, token))
                return true;

            if (comma < 0)
                return false;

            value = value[(comma + 1)..];
        }
    }

    public static bool TryGetCharset(ReadOnlySpan<char> value, out ReadOnlySpan<char> charset)
    {
        charset = default;
        var span = value;

        while (true)
        {
            var sep = span.IndexOf(';');

            if (sep < 0)
                return false;

            span = span[(sep + 1)..];
            span = TrimOws(span);

            if (span.IsEmpty)
                return false;

            if (TryReadAttribute(span, out var name, out var attrValue))
            {
                if (EqualsIgnoreCase(name, "charset".AsSpan()))
                {
                    charset = TrimOws(attrValue);

                    if (charset.Length >= 2 && charset[0] == '"' && charset[^1] == '"')
                        charset = charset[1..^1];

                    return !charset.IsEmpty;
                }
            }
        }
    }

    private static bool TryReadAttribute(ReadOnlySpan<char> value, out ReadOnlySpan<char> name, out ReadOnlySpan<char> attrValue)
    {
        var eq = value.IndexOf('=');

        if (eq < 0)
        {
            name = TrimOws(value);
            attrValue = ReadOnlySpan<char>.Empty;

            return false;
        }

        name = TrimOws(value[..eq]);
        attrValue = TrimOws(value[(eq + 1)..]);

        return !name.IsEmpty;
    }

    private static bool EqualsIgnoreCase(ReadOnlySpan<char> left, ReadOnlySpan<char> right)
    {
        if (left.Length != right.Length)
            return false;

        for (var i = 0; i < left.Length; i++)
        {
            var a = left[i];
            var b = right[i];

            if (a == b)
                continue;

            if (a >= 'A' && a <= 'Z')
                a = (char)(a + 32);

            if (b >= 'A' && b <= 'Z')
                b = (char)(b + 32);

            if (a != b)
                return false;
        }

        return true;
    }

    private static bool IsWhitespace(byte c) => c is (byte)' ' or (byte)'\t';
    private static bool IsWhitespace(char c) => c is ' ' or '\t';

    private static int ParseHex(byte value) => value switch
    {
        >= (byte)'0' and <= (byte)'9' => value - (byte)'0',
        >= (byte)'a' and <= (byte)'f' => value - (byte)'a' + 10,
        >= (byte)'A' and <= (byte)'F' => value - (byte)'A' + 10,
        _ => -1,
    };
}
