// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.TerminalKit;

internal sealed class TerminalBuildContext
{
    private readonly Dictionary<string, TerminalNode> _nodes = new(StringComparer.Ordinal);

    public void Register(TerminalNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        this._nodes[node.Id] = node;
    }

    public TerminalNode Resolve(string id)
    {
        if (!this._nodes.TryGetValue(id, out var node))
            throw new InvalidOperationException($"Component node '{id}' is not part of the current UI tree.");

        return node;
    }
}