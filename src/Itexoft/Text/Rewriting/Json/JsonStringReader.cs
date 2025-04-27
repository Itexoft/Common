// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Globalization;
using System.Text;

namespace Itexoft.Text.Rewriting.Json;

internal static class JsonStringReader
{
    internal static bool TryReadString(string json, int start, out string value, out int length)
    {
        var builder = new StringBuilder();
        var i = start + 1;
        var escaped = false;

        while (i < json.Length)
        {
            var ch = json[i];

            if (!escaped)
            {
                if (ch == '\\')
                {
                    escaped = true;
                    i++;

                    continue;
                }

                if (ch == '"')
                {
                    value = builder.ToString();
                    length = i - start + 1;

                    return true;
                }

                builder.Append(ch);
            }
            else
            {
                switch (ch)
                {
                    case '\\':
                        builder.Append('\\');

                        break;
                    case '"':
                        builder.Append('"');

                        break;
                    case 'b':
                        builder.Append('\b');

                        break;
                    case 'f':
                        builder.Append('\f');

                        break;
                    case 'n':
                        builder.Append('\n');

                        break;
                    case 'r':
                        builder.Append('\r');

                        break;
                    case 't':
                        builder.Append('\t');

                        break;
                    case 'u':
                        if (i + 4 >= json.Length)
                        {
                            value = string.Empty;
                            length = 0;

                            return false;
                        }

                        var hex = json.AsSpan(i + 1, 4);

                        if (!ushort.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code))
                        {
                            value = string.Empty;
                            length = 0;

                            return false;
                        }

                        builder.Append((char)code);
                        i += 4;

                        break;
                    default:
                        builder.Append(ch);

                        break;
                }

                escaped = false;
            }

            i++;
        }

        value = string.Empty;
        length = 0;

        return false;
    }

    internal static bool TryReadString(ReadOnlySpan<char> literal, out string value)
    {
        var builder = new StringBuilder();
        var escaped = false;

        for (var i = 1; i < literal.Length; i++)
        {
            var ch = literal[i];

            if (!escaped)
            {
                if (ch == '\\')
                {
                    escaped = true;

                    continue;
                }

                if (ch == '"')
                {
                    if (i != literal.Length - 1)
                        break;

                    value = builder.ToString();

                    return true;
                }

                builder.Append(ch);
            }
            else
            {
                switch (ch)
                {
                    case '\\':
                        builder.Append('\\');

                        break;
                    case '"':
                        builder.Append('"');

                        break;
                    case 'b':
                        builder.Append('\b');

                        break;
                    case 'f':
                        builder.Append('\f');

                        break;
                    case 'n':
                        builder.Append('\n');

                        break;
                    case 'r':
                        builder.Append('\r');

                        break;
                    case 't':
                        builder.Append('\t');

                        break;
                    case 'u':
                        if (i + 4 >= literal.Length)
                        {
                            value = string.Empty;

                            return false;
                        }

                        var hex = literal.Slice(i + 1, 4);

                        if (!ushort.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code))
                        {
                            value = string.Empty;

                            return false;
                        }

                        builder.Append((char)code);
                        i += 4;

                        break;
                    default:
                        builder.Append(ch);

                        break;
                }

                escaped = false;
            }
        }

        value = string.Empty;

        return false;
    }
}
