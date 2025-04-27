// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Collections;
using System.Linq.Expressions;
using Itexoft.TerminalKit.Dsl;
using Itexoft.TerminalKit.Presets.Controls;

namespace Itexoft.TerminalKit.Presets;

/// <summary>
/// High-level helper that scaffolds a CRUD screen using the low-level TerminalUiBuilder.
/// </summary>
public sealed class TerminalCrudScreenBuilder
{
    private static readonly TerminalEventKey ItemActivatedEvent = TerminalListViewEvents.ItemActivated;
    private static readonly TerminalEventKey ItemDeletedEvent = TerminalListViewEvents.ItemDeleted;
    private static readonly TerminalEventKey CellEditedEvent = TerminalTableViewEvents.CellEdited;
    private static readonly TerminalEventKey FormSubmitEvent = TerminalFormEvents.Submit;
    private static readonly TerminalEventKey FormCancelEvent = TerminalFormEvents.Cancel;
    private readonly TerminalCrudActions _actions;
    private readonly List<TerminalTableColumn> _columns = [];
    private readonly List<TerminalFormFieldDefinition> _fields = [];
    private readonly List<Action<TerminalComponentComposer<TerminalMetadataForm>>> _formDecorators = [];
    private readonly TerminalCrudHandlers _handlers;
    private readonly List<Action<TerminalComponentComposer<TerminalPanel>>> _headerDecorators = [];
    private readonly List<(TerminalNavigationMode Mode, string Gesture, TerminalActionId Action)> _inputs = [];
    private readonly List<Action<TerminalComponentComposer<TerminalListView>>> _listDecorators = [];

    private readonly TerminalCrudOptions _options;
    private readonly List<TerminalShortcutHintDescriptor> _shortcutHints = [];
    private readonly List<Action<TerminalComponentComposer<TerminalTableView>>> _tableDecorators = [];

    /// <summary>
    /// Initializes the preset builder with optional overrides for options, actions and handlers.
    /// </summary>
    public TerminalCrudScreenBuilder(
        TerminalCrudOptions? options = null,
        TerminalCrudActions? actions = null,
        TerminalCrudHandlers? handlers = null)
    {
        this._options = options ?? new TerminalCrudOptions();
        this._actions = actions ?? new TerminalCrudActions();
        this._handlers = handlers ?? new TerminalCrudHandlers();

        this._inputs.Add((TerminalNavigationMode.Accelerator, "Ctrl+N", this._actions.CreateItem));
        this._inputs.Add((TerminalNavigationMode.Accelerator, "Ctrl+D", this._actions.RemoveItem));
        this._inputs.Add((TerminalNavigationMode.Numeric, "Digit", this._actions.JumpToIndex));
        this._inputs.Add((TerminalNavigationMode.Arrow, "Up", this._actions.FocusPrevious));
        this._inputs.Add((TerminalNavigationMode.Arrow, "Down", this._actions.FocusNext));
    }

    /// <summary>
    /// Overrides the screen title used by the preset.
    /// </summary>
    public TerminalCrudScreenBuilder WithTitle(string title)
    {
        this._options.Title = title ?? this._options.Title;

        return this;
    }

    /// <summary>
    /// Overrides the theme identifier.
    /// </summary>
    public TerminalCrudScreenBuilder WithTheme(string theme)
    {
        this._options.Theme = theme ?? this._options.Theme;

        return this;
    }

    /// <summary>
    /// Overrides the empty-state placeholder text.
    /// </summary>
    public TerminalCrudScreenBuilder WithEmptyStateText(string text)
    {
        this._options.EmptyStateText = text ?? this._options.EmptyStateText;

        return this;
    }

    /// <summary>
    /// Adds a column bound to the specified binding key.
    /// </summary>
    public TerminalCrudScreenBuilder AddColumn(DataBindingKey key, string header, int width = 12)
    {
        if (string.IsNullOrWhiteSpace(header))
            throw new ArgumentException("Column header cannot be empty.", nameof(header));

        if (key.Equals(DataBindingKey.Empty))
            throw new ArgumentException("Column key cannot be empty.", nameof(key));

        this._columns.Add(
            new()
            {
                Key = key,
                Header = header,
                Width = width
            });

        return this;
    }

    /// <summary>
    /// Adds a column using a strongly typed selector.
    /// </summary>
    public TerminalCrudScreenBuilder AddColumn<TModel>(Expression<Func<TModel, object?>> selector, string header, int width = 12) =>
        this.AddColumn(DataBindingKey.For(selector), header, width);

    /// <summary>
    /// Adds a text field to the metadata form.
    /// </summary>
    public TerminalCrudScreenBuilder AddTextField(DataBindingKey key, string? label = null, bool required = false) => this.AddField(
        new()
        {
            Key = key,
            Label = label ?? key.Path,
            Editor = TerminalFormFieldEditor.Text,
            IsRequired = required
        });

