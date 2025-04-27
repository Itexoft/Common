// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Text.Json;
using Itexoft.Extensions;

namespace Itexoft.TerminalKit;

/// <summary>
/// Entry point for composing strongly typed console UI snapshots.
/// </summary>
internal sealed class TerminalUiBuilder<TScreen> where TScreen : TerminalComponentDefinition
{
    private readonly TerminalBuildContext context = new();
    private readonly List<TerminalInputBinding> input = [];
    private readonly TerminalNode root;
    private readonly List<TerminalStateSlice> state = [];

    private TerminalUiBuilder()
    {
        this.root = TerminalNode.Create(typeof(TScreen));
        this.context.Register(this.root);
    }

    public static TerminalUiBuilder<TScreen> Create() => new();

    public TerminalUiBuilder<TScreen> Configure(Action<TerminalComponentBuilder<TScreen>> configure)
    {
        configure.Required();
        configure(new(this.root, this.context));

        return this;
    }

    public StateHandle<TState> WithState<TState>(string name, TState state)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("State name cannot be empty.", nameof(name));

        this.state.RemoveAll(slice => string.Equals(slice.Name, name, StringComparison.Ordinal));
        this.state.Add(new(name, state));

        return new(name);
    }

    public TerminalUiBuilder<TScreen> BindInput(TerminalNavigationMode mode, string gesture, string action)
    {
        this.input.Add(new(mode, gesture, action));

        return this;
    }

    public TerminalUiBuilder<TScreen> BindInput(TerminalNavigationMode mode, string gesture, TerminalActionId action) =>
        this.BindInput(mode, gesture, action.Name);

    public TerminalUiBuilder<TScreen> BindEvent<TComponent>(TerminalComponentHandle<TComponent> handle, TerminalEventKey eventKey, string handler)
        where TComponent : TerminalComponentDefinition
    {
        if (string.IsNullOrWhiteSpace(handle.Id))
            throw new ArgumentException("Handle must reference a component id.", nameof(handle));

        var node = this.context.Resolve(handle.Id);
        node.AddEvent(new(eventKey, handler));

        return this;
    }

    public TerminalSnapshot BuildSnapshot() => new(this.root, this.state.ToArray(), this.input.ToArray());

    public string ToJson(JsonSerializerOptions? options = null) => this.BuildSnapshot().ToJson(options);
}
