// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Linq.Expressions;
using Itexoft.Extensions;

namespace Itexoft.TerminalKit.Dsl;

/// <summary>
/// DSL helper for configuring rich tabular views.
/// </summary>
public sealed class TerminalTableComposer
{
    private readonly TerminalComponentBuilder<TerminalTableView> builder;
    private readonly List<TerminalTableColumn> columns = [];
    private readonly Dictionary<string, TerminalCellStyle> columnStyles = new(StringComparer.Ordinal);
    private readonly List<TerminalCellStyleRule> rules = [];
    private StateHandle<object>? selectionHandle;
    private TerminalCellStyle? tableStyle;

    internal TerminalTableComposer(TerminalComponentBuilder<TerminalTableView> builder) => this.builder = builder;

    /// <summary>
    /// Gets the handle pointing to the table view component.
    /// </summary>
    public TerminalComponentHandle<TerminalTableView> Handle => this.builder.Handle;

    /// <summary>
    /// Adds a column bound to a property identified by <paramref name="key" />.
    /// </summary>
    public TerminalTableComposer Column(DataBindingKey key, string header, int width = 12)
    {
        this.columns.Add(
            new()
            {
                Key = key,
                Header = header,
                Width = width,
            });

        return this;
    }

    /// <summary>
    /// Adds a strongly typed column bound via expression instead of a raw key.
    /// </summary>
    public TerminalTableComposer Column<TModel>(Expression<Func<TModel, object?>> selector, string header, int width = 12) =>
        this.Column(DataBindingKey.For(selector), header, width);

    /// <summary>
    /// Binds a handler executed when any cell is edited.
    /// </summary>
    public TerminalTableComposer OnCellEdit(TerminalHandlerId handler)
    {
        this.builder.BindEvent(TerminalTableViewEvents.CellEdited, handler);

        return this;
    }

    /// <summary>
    /// Sets global table colors.
    /// </summary>
    public TerminalTableComposer StyleTable(ConsoleColor? foreground = null, ConsoleColor? background = null)
    {
        this.tableStyle = new()
        {
            Foreground = foreground,
            Background = background,
        };

        return this;
    }

    /// <summary>
    /// Overrides colors for a specific column referenced by key.
    /// </summary>
    public TerminalTableComposer StyleColumn(DataBindingKey key, ConsoleColor? foreground = null, ConsoleColor? background = null)
    {
        this.columnStyles[key.Path] = new()
        {
            Foreground = foreground,
            Background = background,
        };

        return this;
    }

    /// <summary>
    /// Overrides column colors using a strongly typed selector.
    /// </summary>
    public TerminalTableComposer StyleColumn<TModel>(
        Expression<Func<TModel, object?>> selector,
        ConsoleColor? foreground = null,
        ConsoleColor? background = null) => this.StyleColumn(DataBindingKey.For(selector), foreground, background);

    /// <summary>
    /// Adds a conditional formatting rule driven by a binding key.
    /// </summary>
    public TerminalTableComposer StyleByValue(DataBindingKey key, Action<TerminalCellStyleRuleBuilder> configure)
    {
        configure.Required();
        var builder = new TerminalCellStyleRuleBuilder(key);
        configure(builder);
        this.rules.Add(builder.Build());

        return this;
    }

    /// <summary>
    /// Adds a conditional formatting rule using an expression selector.
    /// </summary>
    public TerminalTableComposer StyleByValue<TModel>(Expression<Func<TModel, object?>> selector, Action<TerminalCellStyleRuleBuilder> configure) =>
        this.StyleByValue(DataBindingKey.For(selector), configure);

    /// <summary>
    /// Toggles the navigation summary panel.
    /// </summary>
    public TerminalTableComposer ShowNavigationSummary(bool enabled = true)
    {
        this.builder.Set(t => t.ShowNavigationSummary, enabled);

        return this;
    }

    /// <summary>
    /// Sets custom navigation hint text.
    /// </summary>
    public TerminalTableComposer NavigationHint(string hint)
    {
        this.builder.Set(t => t.NavigationHint, hint);

        return this;
    }

    /// <summary>
    /// Displays an informational status message below the table.
    /// </summary>
    public TerminalTableComposer StatusMessage(string? message)
    {
        this.builder.Set(t => t.StatusMessage, message);

        return this;
    }

    internal void SetSelection(StateHandle<object> selectionHandle) => this.selectionHandle = selectionHandle;

    internal void Apply()
    {
        this.builder.Set(t => t.Columns, this.columns.ToArray());

        if (this.tableStyle != null)
            this.builder.Set(t => t.TableStyle, this.tableStyle);

        if (this.columnStyles.Count > 0)
        {
            var snapshot = this.columnStyles.ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.Ordinal);
            this.builder.Set(t => t.ColumnStyles, snapshot);
        }

        if (this.rules.Count > 0)
            this.builder.Set(t => t.CellStyleRules, this.rules.ToArray());

        if (this.selectionHandle is { } selectionHandle)
            this.builder.BindState(t => t.SelectionState, selectionHandle);
    }
}
