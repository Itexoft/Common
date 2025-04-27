// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Extensions;

namespace Itexoft.TerminalKit;

internal sealed class TerminalBuildContext
{
    private readonly Dictionary<string, TerminalNode> nodes = new(StringComparer.Ordinal);

    public void Register(TerminalNode node)
    {
        node.Required();
        this.nodes[node.Id] = node;
    }

    public TerminalNode Resolve(string id)
    {
        if (!this.nodes.TryGetValue(id, out var node))
            throw new InvalidOperationException($"Component node '{id}' is not part of the current UI tree.");

        return node;
    }
}
