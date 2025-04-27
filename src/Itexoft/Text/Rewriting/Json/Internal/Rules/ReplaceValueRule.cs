// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Text.Rewriting.Json.Internal.Rules;

internal sealed class ReplaceValueRule : JsonRewriteRule
{
    internal ReplaceValueRule(string pointer, string replacement)
    {
        this.Pointer = pointer ?? throw new ArgumentNullException(nameof(pointer));
        this.Replacement = replacement ?? string.Empty;
    }

    internal override string Pointer { get; }

    internal string Replacement { get; }
}
