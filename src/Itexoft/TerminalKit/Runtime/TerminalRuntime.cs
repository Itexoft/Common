// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.TerminalKit;

/// <summary>
/// Provides runtime lookup helpers for resolving state slices and component nodes by their handles.
/// </summary>
internal sealed class TerminalRuntime
{
    private readonly Dictionary<string, TerminalNode> nodeIndex = new(StringComparer.Ordinal);
    private readonly Dictionary<string, object?> stateIndex = new(StringComparer.Ordinal);

    public TerminalRuntime(TerminalSnapshot snapshot)
    {
        this.Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        this.IndexNodes(this.Snapshot.Root);
        this.IndexState(this.Snapshot.State);
    }

    public TerminalSnapshot Snapshot { get; }

    public TerminalNode? FindNode(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;

        return this.nodeIndex.TryGetValue(id, out var node) ? node : null;
    }

    public bool TryResolve<TComponent>(TerminalComponentHandle<TComponent> handle, out TerminalNode? node)
        where TComponent : TerminalComponentDefinition
    {
        if (string.IsNullOrWhiteSpace(handle.Id))
        {
            node = null;

            return false;
        }

        return this.nodeIndex.TryGetValue(handle.Id, out node);
    }

    public bool TryResolveState<TState>(StateHandle<TState> handle, out TState state)
    {
        if (this.stateIndex.TryGetValue(handle.Name, out var value) && value is TState typed)
        {
            state = typed;

            return true;
        }

        state = default!;

        return false;
    }

    public object? GetStateSlice(string name) => this.stateIndex.TryGetValue(name, out var value) ? value : null;

    public static bool TryGetStateName(string? bindingExpression, out string stateName)
    {
        stateName = string.Empty;

        if (string.IsNullOrWhiteSpace(bindingExpression))
            return false;

        const string prefix = "@state.";

        if (!bindingExpression.StartsWith(prefix, StringComparison.Ordinal))
            return false;

        stateName = bindingExpression[prefix.Length..];

        return !string.IsNullOrWhiteSpace(stateName);
    }

    private void IndexNodes(TerminalNode node)
    {
        this.nodeIndex[node.Id] = node;

        foreach (var child in node.Children)
            this.IndexNodes(child);
    }

    private void IndexState(IReadOnlyList<TerminalStateSlice> slices)
    {
        foreach (var slice in slices)
            this.stateIndex[slice.Name] = slice.Value;
    }
}
