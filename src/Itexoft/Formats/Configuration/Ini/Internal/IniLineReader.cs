// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Formats.Configuration.Ini;

internal ref struct IniLineReader
{
    private ReadOnlySpan<char> remaining;
    private int offset;
    private int lineNumber;

    public IniLineReader(ReadOnlySpan<char> source)
    {
        this.remaining = source;
        this.offset = 0;
        this.lineNumber = 0;
    }

    public bool TryReadLine(out IniLine line)
    {
        if (this.remaining.IsEmpty)
        {
            line = default;
            return false;
        }

        var span = this.remaining;
        var end = -1;

        for (var i = 0; i < span.Length; i++)
        {
            var ch = span[i];
            if (ch is '\r' or '\n')
            {
                end = i;
                break;
            }
        }

        if (end < 0)
        {
            line = new IniLine(span, this.offset, ++this.lineNumber);
            this.remaining = ReadOnlySpan<char>.Empty;
            this.offset += span.Length;
            return true;
        }

        var lineSpan = span[..end];
        var advance = 1;

        if (span[end] == '\r' && end + 1 < span.Length && span[end + 1] == '\n')
            advance = 2;

        line = new IniLine(lineSpan, this.offset, ++this.lineNumber);
        this.remaining = span[(end + advance)..];
        this.offset += end + advance;

        return true;
    }
}

internal readonly ref struct IniLine
{
    public IniLine(ReadOnlySpan<char> span, int start, int lineNumber)
    {
        this.Span = span;
        this.Start = start;
        this.LineNumber = lineNumber;
    }

    public ReadOnlySpan<char> Span { get; }

    public int Start { get; }

    public int LineNumber { get; }
}
