// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Formats.Configuration.Ini;

public sealed class IniKeyValueEntry : IniEntry
{
    private readonly IniValueCollection values;

    internal IniKeyValueEntry(IniKey key, IniValue[] values, ReadOnlyMemory<char> rawLine, int lineNumber)
        : base(IniEntryKind.KeyValue, rawLine, lineNumber)
    {
        this.Key = key;
        this.values = values.Length == 0 ? IniValueCollection.Empty : new IniValueCollection(values);
    }

    public IniKey Key { get; }

    public IniValueCollection Values => this.values;

    public IniValue Value => this.values.FirstOrDefault;
}
