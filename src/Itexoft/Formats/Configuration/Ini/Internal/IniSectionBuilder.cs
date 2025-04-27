// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Formats.Configuration.Ini;

internal sealed class IniSectionBuilder
{
    private readonly List<IniEntry> entries = [];
    private readonly StringComparer keyComparer;

    public IniSectionBuilder(string name, ReadOnlyMemory<char> nameMemory, bool isGlobal, StringComparer keyComparer)
    {
        this.Name = name;
        this.NameMemory = nameMemory;
        this.IsGlobal = isGlobal;
        this.keyComparer = keyComparer;
    }

    public string Name { get; }

    public ReadOnlyMemory<char> NameMemory { get; }

    public bool IsGlobal { get; }

    public void AddEntry(IniEntry entry) => this.entries.Add(entry);

    public IniSection Build() => new(this.Name, this.NameMemory, this.IsGlobal, this.entries, this.keyComparer);
}
