// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Formats.Configuration.Ini;

internal sealed class IniDocumentBuilder
{
    private readonly string source;
    private readonly IniReaderOptions options;
    private readonly List<IniSectionBuilder> sections = [];
    private readonly Dictionary<string, IniSectionBuilder> sectionIndex;
    private readonly IniSectionBuilder global;

    public IniDocumentBuilder(string source, IniReaderOptions options)
    {
        this.source = source;
        this.options = options;
        this.sectionIndex = new Dictionary<string, IniSectionBuilder>(options.SectionNameComparer);
        this.global = new IniSectionBuilder(string.Empty, ReadOnlyMemory<char>.Empty, true, options.KeyComparer);
    }

    public IniSectionBuilder Global => this.global;

    public ReadOnlyMemory<char> Slice(int start, int length) =>
        length <= 0 ? ReadOnlyMemory<char>.Empty : this.source.AsMemory(start, length);

    public IniSectionBuilder GetOrCreateSection(ReadOnlyMemory<char> nameMemory)
    {
        var nameText = nameMemory.ToString();

        if (this.sectionIndex.TryGetValue(nameText, out var existing))
        {
            if (this.options.MergeDuplicateSections)
                return existing;

            var duplicate = new IniSectionBuilder(nameText, nameMemory, false, this.options.KeyComparer);
            this.sections.Add(duplicate);
            return duplicate;
        }

        var section = new IniSectionBuilder(nameText, nameMemory, false, this.options.KeyComparer);
        this.sectionIndex[nameText] = section;
        this.sections.Add(section);
        return section;
    }

    public IniDocument Build()
    {
        var builtSections = new List<IniSection>(this.sections.Count);
        var builtIndex = new Dictionary<string, IniSection>(this.options.SectionNameComparer);

        foreach (var builder in this.sections)
        {
            var section = builder.Build();
            builtSections.Add(section);
            if (!builtIndex.ContainsKey(section.Name))
                builtIndex[section.Name] = section;
        }

        var globalSection = this.global.Build();

        return new IniDocument(this.source, globalSection, new IniSectionCollection(builtSections, builtIndex), this.options);
    }
}
