// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Formats.Configuration.Ini;

internal static class IniParsing
{
    public static ReadOnlySpan<char> TrimOws(ReadOnlySpan<char> span, out int start, out int length)
    {
        start = 0;
        var end = span.Length;

        while (start < end && IsWhitespace(span[start]))
            start++;

        while (end > start && IsWhitespace(span[end - 1]))
            end--;

        length = end - start;

        return span.Slice(start, length);
    }

    public static bool IsCommentLine(ReadOnlySpan<char> span, string commentPrefixes) =>
        !span.IsEmpty && IsCommentPrefix(span[0], commentPrefixes);

    public static bool TryParseSectionHeader(ReadOnlySpan<char> span, out int nameStart, out int nameLength)
    {
        nameStart = 0;
        nameLength = 0;

        if (span.Length < 2 || span[0] != '[' || span[^1] != ']')
            return false;

        var inner = span[1..^1];
        TrimOws(inner, out nameStart, out nameLength);
        nameStart += 1;

        return true;
    }

    public static int FindInlineComment(ReadOnlySpan<char> span, string commentPrefixes)
    {
        var inQuotes = false;

        for (var i = 0; i < span.Length; i++)
        {
            var ch = span[i];

            if (ch == '"')
            {
                inQuotes = !inQuotes;

                continue;
            }

            if (!inQuotes && IsCommentPrefix(ch, commentPrefixes) && i > 0 && IsWhitespace(span[i - 1]))
                return i;
        }

        return -1;
    }

    public static bool IsCommentPrefix(char value, string prefixes) =>
        !string.IsNullOrEmpty(prefixes) && prefixes.IndexOf(value) >= 0;

    public static bool IsWhitespace(char value) => value is ' ' or '\t';
}
