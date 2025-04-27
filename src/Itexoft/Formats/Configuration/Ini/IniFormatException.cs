// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Formats.Configuration.Ini;

public sealed class IniFormatException : FormatException
{
    public IniFormatException(string message, int lineNumber, ReadOnlyMemory<char> line)
        : base(message)
    {
        this.LineNumber = lineNumber;
        this.Line = line;
    }

    public int LineNumber { get; }

    public ReadOnlyMemory<char> Line { get; }
}
