// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Text.Rewriting.Json.Internal.Rules;

internal sealed class RenamePropertyRule : JsonRewriteRule
{
    internal RenamePropertyRule(string pointer, string newName)
    {
        this.Pointer = pointer ?? throw new ArgumentNullException(nameof(pointer));
        this.NewName = newName ?? throw new ArgumentNullException(nameof(newName));
    }

    internal override string Pointer { get; }

    internal string NewName { get; }
}
