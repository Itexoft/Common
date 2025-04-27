// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Extensions;

namespace Itexoft.TerminalKit.Dsl;

/// <summary>
/// High-level DSL entry point that wires top-level components into a console scene.
/// </summary>
public sealed class TerminalSceneComposer<TScreen> where TScreen : TerminalComponentDefinition
{
    private readonly TerminalComponentBuilder<TScreen> root;
    private readonly TerminalScene<TScreen> scene;

    internal TerminalSceneComposer(TerminalComponentBuilder<TScreen> root, TerminalScene<TScreen> scene)
    {
        this.root = root;
        this.scene = scene;
    }

    /// <summary>
    /// Adds a breadcrumb line rendered above the main content.
    /// </summary>
    public TerminalSceneComposer<TScreen> Breadcrumb(Func<string> textFactory)
    {
        textFactory.Required();
        this.root.AddChild<TerminalBreadcrumb>(breadcrumb => { breadcrumb.Set(b => b.Path, textFactory()); });

        return this;
    }

    /// <summary>
    /// Adds a command bar/shortcut block to the scene.
    /// </summary>
    public TerminalSceneComposer<TScreen> CommandBar(Action<TerminalCommandBarComposer> configure)
    {
        configure.Required();

        this.root.AddChild<TerminalPanel>(panel =>
        {
            var composer = new TerminalCommandBarComposer(panel);
            configure(composer);
        });

        return this;
    }

    /// <summary>
    /// Adds a scrollable list view bound to the provided states.
    /// </summary>
    public TerminalSceneComposer<TScreen> List(TerminalStateKey items, TerminalStateKey viewport, Action<TerminalListComposer>? configure = null)
    {
        var itemsHandle = this.scene.GetStateHandle(items);
        var viewportHandle = this.scene.GetStateHandle(viewport);

        this.root.AddChild<TerminalListView>(list =>
        {
            list.BindState(l => l.DataSource, itemsHandle).BindState(l => l.ViewportState, viewportHandle);
            configure?.Invoke(new(list));
        });

        return this;
    }

    /// <summary>
    /// Adds a table view without selection state.
    /// </summary>
    public TerminalSceneComposer<TScreen> Table(TerminalStateKey items, TerminalStateKey viewport, Action<TerminalTableComposer>? configure = null)
    {
        var itemsHandle = this.scene.GetStateHandle(items);
        var viewportHandle = this.scene.GetStateHandle(viewport);

        this.root.AddChild<TerminalTableView>(table =>
        {
            table.BindState(t => t.DataSource, itemsHandle).BindState(t => t.ViewportState, viewportHandle);
            var composer = new TerminalTableComposer(table);
            configure?.Invoke(composer);
            composer.Apply();
        });

        return this;
    }

    /// <summary>
    /// Adds a table view bound to selection state in addition to data and viewport.
    /// </summary>
    public TerminalSceneComposer<TScreen> Table(
        TerminalStateKey items,
        TerminalStateKey viewport,
        TerminalStateKey selection,
        Action<TerminalTableComposer>? configure = null)
    {
        var itemsHandle = this.scene.GetStateHandle(items);
        var viewportHandle = this.scene.GetStateHandle(viewport);
        var selectionHandle = this.scene.GetStateHandle(selection);

        this.root.AddChild<TerminalTableView>(table =>
        {
            table.BindState(t => t.DataSource, itemsHandle).BindState(t => t.ViewportState, viewportHandle);
            var composer = new TerminalTableComposer(table);
            composer.SetSelection(selectionHandle);
            configure?.Invoke(composer);
            composer.Apply();
        });

        return this;
    }

    /// <summary>
    /// Adds a metadata form bound to the selected entity.
    /// </summary>
    public TerminalSceneComposer<TScreen> Form(TerminalStateKey selection, Action<TerminalFormComposer> configure)
    {
        configure.Required();
        var selectionHandle = this.scene.GetStateHandle(selection);

        this.root.AddChild<TerminalMetadataForm>(form =>
        {
            var composer = new TerminalFormComposer(form, selectionHandle);
            configure(composer);
            composer.Apply();
        });

        return this;
    }

    /// <summary>
    /// Provides direct access to the low-level input binding composer.
    /// </summary>
    public TerminalSceneComposer<TScreen> Input(Action<TerminalInputComposer<TScreen>> configure)
    {
        configure.Required();
        configure(new(this.scene.LowLevelBuilder));

        return this;
    }

    /// <summary>
    /// Exposes underlying component handles for hand-crafted customization.
    /// </summary>
    public TerminalSceneComposer<TScreen> Custom(Action<TerminalComponentComposer<TScreen>> configure)
    {
        configure.Required();
        configure(new(this.root));

        return this;
    }
}
