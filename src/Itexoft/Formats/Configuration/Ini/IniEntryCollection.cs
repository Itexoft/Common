// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Collections;

namespace Itexoft.Formats.Configuration.Ini;

public sealed class IniEntryCollection : IReadOnlyList<IniEntry>
{
    private readonly List<IniEntry> entries;

    internal IniEntryCollection(List<IniEntry> entries) => this.entries = entries;

    public IEnumerable<IniKeyValueEntry> KeyValues
    {
        get
        {
            foreach (var entry in this.entries)
            {
                if (entry is IniKeyValueEntry keyValue)
                    yield return keyValue;
            }
        }
    }

    public IEnumerable<IniValueEntry> Values
    {
        get
        {
            foreach (var entry in this.entries)
            {
                if (entry is IniValueEntry value)
                    yield return value;
            }
        }
    }

    public int Count => this.entries.Count;

    public IniEntry this[int index] => this.entries[index];

    public IEnumerator<IniEntry> GetEnumerator() => this.entries.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => this.entries.GetEnumerator();
}
