// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Formats.Configuration.Ini;

internal sealed class IniSectionBuilder(string name, ReadOnlyMemory<char> nameMemory, bool isGlobal, StringComparer keyComparer)
{
    private readonly List<IniEntry> entries = [];

    public string Name { get; } = name;

    public ReadOnlyMemory<char> NameMemory { get; } = nameMemory;

    public bool IsGlobal { get; } = isGlobal;

    public void AddEntry(IniEntry entry) => this.entries.Add(entry);

    public IniSection Build() => new(this.Name, this.NameMemory, this.IsGlobal, this.entries, keyComparer);
}
