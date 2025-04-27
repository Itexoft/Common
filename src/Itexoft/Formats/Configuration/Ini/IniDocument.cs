// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Formats.Configuration.Ini;

public sealed class IniDocument
{
    internal IniDocument(string source, IniSection global, IniSectionCollection sections, IniReaderOptions options)
    {
        this.Source = source;
        this.Global = global;
        this.Sections = sections;
        this.Options = options;
    }

    public string Source { get; }

    public ReadOnlyMemory<char> SourceMemory => this.Source.AsMemory();

    public IniReaderOptions Options { get; }

    public IniSection Global { get; }

    public IniSectionCollection Sections { get; }

    public static IniDocument Parse(string text, IniReaderOptions? options = null) =>
        IniParser.Parse(text, options ?? IniReaderOptions.Default);

    public static IniDocument Parse(ReadOnlySpan<char> text, IniReaderOptions? options = null) =>
        IniParser.Parse(text, options ?? IniReaderOptions.Default);
}
