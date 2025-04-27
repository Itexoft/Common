// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Formats.Configuration.Ini;

internal sealed class IniDocumentBuilder(string source, IniReaderOptions options)
{
    private readonly Dictionary<string, IniSectionBuilder> sectionIndex = new(options.SectionNameComparer);
    private readonly List<IniSectionBuilder> sections = [];

    public IniSectionBuilder Global { get; } = new(string.Empty, ReadOnlyMemory<char>.Empty, true, options.KeyComparer);

    public ReadOnlyMemory<char> Slice(int start, int length) =>
        length <= 0 ? ReadOnlyMemory<char>.Empty : source.AsMemory(start, length);

    public IniSectionBuilder GetOrCreateSection(ReadOnlyMemory<char> nameMemory)
    {
        var nameText = nameMemory.ToString();

        if (this.sectionIndex.TryGetValue(nameText, out var existing))
        {
            if (options.MergeDuplicateSections)
                return existing;

            var duplicate = new IniSectionBuilder(nameText, nameMemory, false, options.KeyComparer);
            this.sections.Add(duplicate);

            return duplicate;
        }

        var section = new IniSectionBuilder(nameText, nameMemory, false, options.KeyComparer);
        this.sectionIndex[nameText] = section;
        this.sections.Add(section);

        return section;
    }

    public IniDocument Build()
    {
        var builtSections = new List<IniSection>(this.sections.Count);
        var builtIndex = new Dictionary<string, IniSection>(options.SectionNameComparer);

        foreach (var builder in this.sections)
        {
            var section = builder.Build();
            builtSections.Add(section);

            if (!builtIndex.ContainsKey(section.Name))
                builtIndex[section.Name] = section;
        }

        var globalSection = this.Global.Build();

        return new IniDocument(source, globalSection, new IniSectionCollection(builtSections, builtIndex), options);
    }
}
