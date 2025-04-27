// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Collections;
using System.Linq.Expressions;
using Itexoft.Extensions;
using Itexoft.TerminalKit.Dsl;
using Itexoft.TerminalKit.Presets.Controls;

namespace Itexoft.TerminalKit.Presets;

/// <summary>
/// High-level helper that scaffolds a CRUD screen using the low-level TerminalUiBuilder.
/// </summary>
public sealed class TerminalCrudScreenBuilder
{
    private static readonly TerminalEventKey itemActivatedEvent = TerminalListViewEvents.ItemActivated;
    private static readonly TerminalEventKey itemDeletedEvent = TerminalListViewEvents.ItemDeleted;
    private static readonly TerminalEventKey cellEditedEvent = TerminalTableViewEvents.CellEdited;
    private static readonly TerminalEventKey formSubmitEvent = TerminalFormEvents.Submit;
    private static readonly TerminalEventKey formCancelEvent = TerminalFormEvents.Cancel;
    private readonly TerminalCrudActions actions;
    private readonly List<TerminalTableColumn> columns = [];
    private readonly List<TerminalFormFieldDefinition> fields = [];
    private readonly List<Action<TerminalComponentComposer<TerminalMetadataForm>>> formDecorators = [];
    private readonly TerminalCrudHandlers handlers;
    private readonly List<Action<TerminalComponentComposer<TerminalPanel>>> headerDecorators = [];
    private readonly List<(TerminalNavigationMode Mode, string Gesture, TerminalActionId Action)> inputs = [];
    private readonly List<Action<TerminalComponentComposer<TerminalListView>>> listDecorators = [];

    private readonly TerminalCrudOptions options;
    private readonly List<TerminalShortcutHintDescriptor> shortcutHints = [];
    private readonly List<Action<TerminalComponentComposer<TerminalTableView>>> tableDecorators = [];

    /// <summary>
    /// Initializes the preset builder with optional overrides for options, actions and handlers.
    /// </summary>
    public TerminalCrudScreenBuilder(TerminalCrudOptions? options = null, TerminalCrudActions? actions = null, TerminalCrudHandlers? handlers = null)
    {
        this.options = options ?? new TerminalCrudOptions();
        this.actions = actions ?? new TerminalCrudActions();
        this.handlers = handlers ?? new TerminalCrudHandlers();

        this.inputs.Add((TerminalNavigationMode.Accelerator, "Ctrl+N", this.actions.CreateItem));
        this.inputs.Add((TerminalNavigationMode.Accelerator, "Ctrl+D", this.actions.RemoveItem));
        this.inputs.Add((TerminalNavigationMode.Numeric, "Digit", this.actions.JumpToIndex));
        this.inputs.Add((TerminalNavigationMode.Arrow, "Up", this.actions.FocusPrevious));
        this.inputs.Add((TerminalNavigationMode.Arrow, "Down", this.actions.FocusNext));
    }

    /// <summary>
    /// Overrides the screen title used by the preset.
    /// </summary>
    public TerminalCrudScreenBuilder WithTitle(string title)
    {
        this.options.Title = title ?? this.options.Title;

        return this;
    }

    /// <summary>
    /// Overrides the theme identifier.
    /// </summary>
    public TerminalCrudScreenBuilder WithTheme(string theme)
    {
        this.options.Theme = theme ?? this.options.Theme;

        return this;
    }

