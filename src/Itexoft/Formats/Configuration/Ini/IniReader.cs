// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.IO;

namespace Itexoft.Formats.Configuration.Ini;

public sealed class IniReader
{
    public IniReader(IniReaderOptions? options = null) =>
        this.Options = options ?? IniReaderOptions.Default;

    public IniReaderOptions Options { get; }

    public IniDocument Parse(string text) => IniParser.Parse(text, this.Options);

    public IniDocument Parse(ReadOnlySpan<char> text) => IniParser.Parse(text, this.Options);

    public IniDocument ParseFile(string path) => this.Parse(File.ReadAllText(path));
}
