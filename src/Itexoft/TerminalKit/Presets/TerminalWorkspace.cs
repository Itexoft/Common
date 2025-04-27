// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.TerminalKit.Binding;

namespace Itexoft.TerminalKit.Presets;

/// <summary>
/// High-level helper that combines a CRUD preset builder, observable items, and navigation state.
/// </summary>
public sealed class TerminalWorkspace<TItem>
{
    private readonly Dictionary<string, object?> _additionalState = new(StringComparer.Ordinal);
    private readonly TerminalCrudScreenBuilder _screenBuilder;
    private readonly Func<TItem, object?> _selectionKeySelector;
    private readonly TerminalScrollableWindowController _windowController;

    /// <summary>
    /// Initializes a workspace with optional seed data and customization hooks.
    /// </summary>
    /// <param name="items">Initial items to populate the workspace.</param>
    /// <param name="options">Optional preset options shared with the screen builder.</param>
    /// <param name="actions">Optional action identifiers shared with the screen builder.</param>
    /// <param name="handlers">Optional handler identifiers shared with the screen builder.</param>
    /// <param name="selectionKeySelector">Selector that extracts a stable selection key for metadata consumers.</param>
    public TerminalWorkspace(
        IEnumerable<TItem>? items = null,
        TerminalCrudOptions? options = null,
        TerminalCrudActions? actions = null,
        TerminalCrudHandlers? handlers = null,
        Func<TItem, object?>? selectionKeySelector = null)
    {
        this.Options = options ?? new TerminalCrudOptions();
        this.Items = new((items ?? []).ToList());
        this.Selection = new();
        this.Viewport = new();
        this._selectionKeySelector = selectionKeySelector ?? (item => item);
        this._screenBuilder = new(this.Options, actions, handlers);
        this._windowController = new(this.Viewport, () => this.Items.Count);

        this.Items.ListChanged += this.OnListChanged;
        this.SyncSelection();
    }

    /// <summary>
    /// Gets the options shared with the underlying CRUD screen builder.
    /// </summary>
    public TerminalCrudOptions Options { get; }

    /// <summary>
    /// Gets the observable collection of items bound to list/table components.
    /// </summary>
    public TerminalObservableBindingList<TItem> Items { get; }

    /// <summary>
    /// Gets the selection metadata exposed to preset consumers.
    /// </summary>
    public TerminalSelectionState Selection { get; }

    /// <summary>
    /// Gets the viewport state used by scrollable components.
    /// </summary>
    public TerminalViewportState Viewport { get; }

    /// <summary>
    /// Gets the screen builder configured for this workspace.
    /// </summary>
    public TerminalCrudScreenBuilder ScreenBuilder => this._screenBuilder;

    /// <summary>
    /// Gets a read-only view of additional state slices contributed to the screen.
    /// </summary>
    public IReadOnlyDictionary<string, object?> AdditionalState => this._additionalState;

    /// <summary>
    /// Gets the zero-based index of the currently selected item.
    /// </summary>
    public int SelectedIndex => this._windowController.Selection;

    /// <summary>
    /// Registers an extra state slice that will be published when building a snapshot.
    /// </summary>
    /// <param name="name">Logical name used by bindings.</param>
    /// <param name="state">State object to expose.</param>
    public void SetAdditionalState(string name, object? state)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("State name cannot be empty.", nameof(name));

        this._additionalState[name] = state;
    }

    /// <summary>
    /// Attempts to retrieve the currently selected item.
    /// </summary>
    /// <param name="item">Selected item when available.</param>
    /// <returns><c>true</c> when a valid selection exists.</returns>
    public bool TryGetSelectedItem(out TItem? item)
    {
        var index = this._windowController.Selection;
        if (index < 0 || index >= this.Items.Count)
        {
            item = default;

            return false;
        }

        item = this.Items[index];

        return true;
    }

    /// <summary>
    /// Moves selection to the specified index and keeps the viewport in sync.
    /// </summary>
    /// <param name="index">Zero-based index to select.</param>
    public void SelectByIndex(int index)
    {
        this._windowController.MoveTo(index);
        this.SyncSelectionState();
    }

    /// <summary>
    /// Builds a snapshot using the current state and the configured builder.
    /// </summary>
    /// <param name="configure">Optional customization hook executed before building the snapshot.</param>
    public TerminalSnapshot BuildSnapshot(Action<TerminalCrudScreenBuilder>? configure = null)
    {
        configure?.Invoke(this._screenBuilder);

        return this._screenBuilder.Build(this.BuildState());
    }

    /// <summary>
    /// Creates a default binding map that wires navigation keys to the workspace viewport.
    /// </summary>
    internal TerminalKeyBindingMap BuildBindings() => new TerminalKeyBindingMap().WithScrollableWindow(this._windowController);

    private TerminalCrudScreenState BuildState() => new()
    {
        Items = this.Items.Items,
        Selection = this.Selection,
        Viewport = this.Viewport,
        Additional = new Dictionary<string, object?>(this._additionalState, StringComparer.Ordinal)
    };

    private void OnListChanged(object? sender, TerminalBindingListChangedEventArgs<TItem> e) => this.SyncSelection();

    private void SyncSelection()
    {
        this._windowController.Sync();
        this.SyncSelectionState();
    }

    private void SyncSelectionState()
    {
        if (!this.TryGetSelectedItem(out var selected))
        {
            this.Selection.Clear();

            return;
        }

        this.Selection.ActiveItemId = selected is null ? null : this._selectionKeySelector(selected);
    }
}