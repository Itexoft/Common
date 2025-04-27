// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.TerminalKit.Dsl;

/// <summary>
/// Configures scrollable list components inside a scene.
/// </summary>
public sealed class TerminalListComposer
{
    private readonly TerminalComponentBuilder<TerminalListView> _builder;

    internal TerminalListComposer(TerminalComponentBuilder<TerminalListView> builder) => this._builder = builder;

    /// <summary>
    /// Gets the handle pointing to the list view component.
    /// </summary>
    public TerminalComponentHandle<TerminalListView> Handle => this._builder.Handle;

    /// <summary>
    /// Sets the text displayed when the list contains no items.
    /// </summary>
    public TerminalListComposer EmptyText(string text)
    {
        this._builder.Set(l => l.EmptyStateText, text);

        return this;
    }

    /// <summary>
    /// Sets the item template used for simple string interpolation.
    /// </summary>
    public TerminalListComposer ItemTemplate(string template)
    {
        this._builder.Set(l => l.ItemTemplate, template);

        return this;
    }

    /// <summary>
    /// Hooks a handler executed when the user activates an item.
    /// </summary>
    public TerminalListComposer OnActivate(TerminalHandlerId handler)
    {
        this._builder.BindEvent(TerminalListViewEvents.ItemActivated, handler);

        return this;
    }

    /// <summary>
    /// Hooks a handler executed when the user requests deletion.
    /// </summary>
    public TerminalListComposer OnDelete(TerminalHandlerId handler)
    {
        this._builder.BindEvent(TerminalListViewEvents.ItemDeleted, handler);

        return this;
    }

    /// <summary>
    /// Toggles the navigation summary panel.
    /// </summary>
    public TerminalListComposer ShowNavigationSummary(bool enabled = true)
    {
        this._builder.Set(l => l.ShowNavigationSummary, enabled);

        return this;
    }

    /// <summary>
    /// Appends a custom hint line under the list.
    /// </summary>
    public TerminalListComposer NavigationHint(string hint)
    {
        this._builder.Set(l => l.NavigationHint, hint);

        return this;
    }

    /// <summary>
    /// Displays a transient status message under the list.
    /// </summary>
    public TerminalListComposer StatusMessage(string? message)
    {
        this._builder.Set(l => l.StatusMessage, message);

        return this;
    }
}