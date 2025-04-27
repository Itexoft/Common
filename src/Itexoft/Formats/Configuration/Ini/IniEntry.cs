// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Formats.Configuration.Ini;

public abstract class IniEntry
{
    internal IniEntry(IniEntryKind kind, ReadOnlyMemory<char> rawLine, int lineNumber)
    {
        this.Kind = kind;
        this.RawLine = rawLine;
        this.LineNumber = lineNumber;
    }

    public IniEntryKind Kind { get; }

    public ReadOnlyMemory<char> RawLine { get; }

    public ReadOnlySpan<char> RawSpan => this.RawLine.Span;

    public int LineNumber { get; }
}

public enum IniEntryKind
{
    Value,
    KeyValue,
}
