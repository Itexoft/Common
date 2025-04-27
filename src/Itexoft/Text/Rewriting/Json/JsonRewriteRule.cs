// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Text.Rewriting.Json;

/// <summary>
/// Base type for JSON rewrite rules.
/// </summary>
public abstract class JsonRewriteRule
{
    internal int RuleId { get; private set; }

    internal string? Group { get; private set; }

    internal abstract string? Pointer { get; }

    internal virtual bool HasAsync => false;

    internal void AssignMetadata(int id, string? group)
    {
        this.RuleId = id;
        this.Group = group;
    }
}