    /// <summary>
    /// Overrides the empty-state placeholder text.
    /// </summary>
    public TerminalCrudScreenBuilder WithEmptyStateText(string text)
    {
        this.options.EmptyStateText = text ?? this.options.EmptyStateText;

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
            IsRequired = required,
        });

    /// <summary>
    /// Adds a text field using a strongly typed selector.
    /// </summary>
    public TerminalCrudScreenBuilder AddTextField<TModel>(Expression<Func<TModel, object?>> selector, string? label = null, bool required = false) =>
        this.AddTextField(DataBindingKey.For(selector), label, required);

    /// <summary>
    /// Adds a select/dropdown field to the metadata form.
    /// </summary>
    public TerminalCrudScreenBuilder AddSelectField(DataBindingKey key, IReadOnlyList<string> options, string? label = null)
    {
        options.Required();

        return this.AddField(
            new()
            {
                Key = key,
                Label = label ?? key.Path,
                Editor = TerminalFormFieldEditor.Select,
                Options = options,
                IsRequired = true,
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
            Editor = TerminalFormFieldEditor.TextArea,
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
        field.Required();

        if (field.Key.Equals(DataBindingKey.Empty))
            throw new ArgumentException("Field key cannot be empty.", nameof(field));

        this.fields.Add(field);

        return this;
    }

    /// <summary>
    /// Allows customization of the header panel.
    /// </summary>
    public TerminalCrudScreenBuilder DecorateHeader(Action<TerminalComponentComposer<TerminalPanel>> configure)
    {
        configure.Required();
        this.headerDecorators.Add(configure);

        return this;
    }

    /// <summary>
    /// Allows customization of the list view.
    /// </summary>
    public TerminalCrudScreenBuilder DecorateList(Action<TerminalComponentComposer<TerminalListView>> configure)
    {
        configure.Required();
        this.listDecorators.Add(configure);

        return this;
    }

    /// <summary>
    /// Allows customization of the table view.
    /// </summary>
    public TerminalCrudScreenBuilder DecorateTable(Action<TerminalComponentComposer<TerminalTableView>> configure)
    {
        configure.Required();
        this.tableDecorators.Add(configure);

        return this;
    }

    /// <summary>
    /// Allows customization of the metadata form component.
    /// </summary>
    public TerminalCrudScreenBuilder DecorateForm(Action<TerminalComponentComposer<TerminalMetadataForm>> configure)
    {
        configure.Required();
        this.formDecorators.Add(configure);

        return this;
    }

    /// <summary>
    /// Registers a new key binding for the preset.
    /// </summary>
    public TerminalCrudScreenBuilder AddInput(TerminalNavigationMode mode, string gesture, TerminalActionId action)
    {
        this.inputs.Add((mode, gesture, action));

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

        this.shortcutHints.Add(
            new()
            {
                Text = text,
            });

        return this;
    }

    /// <summary>
    /// Configures the reusable scrollable window options shared by list/table components.
    /// </summary>
    public TerminalCrudScreenBuilder ConfigureScrollableWindow(Action<TerminalScrollableOptions> configure)
    {
        configure.Required();
        configure(this.options.ScrollableWindow);

        return this;
    }

    /// <summary>
    /// Builds a console UI snapshot using the provided state slices.
    /// </summary>
    /// <param name="state">State container supplying items, selection and viewport data.</param>
    public TerminalSnapshot Build(TerminalCrudScreenState state)
    {
        state.Required();

        var resolvedColumns = this.columns.Count > 0
            ? this.columns
            :
            [
                new()
                {
                    Key = DataBindingKey.From("name"),
                    Header = "Name",
                    Width = 24,
                },
            ];

        var resolvedFields = this.fields.Count > 0
            ? this.fields
            :
            [
                new()
                {
                    Key = DataBindingKey.From("name"),
                    Label = "Name",
                    Editor = TerminalFormFieldEditor.Text,
                    IsRequired = true,
                },
                new()
                {
                    Key = DataBindingKey.From("metadata.status"),
                    Label = "Status",
                    Editor = TerminalFormFieldEditor.Select,
                    Options = ["Draft", "Published", "Archived"],
                    IsRequired = true,
                },
                new()
                {
                    Key = DataBindingKey.From("metadata.owner"),
                    Label = "Owner",
                    Editor = TerminalFormFieldEditor.Text,
                },
            ];

        var builder = TerminalUiBuilder<TerminalScreen>.Create();
        var itemsHandle = builder.WithState(this.options.ItemsStateName, state.Items);
        var selectionHandle = builder.WithState(this.options.SelectionStateName, state.Selection ?? new TerminalSelectionState());
        var viewportHandle = builder.WithState(this.options.ScrollableWindow.StateName, state.Viewport ?? this.CreateDefaultViewportState());

        var additionalSlices = state.Additional ?? new Dictionary<string, object?>();

        foreach (var extra in additionalSlices)
            builder.WithState(extra.Key, extra.Value);

        builder.Configure(screen =>
        {
            screen.Set(s => s.Title, this.options.Title).Set(s => s.Theme, this.options.Theme).AddChild<TerminalPanel>(header =>
            {
                TerminalCommandPanelPreset.Build(header, "row-space-between", ComposeCountLabel(state.Items), this.shortcutHints);

                foreach (var decorator in this.headerDecorators)
                    decorator(new(header));
            }).AddChild<TerminalListView>(list =>
            {
                TerminalScrollableListPreset.Build(
                    list,
                    this.options,
                    this.handlers,
                    itemsHandle,
                    BoxState(viewportHandle),
                    itemActivatedEvent,
                    itemDeletedEvent);

                foreach (var decorator in this.listDecorators)
                    decorator(new(list));
            }).AddChild<TerminalTableView>(table =>
            {
                TerminalCrudTablePreset.Build(
                    table,
                    this.options,
                    resolvedColumns,
                    this.handlers,
                    itemsHandle,
                    BoxState(viewportHandle),
                    cellEditedEvent);

                foreach (var decorator in this.tableDecorators)
                    decorator(new(table));
            }).AddChild<TerminalMetadataForm>(form =>
            {
                TerminalMetadataFormPreset.Build(
                    form,
                    this.options,
                    resolvedFields,
                    BoxState(selectionHandle),
                    this.handlers,
                    formSubmitEvent,
                    formCancelEvent);

                foreach (var decorator in this.formDecorators)
                    decorator(new(form));
            });
        });

        foreach (var binding in this.inputs)
            builder.BindInput(binding.Mode, binding.Gesture, binding.Action);

        return builder.BuildSnapshot();
    }

    /// <summary>
    /// Gets the configured form fields, falling back to sensible defaults when not specified.
    /// </summary>
    public IReadOnlyList<TerminalFormFieldDefinition> GetFormFields() => this.fields.Count > 0
        ? this.fields
        :
        [
            new()
            {
                Key = DataBindingKey.From("name"),
                Label = "Name",
                Editor = TerminalFormFieldEditor.Text,
                IsRequired = true,
            },
            new()
            {
                Key = DataBindingKey.From("metadata.status"),
                Label = "Status",
                Editor = TerminalFormFieldEditor.Select,
                Options = ["Draft", "Published", "Archived"],
                IsRequired = true,
            },
            new()
            {
                Key = DataBindingKey.From("metadata.owner"),
                Label = "Owner",
                Editor = TerminalFormFieldEditor.Text,
            },
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
        {
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
        }

        return null;
    }

    private TerminalViewportState CreateDefaultViewportState() => new()
    {
        Offset = 0,
        WindowSize = this.options.ScrollableWindow.DefaultWindowSize ?? 10,
    };

    private static StateHandle<object> BoxState<TState>(StateHandle<TState> handle) => new(handle.Name);
}
