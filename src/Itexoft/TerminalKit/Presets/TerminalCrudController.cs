// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.TerminalKit.Rendering;

namespace Itexoft.TerminalKit.Presets;

/// <summary>
/// Simple console host that renders snapshots and handles A/E/D/Q workflow for TerminalWorkspace.
/// </summary>
public sealed class TerminalCrudController<TItem>
{
    private readonly Func<IDictionary<DataBindingKey, string?>, TItem> _createFactory;
    private readonly Func<TItem?, TerminalFormFieldDefinition, string?> _defaultValueProvider;
    private readonly TerminalFormDialog _dialog;
    private readonly Action<TItem, IDictionary<DataBindingKey, string?>> _editApplicator;
    private readonly Func<TItem, string> _itemFormatter;
    private readonly Func<TItem?, TerminalFormFieldDefinition, IReadOnlyList<string>?> _optionsProvider;
    private readonly Func<TerminalWorkspace<TItem>, TerminalSnapshot>? _snapshotFactory;
    private readonly Func<TItem?, TerminalFormFieldDefinition, string?, string?> _validator;
    private readonly TerminalWorkspace<TItem> _workspace;

    /// <summary>
    /// Initializes the controller that hosts a CRUD workspace inside a classic console loop.
    /// </summary>
    public TerminalCrudController(
        TerminalWorkspace<TItem> workspace,
        TerminalFormDialog dialog,
        Func<IDictionary<DataBindingKey, string?>, TItem> createFactory,
        Action<TItem, IDictionary<DataBindingKey, string?>> editApplicator,
        Func<TItem, string>? itemFormatter = null,
        Func<TItem?, TerminalFormFieldDefinition, string?>? defaultValueProvider = null,
        Func<TItem?, TerminalFormFieldDefinition, string?, string?>? validator = null,
        Func<TItem?, TerminalFormFieldDefinition, IReadOnlyList<string>?>? optionsProvider = null,
        Func<TerminalWorkspace<TItem>, TerminalSnapshot>? snapshotFactory = null)
    {
        this._workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        this._dialog = dialog ?? throw new ArgumentNullException(nameof(dialog));
        this._createFactory = createFactory ?? throw new ArgumentNullException(nameof(createFactory));
        this._editApplicator = editApplicator ?? throw new ArgumentNullException(nameof(editApplicator));
        this._itemFormatter = itemFormatter ?? (item => item?.ToString() ?? string.Empty);
        this._defaultValueProvider = defaultValueProvider ?? ((_, _) => null);
        this._validator = validator ?? ((_, _, _) => null);
        this._optionsProvider = optionsProvider ?? ((_, _) => null);
        this._snapshotFactory = snapshotFactory;
    }

    /// <summary>
    /// Starts the controller loop using the configured workspace and dialog.
    /// </summary>
    public void Run()
    {
        while (true)
        {
            this.Render();
            Console.Write("\n[A]dd  [E]dit  [D]elete  [Q]uit: ");
            var key = Console.ReadKey(intercept: true).KeyChar;
            switch (char.ToUpperInvariant(key))
            {
                case 'A':
                    this.HandleAdd();

                    break;
                case 'E':
                    this.HandleEdit();

                    break;
                case 'D':
                    this.HandleDelete();

                    break;
                case 'Q':
                    return;
            }
        }
    }

    private void Render()
    {
        var snapshot = this._snapshotFactory?.Invoke(this._workspace) ?? this._workspace.BuildSnapshot();
        var renderer = new TerminalSnapshotRenderer(snapshot);
        renderer.Render();
    }

    private void HandleAdd()
    {
        var values = this._dialog.Prompt(
            field => this._defaultValueProvider(default, field),
            (field, value) => this._validator(default, field, value),
            field => this._optionsProvider(default, field));
        var item = this._createFactory(values);
        this._workspace.Items.Add(item);
        this._workspace.SelectByIndex(this._workspace.Items.Count - 1);
    }

    private void HandleEdit()
    {
        if (!TryReadIndex(out var index))
            return;

        var items = this._workspace.Items;

        if (index < 0 || index >= items.Count)
            return;

        var item = items[index];
        this._workspace.SelectByIndex(index);
        var values = this._dialog.Prompt(
            field => this._defaultValueProvider(item, field),
            (field, value) => this._validator(item, field, value),
            field => this._optionsProvider(item, field));
        this._editApplicator(item, values);
    }

    private void HandleDelete()
    {
        if (!TryReadIndex(out var index))
            return;

        var items = this._workspace.Items;

        if (index < 0 || index >= items.Count)
            return;

        items.RemoveAt(index);
        this._workspace.SelectByIndex(Math.Min(index, items.Count - 1));
    }

    private static bool TryReadIndex(out int index)
    {
        Console.Write("\nIndex: ");
        var input = Console.ReadLine();

        return int.TryParse(input, out index);
    }
}