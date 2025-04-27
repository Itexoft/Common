// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Formats.Configuration.Ini;

public sealed class IniReaderOptions
{
    public static IniReaderOptions Default { get; } = new();

    public StringComparer SectionNameComparer { get; init; } = StringComparer.OrdinalIgnoreCase;

    public StringComparer KeyComparer { get; init; } = StringComparer.OrdinalIgnoreCase;

    public string CommentPrefixes { get; init; } = ";#";

    public bool AllowInlineComments { get; init; } = true;

    public bool AllowEmptyKeys { get; init; } = true;

    public bool AllowEntriesBeforeFirstSection { get; init; } = true;

    public bool MergeDuplicateSections { get; init; } = true;

    public bool TrimWhitespace { get; init; } = true;
}
