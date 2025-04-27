// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Collections;

namespace Itexoft.Formats.Configuration.Ini;

public sealed class IniSectionCollection : IReadOnlyList<IniSection>
{
    private readonly Dictionary<string, IniSection> index;
    private readonly List<IniSection> sections;

    internal IniSectionCollection(List<IniSection> sections, Dictionary<string, IniSection> index)
    {
        this.sections = sections;
        this.index = index;
    }

    public IniSection? this[string name] => this.index.TryGetValue(name, out var section) ? section : null;

    public int Count => this.sections.Count;

    public IniSection this[int index] => this.sections[index];

    public IEnumerator<IniSection> GetEnumerator() => this.sections.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => this.sections.GetEnumerator();

    public bool TryGet(string name, out IniSection section) => this.index.TryGetValue(name, out section!);
}
