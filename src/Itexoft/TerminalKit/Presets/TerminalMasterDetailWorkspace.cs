// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.TerminalKit.Binding;
using Itexoft.TerminalKit.Dsl;

namespace Itexoft.TerminalKit.Presets;

/// <summary>
/// Workspace that renders a master list alongside a detail table using the DSL.
/// </summary>
public sealed class TerminalMasterDetailWorkspace<TMaster, TDetail>
{
    private readonly Func<TMaster, IEnumerable<TDetail>> _detailProvider;
    private readonly TerminalObservableBindingList<TDetail> _details = [];
    private readonly TerminalViewportState _detailViewport = new();
    private readonly TerminalWorkspace<TMaster> _master;

    /// <summary>
    /// Initializes the workspace with an existing master CRUD workspace and a detail projection delegate.
    /// </summary>
    public TerminalMasterDetailWorkspace(
        TerminalWorkspace<TMaster> masterWorkspace,
        Func<TMaster, IEnumerable<TDetail>> detailProvider)
    {
        this._master = masterWorkspace ?? throw new ArgumentNullException(nameof(masterWorkspace));
        this._detailProvider = detailProvider ?? throw new ArgumentNullException(nameof(detailProvider));
    }

    /// <summary>
    /// Gets the state key referencing master items.
    /// </summary>
    public TerminalStateKey MasterItemsKey { get; init; } = TerminalStateKey.From("master.items");

    /// <summary>
    /// Gets the state key referencing master selection metadata.
    /// </summary>
    public TerminalStateKey MasterSelectionKey { get; init; } = TerminalStateKey.From("master.selection");

    /// <summary>
    /// Gets the state key referencing the master viewport metadata.
    /// </summary>
    public TerminalStateKey MasterViewportKey { get; init; } = TerminalStateKey.From("master.viewport");

    /// <summary>
    /// Gets the state key referencing detail items.
    /// </summary>
    public TerminalStateKey DetailItemsKey { get; init; } = TerminalStateKey.From("detail.items");

    /// <summary>
    /// Gets the state key referencing the detail viewport metadata.
    /// </summary>
    public TerminalStateKey DetailViewportKey { get; init; } = TerminalStateKey.From("detail.viewport");

    /// <summary>
    /// Builds a master-detail snapshot with customizable detail table and extra composition steps.
    /// </summary>
    /// <param name="detailTable">Configures the detail table columns and styling.</param>
    /// <param name="extraCompose">Optional callback for additional scene composition.</param>
    public TerminalSnapshot BuildSnapshot(
        Action<TerminalTableComposer> detailTable,
        Action<TerminalSceneComposer<TerminalScreen>>? extraCompose = null)
    {
        if (detailTable == null)
            throw new ArgumentNullException(nameof(detailTable));

        this.SynchronizeDetails();

        var scene = TerminalScene<TerminalScreen>.Create()
            .WithState(this.MasterItemsKey, this._master.Items.Items)
            .WithState(this.MasterSelectionKey, this._master.Selection)
            .WithState(this.MasterViewportKey, this._master.Viewport)
            .WithState(this.DetailItemsKey, this._details.Items)
            .WithState(this.DetailViewportKey, this._detailViewport)
            .Compose(composer =>
            {
                composer.CommandBar(bar => bar.Counter(() => $"Master items: {this._master.Items.Count}"));
                composer.List(
                    this.MasterItemsKey,
                    this.MasterViewportKey,
                    list => { list.EmptyText("No master items"); });
                composer.Table(this.DetailItemsKey, this.DetailViewportKey, detailTable);
                extraCompose?.Invoke(composer);
            });

        return scene.Build();
    }

    private void SynchronizeDetails()
    {
        this._details.Clear();
        if (this._master.TryGetSelectedItem(out var selected))
        {
            var details = this._detailProvider(selected);

            if (details == null)
                return;

            foreach (var detail in details)
                this._details.Add(detail);
        }
    }
}