    /// <summary>
    /// Adds a text field using a strongly typed selector.
    /// </summary>
    public TerminalCrudScreenBuilder AddTextField<TModel>(
        Expression<Func<TModel, object?>> selector,
        string? label = null,
        bool required = false) => this.AddTextField(DataBindingKey.For(selector), label, required);

    /// <summary>
    /// Adds a select/dropdown field to the metadata form.
    /// </summary>
    public TerminalCrudScreenBuilder AddSelectField(DataBindingKey key, IReadOnlyList<string> options, string? label = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        return this.AddField(
            new()
            {
                Key = key,
                Label = label ?? key.Path,
                Editor = TerminalFormFieldEditor.Select,
                Options = options,
                IsRequired = true
            });
    }

    /// <summary>
    /// Adds a select field using a strongly typed selector.
    /// </summary>
    public TerminalCrudScreenBuilder AddSelectField<TModel>(
        Expression<Func<TModel, object?>> selector,
        IReadOnlyList<string> options,
        string? label = null) => this.AddSelectField(DataBindingKey.For(selector), options, label);

    /// <summary>
    /// Adds a multi-line text area field to the metadata form.
    /// </summary>
    public TerminalCrudScreenBuilder AddTextAreaField(DataBindingKey key, string? label = null) => this.AddField(
        new()
        {
            Key = key,
            Label = label ?? key.Path,
            Editor = TerminalFormFieldEditor.TextArea
        });

    /// <summary>
    /// Adds a text area field using a strongly typed selector.
    /// </summary>
    public TerminalCrudScreenBuilder AddTextAreaField<TModel>(Expression<Func<TModel, object?>> selector, string? label = null) =>
        this.AddTextAreaField(DataBindingKey.For(selector), label);

    /// <summary>
    /// Adds a fully prepared field definition to the metadata form.
    /// </summary>
    public TerminalCrudScreenBuilder AddField(TerminalFormFieldDefinition field)
    {
        ArgumentNullException.ThrowIfNull(field);

        if (field.Key.Equals(DataBindingKey.Empty))
            throw new ArgumentException("Field key cannot be empty.", nameof(field));

        this._fields.Add(field);

        return this;
    }

    /// <summary>
    /// Allows customization of the header panel.
    /// </summary>
    public TerminalCrudScreenBuilder DecorateHeader(Action<TerminalComponentComposer<TerminalPanel>> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        this._headerDecorators.Add(configure);

        return this;
    }

    /// <summary>
    /// Allows customization of the list view.
    /// </summary>
    public TerminalCrudScreenBuilder DecorateList(Action<TerminalComponentComposer<TerminalListView>> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        this._listDecorators.Add(configure);

        return this;
    }

    /// <summary>
    /// Allows customization of the table view.
    /// </summary>
    public TerminalCrudScreenBuilder DecorateTable(Action<TerminalComponentComposer<TerminalTableView>> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        this._tableDecorators.Add(configure);

        return this;
    }

    /// <summary>
    /// Allows customization of the metadata form component.
    /// </summary>
    public TerminalCrudScreenBuilder DecorateForm(Action<TerminalComponentComposer<TerminalMetadataForm>> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        this._formDecorators.Add(configure);

        return this;
    }

    /// <summary>
    /// Registers a new key binding for the preset.
    /// </summary>
    public TerminalCrudScreenBuilder AddInput(TerminalNavigationMode mode, string gesture, TerminalActionId action)
    {
        this._inputs.Add((mode, gesture, action));

        return this;
    }

    /// <summary>
    /// Registers a key binding using a raw action name.
    /// </summary>
    public TerminalCrudScreenBuilder AddInput(TerminalNavigationMode mode, string gesture, string action) =>
        this.AddInput(mode, gesture, TerminalActionId.From(action));

    /// <summary>
    /// Appends a shortcut hint rendered inside the command bar.
    /// </summary>
    public TerminalCrudScreenBuilder AddShortcutHint(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Shortcut hint text cannot be empty.", nameof(text));

        this._shortcutHints.Add(
            new()
            {
                Text = text
            });

        return this;
    }

    /// <summary>
    /// Configures the reusable scrollable window options shared by list/table components.
    /// </summary>
    public TerminalCrudScreenBuilder ConfigureScrollableWindow(Action<TerminalScrollableOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(this._options.ScrollableWindow);

        return this;
    }

