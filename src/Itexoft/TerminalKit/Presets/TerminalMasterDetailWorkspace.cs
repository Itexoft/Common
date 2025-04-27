// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Extensions;
using Itexoft.TerminalKit.Binding;
using Itexoft.TerminalKit.Dsl;

namespace Itexoft.TerminalKit.Presets;

/// <summary>
/// Workspace that renders a master list alongside a detail table using the DSL.
/// </summary>
public sealed class TerminalMasterDetailWorkspace<TMaster, TDetail>
{
    private readonly Func<TMaster, IEnumerable<TDetail>> detailProvider;
    private readonly TerminalObservableBindingList<TDetail> details = [];
    private readonly TerminalViewportState detailViewport = new();
    private readonly TerminalWorkspace<TMaster> master;

    /// <summary>
    /// Initializes the workspace with an existing master CRUD workspace and a detail projection delegate.
    /// </summary>
    public TerminalMasterDetailWorkspace(TerminalWorkspace<TMaster> masterWorkspace, Func<TMaster, IEnumerable<TDetail>> detailProvider)
    {
        this.master = masterWorkspace ?? throw new ArgumentNullException(nameof(masterWorkspace));
        this.detailProvider = detailProvider ?? throw new ArgumentNullException(nameof(detailProvider));
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
        detailTable.Required();

        this.SynchronizeDetails();

        var scene = TerminalScene<TerminalScreen>.Create().WithState(this.MasterItemsKey, this.master.Items.Items)
            .WithState(this.MasterSelectionKey, this.master.Selection).WithState(this.MasterViewportKey, this.master.Viewport)
            .WithState(this.DetailItemsKey, this.details.Items).WithState(this.DetailViewportKey, this.detailViewport).Compose(composer =>
            {
                composer.CommandBar(bar => bar.Counter(() => $"Master items: {this.master.Items.Count}"));
                composer.List(this.MasterItemsKey, this.MasterViewportKey, list => { list.EmptyText("No master items"); });
                composer.Table(this.DetailItemsKey, this.DetailViewportKey, detailTable);
                extraCompose?.Invoke(composer);
            });

        return scene.Build();
    }

    private void SynchronizeDetails()
    {
        this.details.Clear();

        if (this.master.TryGetSelectedItem(out var selected))
        {
            var details = this.detailProvider(selected);

            if (details == null)
                return;

            foreach (var detail in details)
                this.details.Add(detail);
        }
    }
}
