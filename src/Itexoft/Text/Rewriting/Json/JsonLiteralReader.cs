// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Text.Rewriting.Json;

internal static class JsonLiteralReader
{
    internal static int ReadLiteral(ReadOnlySpan<char> json, int start)
    {
        var i = start;

        while (i < json.Length)
        {
            var ch = json[i];

            if (ch == ',' || ch == '}' || ch == ']' || char.IsWhiteSpace(ch))
                break;

            i++;
        }

        return i - start;
    }
}
