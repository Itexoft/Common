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
    private readonly Func<IDictionary<DataBindingKey, string?>, TItem> createFactory;
    private readonly Func<TItem?, TerminalFormFieldDefinition, string?> defaultValueProvider;
    private readonly TerminalFormDialog dialog;
    private readonly Action<TItem, IDictionary<DataBindingKey, string?>> editApplicator;
    private readonly Func<TItem, string> itemFormatter;
    private readonly Func<TItem?, TerminalFormFieldDefinition, IReadOnlyList<string>?> optionsProvider;
    private readonly Func<TerminalWorkspace<TItem>, TerminalSnapshot>? snapshotFactory;
    private readonly Func<TItem?, TerminalFormFieldDefinition, string?, string?> validator;
    private readonly TerminalWorkspace<TItem> workspace;

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
        this.workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        this.dialog = dialog ?? throw new ArgumentNullException(nameof(dialog));
        this.createFactory = createFactory ?? throw new ArgumentNullException(nameof(createFactory));
        this.editApplicator = editApplicator ?? throw new ArgumentNullException(nameof(editApplicator));
        this.itemFormatter = itemFormatter ?? (item => item?.ToString() ?? string.Empty);
        this.defaultValueProvider = defaultValueProvider ?? ((_, _) => null);
        this.validator = validator ?? ((_, _, _) => null);
        this.optionsProvider = optionsProvider ?? ((_, _) => null);
        this.snapshotFactory = snapshotFactory;
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
        var snapshot = this.snapshotFactory?.Invoke(this.workspace) ?? this.workspace.BuildSnapshot();
        var renderer = new TerminalSnapshotRenderer(snapshot);
        renderer.Render();
    }

    private void HandleAdd()
    {
        var values = this.dialog.Prompt(
            field => this.defaultValueProvider(default, field),
            (field, value) => this.validator(default, field, value),
            field => this.optionsProvider(default, field));

        var item = this.createFactory(values);
        this.workspace.Items.Add(item);
        this.workspace.SelectByIndex(this.workspace.Items.Count - 1);
    }

    private void HandleEdit()
    {
        if (!TryReadIndex(out var index))
            return;

        var items = this.workspace.Items;

        if (index < 0 || index >= items.Count)
            return;

        var item = items[index];
        this.workspace.SelectByIndex(index);

        var values = this.dialog.Prompt(
            field => this.defaultValueProvider(item, field),
            (field, value) => this.validator(item, field, value),
            field => this.optionsProvider(item, field));

        this.editApplicator(item, values);
    }

    private void HandleDelete()
    {
        if (!TryReadIndex(out var index))
            return;

        var items = this.workspace.Items;

        if (index < 0 || index >= items.Count)
            return;

        items.RemoveAt(index);
        this.workspace.SelectByIndex(Math.Min(index, items.Count - 1));
    }

    private static bool TryReadIndex(out int index)
    {
        Console.Write("\nIndex: ");
        var input = Console.ReadLine();

        return int.TryParse(input, out index);
    }
}
