// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Formats.Configuration.Ini;

public sealed class IniValueEntry : IniEntry
{
    internal IniValueEntry(IniValue value, ReadOnlyMemory<char> rawLine, int lineNumber) : base(IniEntryKind.Value, rawLine, lineNumber) =>
        this.Value = value;

    public IniValue Value { get; }
}
