// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.TerminalKit.Dsl;

/// <summary>
/// Friendly facade over <see cref="TerminalUiBuilder{TScreen}" />.
/// </summary>
public sealed class TerminalScene<TScreen>
    where TScreen : TerminalComponentDefinition
{
    private readonly List<Action<TerminalSceneComposer<TScreen>>> _layoutBlocks = [];
    private readonly Dictionary<string, StateHandle<object>> _stateHandles = new(StringComparer.Ordinal);
    private Action<TerminalUiBuilder<TScreen>>? _customization;

    private TerminalScene() { }

    internal TerminalUiBuilder<TScreen> LowLevelBuilder { get; } = TerminalUiBuilder<TScreen>.Create();

    /// <summary>
    /// Creates a new scene builder for the specified screen definition.
    /// </summary>
    public static TerminalScene<TScreen> Create() => new();

    /// <summary>
    /// Adds an immutable state slice that can be referenced by composers.
    /// </summary>
    public TerminalScene<TScreen> WithState<TState>(TerminalStateKey key, TState value)
    {
        if (string.IsNullOrWhiteSpace(key.Name))
            throw new ArgumentException("State key cannot be empty.", nameof(key));

        var handle = this.LowLevelBuilder.WithState(key.Name, value);
        this._stateHandles[key.Name] = new(handle.Name);

        return this;
    }

    /// <summary>
    /// Adds a block that wires child components into the scene.
    /// </summary>
    public TerminalScene<TScreen> Compose(Action<TerminalSceneComposer<TScreen>> composer)
    {
        ArgumentNullException.ThrowIfNull(composer);
        this._layoutBlocks.Add(composer);

        return this;
    }

    /// <summary>
    /// Allows injecting low-level builder configuration while still using the friendly facade.
    /// </summary>
    public TerminalScene<TScreen> Custom(Action<TerminalComponentComposer<TScreen>> customize)
    {
        ArgumentNullException.ThrowIfNull(customize);
        this._customization += builder => { builder.Configure(root => customize(new(root))); };

        return this;
    }

    /// <summary>
    /// Finalizes the builder and produces an immutable snapshot describable by the renderer.
    /// </summary>
    public TerminalSnapshot Build()
    {
        this.LowLevelBuilder.Configure(root =>
        {
            var composer = new TerminalSceneComposer<TScreen>(root, this);
            foreach (var block in this._layoutBlocks)
                block(composer);
        });

        this._customization?.Invoke(this.LowLevelBuilder);

        return this.LowLevelBuilder.BuildSnapshot();
    }

    internal StateHandle<object> GetStateHandle(TerminalStateKey key)
    {
        if (!this._stateHandles.TryGetValue(key.Name, out var handle))
            throw new InvalidOperationException($"State '{key.Name}' was not registered in the scene.");

        return handle;
    }

    /// <summary>
    /// Resolves a strongly typed handle for the specified state key.
    /// </summary>
    /// <typeparam name="TState">Type stored inside the state slice.</typeparam>
    /// <param name="key">Key that was registered earlier via <see cref="WithState{TState}" />.</param>
    public StateHandle<TState> ResolveState<TState>(TerminalStateKey key)
    {
        var handle = this.GetStateHandle(key);

        return new(handle.Name);
    }
}