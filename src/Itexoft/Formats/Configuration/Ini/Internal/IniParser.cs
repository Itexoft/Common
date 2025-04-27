// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Formats.Configuration.Ini;

internal static class IniParser
{
    public static IniDocument Parse(string text, IniReaderOptions options)
    {
        if (text is null)
            throw new ArgumentNullException(nameof(text));

        var builder = new IniDocumentBuilder(text, options);
        ParseCore(text.AsSpan(), builder, options);

        return builder.Build();
    }

    public static IniDocument Parse(ReadOnlySpan<char> text, IniReaderOptions options)
    {
        var copied = new string(text);
        var builder = new IniDocumentBuilder(copied, options);
        ParseCore(copied.AsSpan(), builder, options);

        return builder.Build();
    }

    private static void ParseCore(ReadOnlySpan<char> text, IniDocumentBuilder builder, IniReaderOptions options)
    {
        var reader = new IniLineReader(text);
        var current = builder.Global;
        var hasSection = false;
        var firstLine = true;

        while (reader.TryReadLine(out var line))
        {
            var span = line.Span;
            var lineOffset = line.Start;

            if (firstLine)
            {
                firstLine = false;

                if (!span.IsEmpty && span[0] == '\uFEFF')
                {
                    span = span[1..];
                    lineOffset += 1;
                }
            }

            var trimmed = IniParsing.TrimOws(span, out var trimStart, out var trimLength);

            if (trimLength == 0)
                continue;

            var trimmedOffset = lineOffset + trimStart;

            if (IniParsing.IsCommentLine(trimmed, options.CommentPrefixes))
                continue;

            var sectionSpan = trimmed;
            var sectionOffset = trimmedOffset;

            if (options.AllowInlineComments)
            {
                var commentIndex = IniParsing.FindInlineComment(sectionSpan, options.CommentPrefixes);

                if (commentIndex >= 0)
                    sectionSpan = sectionSpan[..commentIndex];
            }

            if (options.TrimWhitespace)
            {
                sectionSpan = IniParsing.TrimOws(sectionSpan, out var sectionTrimStart, out var sectionTrimLength);
                sectionOffset += sectionTrimStart;
            }

            if (sectionSpan.Length > 0 && IniParsing.TryParseSectionHeader(sectionSpan, out var nameStart, out var nameLength))
            {
                if (nameLength == 0)
                    throw new IniFormatException("Section name is empty.", line.LineNumber, builder.Slice(lineOffset, span.Length));

                var nameOffset = sectionOffset + nameStart;
                current = builder.GetOrCreateSection(builder.Slice(nameOffset, nameLength));
                hasSection = true;

                continue;
            }

            if (!hasSection && !options.AllowEntriesBeforeFirstSection)
                throw new IniFormatException("Entry appears before any section header.", line.LineNumber, builder.Slice(lineOffset, span.Length));

            ParseEntry(current, trimmed, trimmedOffset, line.LineNumber, builder, options, span, lineOffset);
        }
    }

    private static void ParseEntry(
        IniSectionBuilder current,
        ReadOnlySpan<char> lineSpan,
        int lineOffset,
        int lineNumber,
        IniDocumentBuilder builder,
        IniReaderOptions options,
        ReadOnlySpan<char> rawLine,
        int rawLineOffset)
    {
        var separator = lineSpan.IndexOf('=');

        if (separator >= 0)
        {
            var keySpan = lineSpan[..separator];
            var valueSpan = lineSpan[(separator + 1)..];

            var keyStart = 0;
            var keyLength = keySpan.Length;

            if (options.TrimWhitespace)
                IniParsing.TrimOws(keySpan, out keyStart, out keyLength);

            if (keyLength == 0 && !options.AllowEmptyKeys)
                throw new IniFormatException("Key is empty.", lineNumber, builder.Slice(rawLineOffset, rawLine.Length));

            var keyOffset = lineOffset + keyStart;
            var key = new IniKey(builder.Slice(keyOffset, keyLength));
            var parsedValue = ParseSingleValue(valueSpan, lineOffset + separator + 1, builder, options);

            current.AddEntry(new IniKeyValueEntry(key, [parsedValue], builder.Slice(rawLineOffset, rawLine.Length), lineNumber));

            return;
        }

        var value = ParseSingleValue(lineSpan, lineOffset, builder, options);
        current.AddEntry(new IniValueEntry(value, builder.Slice(rawLineOffset, rawLine.Length), lineNumber));
    }

    private static IniValue ParseSingleValue(ReadOnlySpan<char> span, int offset, IniDocumentBuilder builder, IniReaderOptions options)
    {
        var start = 0;
        var length = span.Length;

        if (options.TrimWhitespace)
            IniParsing.TrimOws(span, out start, out length);

        var trimmed = span.Slice(start, length);
        var absolute = offset + start;

        if (options.AllowInlineComments)
        {
            var commentIndex = IniParsing.FindInlineComment(trimmed, options.CommentPrefixes);

            if (commentIndex >= 0)
            {
                trimmed = trimmed[..commentIndex];
                length = trimmed.Length;
            }
        }

        if (options.TrimWhitespace)
        {
            var trimmedSpan = IniParsing.TrimOws(trimmed, out var valueStart, out var valueLength);
            trimmed = trimmedSpan;
            absolute += valueStart;
            length = valueLength;
        }

        if (length >= 2 && trimmed[0] == '\"' && trimmed[^1] == '\"')
        {
            absolute += 1;
            length -= 2;
        }

        if (length == 0)
            return new IniValue(ReadOnlyMemory<char>.Empty);

        return new IniValue(builder.Slice(absolute, length));
    }
}