    /// <summary>
    /// Builds a console UI snapshot using the provided state slices.
    /// </summary>
    /// <param name="state">State container supplying items, selection and viewport data.</param>
    public TerminalSnapshot Build(TerminalCrudScreenState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var resolvedColumns = this._columns.Count > 0
            ? this._columns
            :
            [
                new()
                {
                    Key = DataBindingKey.From("name"),
                    Header = "Name",
                    Width = 24
                }
            ];

        var resolvedFields = this._fields.Count > 0
            ? this._fields
            :
            [
                new()
                {
                    Key = DataBindingKey.From("name"),
                    Label = "Name",
                    Editor = TerminalFormFieldEditor.Text,
                    IsRequired = true
                },
                new()
                {
                    Key = DataBindingKey.From("metadata.status"),
                    Label = "Status",
                    Editor = TerminalFormFieldEditor.Select,
                    Options = ["Draft", "Published", "Archived"],
                    IsRequired = true
                },
                new()
                {
                    Key = DataBindingKey.From("metadata.owner"),
                    Label = "Owner",
                    Editor = TerminalFormFieldEditor.Text
                }
            ];

        var builder = TerminalUiBuilder<TerminalScreen>.Create();
        var itemsHandle = builder.WithState(this._options.ItemsStateName, state.Items);
        var selectionHandle = builder.WithState(this._options.SelectionStateName, state.Selection ?? new TerminalSelectionState());
        var viewportHandle = builder.WithState(
            this._options.ScrollableWindow.StateName,
            state.Viewport ?? this.CreateDefaultViewportState());

        var additionalSlices = state.Additional ?? new Dictionary<string, object?>();
        foreach (var extra in additionalSlices)
            builder.WithState(extra.Key, extra.Value);

        builder.Configure(screen =>
        {
            screen.Set(s => s.Title, this._options.Title)
                .Set(s => s.Theme, this._options.Theme)
                .AddChild<TerminalPanel>(header =>
                {
                    TerminalCommandPanelPreset.Build(
                        header,
                        "row-space-between",
                        ComposeCountLabel(state.Items),
                        this._shortcutHints);
                    foreach (var decorator in this._headerDecorators)
                        decorator(new(header));
                })
                .AddChild<TerminalListView>(list =>
                {
                    TerminalScrollableListPreset.Build(
                        list,
                        this._options,
                        this._handlers,
                        itemsHandle,
                        BoxState(viewportHandle),
                        ItemActivatedEvent,
                        ItemDeletedEvent);
                    foreach (var decorator in this._listDecorators)
                        decorator(new(list));
                })
                .AddChild<TerminalTableView>(table =>
                {
                    TerminalCrudTablePreset.Build(
                        table,
                        this._options,
                        resolvedColumns,
                        this._handlers,
                        itemsHandle,
                        BoxState(viewportHandle),
                        CellEditedEvent);
                    foreach (var decorator in this._tableDecorators)
                        decorator(new(table));
                })
                .AddChild<TerminalMetadataForm>(form =>
                {
                    TerminalMetadataFormPreset.Build(
                        form,
                        this._options,
                        resolvedFields,
                        BoxState(selectionHandle),
                        this._handlers,
                        FormSubmitEvent,
                        FormCancelEvent);
                    foreach (var decorator in this._formDecorators)
                        decorator(new(form));
                });
        });

        foreach (var binding in this._inputs)
            builder.BindInput(binding.Mode, binding.Gesture, binding.Action);

        return builder.BuildSnapshot();
    }

    /// <summary>
    /// Gets the configured form fields, falling back to sensible defaults when not specified.
    /// </summary>
    public IReadOnlyList<TerminalFormFieldDefinition> GetFormFields() => this._fields.Count > 0
        ? this._fields
        :
        [
            new()
            {
                Key = DataBindingKey.From("name"),
                Label = "Name",
                Editor = TerminalFormFieldEditor.Text,
                IsRequired = true
            },
            new()
            {
                Key = DataBindingKey.From("metadata.status"),
                Label = "Status",
                Editor = TerminalFormFieldEditor.Select,
                Options = ["Draft", "Published", "Archived"],
                IsRequired = true
            },
            new()
            {
                Key = DataBindingKey.From("metadata.owner"),
                Label = "Owner",
                Editor = TerminalFormFieldEditor.Text
            }
        ];

    private static string ComposeCountLabel(object? items)
    {
        var count = TryResolveCount(items);

        return count.HasValue ? $"Items: {count.Value}" : "Items";
    }

    private static int? TryResolveCount(object? items)
    {
        if (items == null)
            return 0;

        if (items is Array array)
            return array.Length;

        if (items is ICollection nonGeneric)
            return nonGeneric.Count;

        var type = items.GetType();
        foreach (var @interface in type.GetInterfaces())
            if (@interface.IsGenericType)
            {
                var definition = @interface.GetGenericTypeDefinition();
                if (definition == typeof(ICollection<>) || definition == typeof(IReadOnlyCollection<>))
                {
                    var countProperty = @interface.GetProperty(nameof(ICollection.Count));

                    if (countProperty?.GetValue(items) is int genericCount)
                        return genericCount;
                }
            }

        return null;
    }

    private TerminalViewportState CreateDefaultViewportState() => new()
    {
        Offset = 0,
        WindowSize = this._options.ScrollableWindow.DefaultWindowSize ?? 10
    };

    private static StateHandle<object> BoxState<TState>(StateHandle<TState> handle) => new(handle.Name);
}