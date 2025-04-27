// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;
using Itexoft.TerminalKit.Dsl;
using Itexoft.TerminalKit.Interaction;
using Itexoft.TerminalKit.Presets;
using Itexoft.TerminalKit.Reflection;
using Itexoft.TerminalKit.Rendering;

namespace Itexoft.TerminalKit.ObjectExplorer;

internal sealed class TerminalExplorerSession
{
    private const int PropertyLoadDegree = 4;
    private static readonly TerminalStateKey ItemsKey = TerminalStateKey.From("explorer.items");
    private static readonly TerminalStateKey ViewportKey = TerminalStateKey.From("explorer.viewport");
    private static readonly TerminalStateKey SelectionKey = TerminalStateKey.From("explorer.selection");
    private readonly IReadOnlyList<TerminalExplorerButton> _buttons;
    private readonly List<TerminalExplorerEntry> _entries = [];
    private readonly HashSet<MethodInfo> _executingMethods = [];
    private readonly Stack<TerminalExplorerFrame> _frames = new();
    private readonly TerminalObjectExplorer _owner;
    private readonly TerminalViewportState _viewport = new() { WindowSize = 12 };
    private readonly TerminalScrollableWindowController _window;
    private CancellationTokenSource? _propertyLoadCts;
    private SemaphoreSlim _propertyLoadSemaphore = new(PropertyLoadDegree, PropertyLoadDegree);
    private bool _shiftSelectionActive;
    private bool _shiftSelectionState;
    private string _statusMessage = string.Empty;

    public TerminalExplorerSession(
        object? root,
        string title,
        TerminalObjectExplorer owner,
        IReadOnlyList<TerminalExplorerButton> buttons)
    {
        this._owner = owner ?? throw new ArgumentNullException(nameof(owner));
        this._buttons = buttons ?? [];
        this._frames.Push(TerminalExplorerFrame.Create(root, title));
        this._window = new(this._viewport, () => this._entries.Count);
        this.EnsurePropertyLoadSession(reset: true);
        this.RefreshEntries(startLoads: false);
        this.EnsureAutoEnterCurrentFrame();
    }

    internal TerminalSnapshot BuildSnapshot()
    {
        this.UpdateViewportWindowSize();
        this.EnsurePropertyLoadSession(reset: false);
        var snapshotEntries = this._entries.ToArray();
        this.StartVisiblePropertyLoads();
        var frame = this._frames.Peek();
        var scene = TerminalScene<TerminalScreen>.Create()
            .WithState(ItemsKey, snapshotEntries)
            .WithState(ViewportKey, this._viewport)
            .WithState(SelectionKey, this._window.Selection)
            .Compose(composer =>
            {
                composer.Breadcrumb(this.FormatBreadcrumb);

                composer.Table(
                    ItemsKey,
                    ViewportKey,
                    SelectionKey,
                    table =>
                    {
                        table.Column<TerminalExplorerEntry>(x => x.Display, "Item", 32);
                        table.Column<TerminalExplorerEntry>(x => x.ValuePreview, "Value", 52);
                        table.Column<TerminalExplorerEntry>(x => x.ClrTypeName, "Type", 36);
                        table.NavigationHint(this.BuildNavigationHint());
                        table.StatusMessage(string.IsNullOrWhiteSpace(this._statusMessage) ? null : this._statusMessage);
                    });
            });

        return scene.Build();
    }

    internal TerminalKeyBindingMap BuildBindings()
    {
        var map = new TerminalKeyBindingMap()
            .WithScrollableWindow(this._window)
            .On(
                ConsoleKey.UpArrow,
                context =>
                {
                    this.ApplyShiftSelection(IsShiftHeld(context.Modifiers), true);
                    this.MoveAndLoad(this._window.MovePrevious);
                    this.ApplyShiftSelection(IsShiftHeld(context.Modifiers));

                    return false;
                })
            .On(
                ConsoleKey.DownArrow,
                context =>
                {
                    this.ApplyShiftSelection(IsShiftHeld(context.Modifiers), true);
                    this.MoveAndLoad(this._window.MoveNext);
                    this.ApplyShiftSelection(IsShiftHeld(context.Modifiers));

                    return false;
                })
            .On(
                ConsoleKey.Enter,
                () =>
                {
                    this.OpenSelectedEntry();

                    return false;
                })
            .On(
                ConsoleKey.RightArrow,
                () =>
                {
                    this.OpenSelectedEntry();

                    return false;
                })
            .On(ConsoleKey.LeftArrow, () => this.GoBack())
            .On(ConsoleKey.Escape, () => this.GoBack())
            .On(
                ConsoleKey.Spacebar,
                () =>
                {
                    this.ToggleSelectionMark();
                    this._shiftSelectionActive = false;

                    return false;
                })
            .On(
                ConsoleKey.Delete,
                () =>
                {
                    this.DeleteSelection();

                    return false;
                })
            .On(
                ConsoleKey.PageUp,
                context =>
                {
                    this.ApplyShiftSelection(IsShiftHeld(context.Modifiers), true);
                    this.MoveAndLoad(this._window.PageUp);
                    this.ApplyShiftSelection(IsShiftHeld(context.Modifiers));

                    return false;
                })
            .On(
                ConsoleKey.PageDown,
                context =>
                {
                    this.ApplyShiftSelection(IsShiftHeld(context.Modifiers), true);
                    this.MoveAndLoad(this._window.PageDown);
                    this.ApplyShiftSelection(IsShiftHeld(context.Modifiers));

                    return false;
                })
            .On(
                ConsoleKey.Home,
                context =>
                {
                    this.ApplyShiftSelection(IsShiftHeld(context.Modifiers), true);
                    this.MoveAndLoad(() => this._window.MoveTo(0));
                    this.ApplyShiftSelection(IsShiftHeld(context.Modifiers));

                    return false;
                })
            .On(
                ConsoleKey.End,
                context =>
                {
                    this.ApplyShiftSelection(IsShiftHeld(context.Modifiers), true);
                    this.MoveAndLoad(this._window.MoveToEnd);
                    this.ApplyShiftSelection(IsShiftHeld(context.Modifiers));

                    return false;
                })
            .On(
                ConsoleKey.R,
                () =>
                {
                    this.RefreshEntries(force: true);

                    return false;
                });

        map.On(
                ConsoleKey.I,
                () =>
                {
                    if (this.HasListContext())
                        this.InsertRelative(insertAfter: false);
                    else
                        this._statusMessage = "Select a list to insert.";

                    return false;
                })
            .On(
                ConsoleKey.O,
                () =>
                {
                    if (this.HasListContext())
                        this.InsertRelative(insertAfter: true);
                    else
                        this._statusMessage = "Select a list to insert.";

                    return false;
                })
            .On(
                ConsoleKey.A,
                () =>
                {
                    if (this.HasListContext() || this.HasDictionaryContext())
                        this.AddEntry();
                    else
                        this._statusMessage = "Select a collection to add items.";

                    return false;
                })
            .On(
                ConsoleKey.K,
                () =>
                {
                    if (this.HasDictionaryContext())
                        this.EditDictionaryKey();
                    else
                        this._statusMessage = "Not a dictionary.";

                    return false;
                });

        foreach (var button in this.GetVisibleButtons())
        {
            var snapshot = button;
            map.On(
                snapshot.Key,
                () =>
                {
                    var result = snapshot.Invoke(this._owner, this);
                    if (result == TerminalExplorerButtonResult.Refresh)
                        this.RefreshEntries(force: true);

                    return result == TerminalExplorerButtonResult.Exit;
                });
        }

        return map;
    }

    private void RefreshEntries(bool force = false, bool startLoads = true)
    {
        if (force)
            this.ResetPropertyLoadSession();
        else
            this.EnsurePropertyLoadSession(reset: false);
        var frame = this._frames.Peek();
        if (force)
            frame.RefreshEntries();

        this._entries.Clear();
        var normalized = frame.Entries
            .Select(entry =>
            {
                var executing = entry.Method != null && this._executingMethods.Contains(entry.Method);

                return entry with { IsMarked = entry.IsMarked, IsExecuting = executing, LoadStatus = entry.LoadStatus };
            })
            .ToList();
        this._entries.AddRange(normalized);
        frame.PruneSelection(this._entries.Count);
        var targetIndex = Math.Min(this._window.Selection, Math.Max(0, this._entries.Count - 1));
        this._window.MoveTo(targetIndex);

        if (startLoads)
            this.StartVisiblePropertyLoads(frame);

        TerminalDispatcher.Current?.Invalidate();
    }

    private void OpenSelectedEntry()
    {
        if (this._entries.Count == 0)
            return;

        var index = Math.Clamp(this._window.Selection, 0, this._entries.Count - 1);
        var entry = this._entries[index];
        var frame = this._frames.Peek();
        var target = frame.Target;

        try
        {
            switch (entry.Kind)
            {
                case TerminalExplorerEntryKind.Property when entry.Property != null:
                    this.HandleProperty(target, entry.Property, entry.IsEditable);

                    break;
                case TerminalExplorerEntryKind.Method when entry.Method != null:
                    this.HandleMethod(target, entry.Method);

                    break;
                case TerminalExplorerEntryKind.CollectionItemsNode when entry.Payload is IEnumerable enumerable:
                    this.PushFrame(TerminalExplorerFrame.CreateItemsView(enumerable, entry.Display, entry));

                    break;
                case TerminalExplorerEntryKind.CollectionItem:
                    this.PushFrame(entry.Payload, entry.Display);

                    break;
            }
        }
        catch (Exception ex)
        {
            this.ShowError(ex);
        }
    }

    private void ResetPropertyLoadSession()
    {
        if (this._propertyLoadCts != null)
        {
            this._propertyLoadCts.Cancel();
            this._propertyLoadCts.Dispose();
        }

        this._propertyLoadCts = new();
        this._propertyLoadSemaphore = new(PropertyLoadDegree, PropertyLoadDegree);
    }

    private void EnsurePropertyLoadSession(bool reset)
    {
        if (reset)
        {
            this.ResetPropertyLoadSession();

            return;
        }

        if (this._propertyLoadCts == null || this._propertyLoadCts.IsCancellationRequested)
            this._propertyLoadCts = new();
    }

    private void StartBackgroundLoad(Func<CancellationToken, Task> operation)
    {
        var cts = this._propertyLoadCts;
        var semaphore = this._propertyLoadSemaphore;

        _ = Task.Run(async () =>
        {
            if (cts == null || semaphore == null || cts.IsCancellationRequested)
                return;

            try
            {
                await semaphore.WaitAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                if (cts.IsCancellationRequested)
                    return;

                await operation(cts.Token).ConfigureAwait(false);
            }
            finally
            {
                semaphore.Release();
            }
        });
    }

    private void ApplyPropertyState(
        TerminalExplorerFrame frame,
        PropertyInfo property,
        int version,
        TerminalDispatcher? dispatcher,
        Action<TerminalPropertyValueState> update)
    {
        if (!frame.TryGetPropertyState(property, out var state))
            return;

        update(state);

        if (state.Version != version)
            return;

        frame.UpdatePropertyEntry(property, state);
        this.UpdateEntryAfterPropertyLoad(frame, property, state, dispatcher);
    }

    private void BeginPropertyLoad(
        TerminalExplorerFrame frame,
        PropertyInfo property,
        int version,
        TerminalDispatcher? dispatcher)
    {
        var target = frame.Target;
        this.StartBackgroundLoad(async token =>
        {
            try
            {
                var value = await Task.Run(() => property.GetValue(target), token).ConfigureAwait(false);
                var resolution = await this.ResolvePropertyValueAsync(property, value, token).ConfigureAwait(false);
                var preview = TerminalExplorerValueFormatter.Format(resolution.Value);
                var typeName = TerminalExplorerFrame.FormatTypeName(resolution.ValueType);

                this.ApplyPropertyState(
                    frame,
                    property,
                    version,
                    dispatcher,
                    state => state.Complete(version, preview, typeName, resolution.Value));
            }
            catch (Exception ex)
            {
                this.ApplyPropertyState(
                    frame,
                    property,
                    version,
                    dispatcher,
                    state => state.Fail(version, ex));
            }
        });
    }

    private async Task<PropertyValueResolution> ResolvePropertyValueAsync(
        PropertyInfo property,
        object? rawValue,
        CancellationToken token)
    {
        var declaredResultType = GetAsyncResultType(property.PropertyType);

        if (rawValue == null)
            return new(null, declaredResultType ?? property.PropertyType);

        if (TryCreateAwaitable(rawValue, property.PropertyType, out var task, out var asyncResultType))
        {
            await task.WaitAsync(token).ConfigureAwait(false);
            object? awaited = null;
            var hasResult = asyncResultType != null && TryExtractTaskResult(task, out awaited);
            var valueToUse = hasResult ? awaited : rawValue;
            var valueType = valueToUse?.GetType()
                            ?? asyncResultType
                            ?? declaredResultType
                            ?? property.PropertyType;

            return new(valueToUse, valueType);
        }

        return new(rawValue, rawValue.GetType());
    }

    private static bool TryCreateAwaitable(object value, Type declaredType, out Task task, out Type? resultType)
    {
        resultType = GetAsyncResultType(declaredType);

        if (value is Task existingTask)
        {
            resultType ??= GetAsyncResultType(existingTask.GetType());
            task = existingTask;

            return true;
        }

        if (IsValueTaskType(declaredType) || IsValueTaskType(value.GetType()))
        {
            task = ConvertValueTaskToTask(value);
            resultType ??= GetAsyncResultType(value.GetType());

            return true;
        }

        task = Task.CompletedTask;

        return false;
    }

    private static bool IsValueTaskType(Type type) =>
        type == typeof(ValueTask) || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ValueTask<>));

    private static Type? GetAsyncResultType(Type type)
    {
        for (var current = type; current != null; current = current.BaseType)
        {
            if (!current.IsGenericType)
                continue;

            var definition = current.GetGenericTypeDefinition();

            if (definition == typeof(Task<>) || definition == typeof(ValueTask<>))
                return current.GetGenericArguments()[0];
        }

        return null;
    }

    private static Task ConvertValueTaskToTask(object valueTask)
    {
        var method = valueTask.GetType().GetMethod("AsTask", BindingFlags.Instance | BindingFlags.Public);

        if (method == null)
            throw new InvalidOperationException("ValueTask does not expose AsTask.");

        var result = method.Invoke(valueTask, []);

        if (result is Task task)
            return task;

        throw new InvalidOperationException("ValueTask conversion failed.");
    }

    private static bool TryExtractTaskResult(Task task, out object? result)
    {
        var type = task.GetType();
        if (type.IsGenericType)
        {
            var property = type.GetProperty("Result");
            if (property != null)
            {
                result = property.GetValue(task);

                return true;
            }
        }

        result = null;

        return false;
    }

    private object? UnwrapMethodResult(object? result, Type returnType)
    {
        if (result == null)
            return null;

        if (TryCreateAwaitable(result, returnType, out var task, out var asyncResultType))
        {
            task.GetAwaiter().GetResult();

            if (asyncResultType != null && TryExtractTaskResult(task, out var awaited))
                return awaited;

            return asyncResultType != null ? null : result;
        }

        return result;
    }

    private static bool HasReturnValue(Type returnType)
    {
        if (returnType == typeof(void) || returnType == typeof(Task) || returnType == typeof(ValueTask))
            return false;

        if (typeof(Task).IsAssignableFrom(returnType) || IsValueTaskType(returnType))
            return GetAsyncResultType(returnType) != null;

        return true;
    }

    private void StartVisiblePropertyLoads(TerminalExplorerFrame? frame = null)
    {
        frame ??= this._frames.Peek();
        this.EnsurePropertyLoadSession(reset: false);
        var dispatcher = TerminalDispatcher.Current;
        var offset = Math.Max(0, this._viewport.Offset);
        var window = Math.Max(1, this._viewport.WindowSize);
        var start = Math.Max(0, offset - 2);
        var end = Math.Min(this._entries.Count - 1, offset + window + 2);

        foreach (var pending in frame.ConsumePendingLoads(load => load.EntryIndex >= start && load.EntryIndex <= end))
            if (pending.Property != null)
                this.BeginPropertyLoad(frame, pending.Property, pending.Version, dispatcher);
            else
                this.BeginItemPreviewLoad(frame, pending.Item, pending.EntryIndex, dispatcher);
    }

    private void UpdateEntryAfterPropertyLoad(
        TerminalExplorerFrame frame,
        PropertyInfo property,
        TerminalPropertyValueState state,
        TerminalDispatcher? dispatcher)
    {
        if (!ReferenceEquals(frame, this._frames.Peek()))
            return;

        for (var i = 0; i < this._entries.Count; i++)
        {
            if (this._entries[i].Property != property)
                continue;

            var entry = this._entries[i];
            var preview = state.Status switch
            {
                TerminalEntryLoadStatus.Loaded => state.Preview ?? string.Empty,
                TerminalEntryLoadStatus.Failed => $"Error: {state.Error?.GetBaseException().Message}",
                _ => TerminalExplorerVisuals.LoadingIndicator
            };
            var typeName = state.Status == TerminalEntryLoadStatus.Loaded ? state.TypeName ?? string.Empty : string.Empty;

            this._entries[i] = entry with
            {
                ValuePreview = preview,
                ClrTypeName = typeName,
                LoadStatus = state.Status
            };
        }

        (dispatcher ?? TerminalDispatcher.Current)?.Invalidate();
    }

    private void ApplyItemLoad(
        TerminalExplorerFrame frame,
        int entryIndex,
        string preview,
        string typeName,
        TerminalEntryLoadStatus status,
        TerminalDispatcher? dispatcher)
    {
        frame.UpdateItemEntry(entryIndex, preview, typeName, status);
        this.UpdateItemAfterLoad(entryIndex, preview, typeName, status, dispatcher);
    }

    private void BeginItemPreviewLoad(
        TerminalExplorerFrame frame,
        object? item,
        int entryIndex,
        TerminalDispatcher? dispatcher)
    {
        this.StartBackgroundLoad(async token =>
        {
            try
            {
                var preview = await Task.Run(() => TerminalExplorerValueFormatter.Format(item), token).ConfigureAwait(false);
                var typeName = item != null ? TerminalExplorerFrame.FormatTypeName(item.GetType()) : "null";
                this.ApplyItemLoad(frame, entryIndex, preview, typeName, TerminalEntryLoadStatus.Loaded, dispatcher);
            }
            catch (Exception)
            {
                this.ApplyItemLoad(frame, entryIndex, "[error]", string.Empty, TerminalEntryLoadStatus.Failed, dispatcher);
            }
        });
    }

    private void UpdateItemAfterLoad(
        int entryIndex,
        string preview,
        string typeName,
        TerminalEntryLoadStatus status,
        TerminalDispatcher? dispatcher)
    {
        if (entryIndex < 0 || entryIndex >= this._entries.Count)
            return;

        var entry = this._entries[entryIndex];

        if (entry.Kind != TerminalExplorerEntryKind.CollectionItem)
            return;

        this._entries[entryIndex] = entry with
        {
            ValuePreview = preview,
            ClrTypeName = typeName,
            LoadStatus = status
        };

        (dispatcher ?? TerminalDispatcher.Current)?.Invalidate();
    }

    private void MoveAndLoad(Action move)
    {
        move();
        this.StartVisiblePropertyLoads();
    }

    private void HandleProperty(object? target, PropertyInfo property, bool isEditable)
    {
        if (target == null)
        {
            this._statusMessage = "No target instance.";

            return;
        }

        var hasState = this._frames.Peek().TryGetPropertyState(property, out var state);
        if (hasState && state.Status != TerminalEntryLoadStatus.Loaded)
        {
            this._statusMessage = state.Status == TerminalEntryLoadStatus.Failed
                ? $"Failed to load {ResolveLabel(property)}: {state.Error?.GetBaseException().Message}"
                : $"{ResolveLabel(property)} is still loading...";

            return;
        }

        var value = hasState && state.Status == TerminalEntryLoadStatus.Loaded
            ? state.Value
            : property.GetValue(target);
        var type = hasState && state.Status == TerminalEntryLoadStatus.Loaded && state.Value != null
            ? state.Value.GetType()
            : property.PropertyType;

        if (!IsEditablePrimitive(type))
        {
            if (value == null)
            {
                this._statusMessage = "Value is null.";

                return;
            }

            this.PushFrame(value, ResolveLabel(property));

            return;
        }

        if (!isEditable)
        {
            this._statusMessage = $"{ResolveLabel(property)}: {TerminalExplorerValueFormatter.Format(value)}";

            return;
        }

        if (this.TryEditTemporalProperty(target, property))
        {
            this.RefreshEntries(force: true);

            return;
        }

        var field = TerminalPropertyFieldDefinitionFactory.TryCreate(property, target);
        if (field == null)
        {
            this._statusMessage = "Property is not editable.";

            return;
        }

        var dialog = new TerminalFormDialog([field]);
        var defaults = new Dictionary<DataBindingKey, string?> { [field.Key] = ConvertToString(value) };
        var map = dialog.Prompt(_ => defaults[field.Key]);

        if (!map.TryGetValue(field.Key, out var raw))
            return;

        try
        {
            var converted = ConvertFromString(raw, property.PropertyType);
            property.SetValue(target, converted);
        }
        catch (Exception ex)
        {
            this.ShowError(ex);

            return;
        }

        this._statusMessage = $"{field.Label} updated.";
        this.RefreshEntries(force: true);
    }

    private void ToggleSelectionMark()
    {
        if (this._entries.Count == 0)
            return;

        if (!this.TryGetCollectionFrame(out var frame))
            return;

        var index = Math.Clamp(this._window.Selection, 0, this._entries.Count - 1);
        var isMarked = frame.SelectedIndices.Contains(index);
        frame.SetSelection(index, !isMarked);

        var count = frame.SelectedIndices.Count;
        this._statusMessage = count > 0 ? $"{count} item(s) marked." : string.Empty;
        this._shiftSelectionActive = false;
        this.RefreshEntries(force: true);
    }

    private void HandleMethod(object? target, MethodInfo method)
    {
        if (target == null)
        {
            this._statusMessage = "No target instance.";

            return;
        }

        if (!this._executingMethods.Add(method))
        {
            this._statusMessage = $"{method.Name} is already running.";

            return;
        }

        this.RefreshEntries();
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var result = method.Invoke(target, []);
            var hasReturnValue = HasReturnValue(method.ReturnType);
            var resolvedResult = this.UnwrapMethodResult(result, method.ReturnType);

            if (hasReturnValue)
                this._statusMessage = $"{method.Name}: {TerminalExplorerValueFormatter.Format(resolvedResult)}";
            else
                this._statusMessage = $"{method.Name} executed.";
        }
        catch (Exception ex)
        {
            this.ShowError(ex);
        }
        finally
        {
            var elapsed = stopwatch.ElapsedMilliseconds;
            if (elapsed < 200)
                Thread.Sleep((int)(200 - elapsed));

            this._executingMethods.Remove(method);
            this.RefreshEntries(force: true);
        }
    }

    private void PushFrame(object? target, string title) => this.PushFrameInternal(TerminalExplorerFrame.Create(target, title));

    private void PushFrame(TerminalExplorerFrame frame) => this.PushFrameInternal(frame);

    private void PushFrameInternal(TerminalExplorerFrame frame)
    {
        this.ResetPropertyLoadSession();
        frame.ClearSelection();
        this._frames.Push(frame);
        this.RefreshEntries(force: true);
        this.EnsureAutoEnterCurrentFrame();
    }

    private bool GoBack()
    {
        if (this._frames.Count <= 1)
            return false;

        this._frames.Pop();
        this.RefreshEntries(force: true);
        this.EnsureAutoEnterCurrentFrame();
        this.StartVisiblePropertyLoads();
        TerminalDispatcher.Current?.Invalidate();

        return false;
    }

    private void EnsureAutoEnterCurrentFrame()
    {
        var frame = this._frames.Peek();
        while (frame.TryGetAutoItems(out var enumerable))
        {
            this._frames.Pop();
            frame = TerminalExplorerFrame.CreateItemsView(enumerable, frame.Title, frame.SourceEntry);
            frame.ClearSelection();
            this._frames.Push(frame);
            this.RefreshEntries(force: true);
        }
    }

    private static bool IsEditablePrimitive(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;

        return type.IsPrimitive
               || type == typeof(string)
               || type == typeof(decimal)
               || type == typeof(DateTime)
               || type == typeof(DateOnly)
               || type == typeof(Guid)
               || type.IsEnum;
    }

    private static string? ConvertToString(object? value)
    {
        if (value == null)
            return null;

        if (value is DateTime dt)
            return dt.ToString("u");

        if (value is DateOnly dateOnly)
            return dateOnly.ToString("O");

        return value.ToString();
    }

    private static object? ConvertFromString(string? value, Type targetType)
    {
        var nullable = Nullable.GetUnderlyingType(targetType);
        if (nullable != null)
        {
            targetType = nullable;

            if (string.IsNullOrWhiteSpace(value))
                return null;
        }

        if (targetType == typeof(string))
            return value ?? string.Empty;

        if (targetType == typeof(Guid))
            return Guid.Parse(value ?? string.Empty);

        if (targetType == typeof(DateTime))
            return DateTime.Parse(value ?? string.Empty);

        if (targetType == typeof(DateOnly))
            return DateOnly.Parse(value ?? string.Empty);

        if (targetType.IsEnum)
            return Enum.Parse(targetType, value ?? string.Empty, true);

        return Convert.ChangeType(value, targetType);
    }

    private bool TryEditTemporalProperty(object target, PropertyInfo property, bool suppressStatus = false)
    {
        var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        if (type == typeof(DateTime))
        {
            var current = (DateTime)(property.GetValue(target) ?? DateTime.Now);
            var outcome = this.EditDateTimeInteractive(ref current);
            if (outcome == MenuExitReason.Save)
            {
                property.SetValue(target, current);
                if (!suppressStatus)
                    this._statusMessage = $"{ResolveLabel(property)} updated.";
            }

            return true;
        }

        if (type == typeof(DateOnly))
        {
            var current = (DateOnly)(property.GetValue(target) ?? DateOnly.FromDateTime(DateTime.Now));
            var outcome = this.EditDateOnlyInteractive(ref current);
            if (outcome == MenuExitReason.Save)
            {
                property.SetValue(target, current);
                if (!suppressStatus)
                    this._statusMessage = $"{ResolveLabel(property)} updated.";
            }

            return true;
        }

        return false;
    }

    private static string ResolveLabel(MemberInfo member)
    {
        var attribute = member.GetCustomAttribute<TerminalDisplayAttribute>();

        return string.IsNullOrWhiteSpace(attribute?.Label) ? member.Name : attribute!.Label!;
    }

    private string FormatBreadcrumb()
    {
        var frames = this._frames.ToArray();
        Array.Reverse(frames);
        var crumbs = frames.Select(frame => frame.Title).ToList();

        if (crumbs.Count == 0)
            return string.Empty;

        var text = string.Join(" / ", crumbs);
        var maxWidth = Math.Max(20, GetConsoleWidth() - 4);
        while (text.Length > maxWidth && crumbs.Count > 1)
        {
            crumbs.RemoveAt(0);
            text = "... / " + string.Join(" / ", crumbs);
        }

        return text.Length <= maxWidth ? text : text[..maxWidth];
    }

    private void UpdateViewportWindowSize()
    {
        var height = GetConsoleHeight();
        var reserved = 8;
        var desired = Math.Max(3, height - reserved);
        if (this._viewport.WindowSize != desired)
            this._viewport.WindowSize = desired;
    }

    private static int GetConsoleWidth() => TerminalDimensions.GetWindowWidthOrDefault();

    private static int GetConsoleHeight() => TerminalDimensions.GetWindowHeightOrDefault();

    private bool HasDictionaryContext() => IsDictionaryFrame(this._frames.Peek());

    private bool HasListContext()
    {
        var frame = this._frames.Peek();

        return frame.Kind == TerminalExplorerFrameKind.CollectionItems && frame.Target is IList && !IsDictionaryFrame(frame);
    }

    private bool CanAddDictionaryEntry()
    {
        if (!this.TryGetActiveDictionary(out _, out var keyType, out var valueType))
            return false;

        return CanInstantiateType(keyType) && CanInstantiateType(valueType);
    }

    private string BuildNavigationHint()
    {
        var primary = new[]
        {
            FormatButton("↑/↓", "Move"),
            FormatButton("PgUp/PgDn", "Page"),
            FormatButton("Home/End", "Jump"),
            FormatButton("Enter/→", "Open"),
            FormatButton("Esc/←", "Back")
        };

        var lines = new List<string> { JoinButtons(primary) };
        var frame = this._frames.Peek();
        if (frame.Kind == TerminalExplorerFrameKind.CollectionItems)
        {
            if (IsDictionaryFrame(frame))
            {
                var segments = new List<string>
                {
                    FormatButton("Shift+↑/↓", "Mark"),
                    FormatButton("Space", "Toggle"),
                    FormatButton("K", "Edit key"),
                    FormatButton("Del", "Delete (confirm)")
                };

                if (this.CanAddDictionaryEntry())
                    segments.Insert(1, FormatButton("A", "Add"));

                lines.Add(JoinButtons(segments));
            }
            else if (frame.Target is IList list)
            {
                var segments = new List<string>
                {
                    FormatButton("Shift+↑/↓", "Mark"),
                    FormatButton("Space", "Toggle")
                };

                if (list.Count == 0)
                {
                    segments.Add(FormatButton("A", "Add"));
                }
                else
                {
                    segments.Add(FormatButton("I", "Insert before"));
                    segments.Add(FormatButton("O", "Insert after"));
                }

                segments.Add(FormatButton("Del", "Delete (confirm)"));
                lines.Add(JoinButtons(segments));
            }
        }

        var customButtons = this.GetVisibleButtons();
        if (customButtons.Count > 0)
        {
            var customLine = customButtons
                .Select(button => FormatButton(button.Key.ToString(), button.Label));
            lines.Add(JoinButtons(customLine));
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string FormatButton(string keys, string action) => $"[{keys}] {action}";

    private static string JoinButtons(IEnumerable<string> buttons) => " " + string.Join(" │ ", buttons);

    private static bool IsDictionaryFrame(TerminalExplorerFrame frame) => frame.Target is IDictionary;

    private static bool CanInstantiateType(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;

        return IsSimpleElementType(type)
               || type.GetConstructor(Type.EmptyTypes) != null
               || HasExplorerConstructor(type);
    }

    private static bool HasExplorerConstructor(Type type) => FindExplorerConstructor(type) != null;

    private object? CreateInstanceForExplorer(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;

        var ctor = FindExplorerConstructor(type);
        if (ctor != null)
            try
            {
                return ctor.Invoke([this._owner]);
            }
            catch { }

        if (type.GetConstructor(Type.EmptyTypes) == null)
            return null;

        try
        {
            return Activator.CreateInstance(type);
        }
        catch
        {
            return null;
        }
    }

    private static ConstructorInfo? FindExplorerConstructor(Type type)
    {
        return type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(ctor =>
            {
                var parameters = ctor.GetParameters();

                return parameters.Length == 1 && typeof(TerminalObjectExplorer).IsAssignableFrom(parameters[0].ParameterType);
            });
    }

    private IReadOnlyList<TerminalExplorerButton> GetVisibleButtons()
    {
        if (this._buttons.Count == 0)
            return [];

        var list = new List<TerminalExplorerButton>();
        foreach (var button in this._buttons)
            if (button != null && button.IsVisible(this._owner, this))
                list.Add(button);

        return list;
    }

    private void InsertRelative(bool insertAfter)
    {
        try
        {
            if (this.TryGetActiveDictionary(out _, out _, out _))
            {
                this._statusMessage = "Use Add to insert dictionary entries.";

                return;
            }

            if (!this.TryGetListContext(out var listContext))
            {
                this._statusMessage = "Select a list to insert items.";

                return;
            }

            var anchor = listContext.List.Count == 0
                ? 0
                : Math.Clamp(this._window.Selection, 0, Math.Max(0, this._entries.Count - 1));
            var insertIndex = insertAfter ? Math.Min(anchor + 1, listContext.List.Count) : anchor;

            var created = this.PromptNewElement(
                listContext.ElementType,
                $"New {listContext.ElementType.Name}",
                listContext.AllowsNullElements,
                () => this.Confirm("Cancel inserting item?"),
                out var editResult);

            if (editResult == EditInteractionResult.Cancel)
            {
                this._statusMessage = "Insert cancelled.";

                return;
            }

            var valueToInsert = created;
            if (editResult == EditInteractionResult.Delete && listContext.AllowsNullElements)
            {
                valueToInsert = null;
                this._statusMessage = "Item set to null.";
            }
            else if (valueToInsert == null && !listContext.AllowsNullElements)
            {
                this._statusMessage = "Value is required.";

                return;
            }
            else
            {
                this._statusMessage = $"Inserted at index {insertIndex}.";
            }

            listContext.List.Insert(insertIndex, valueToInsert);
            listContext.Frame.ClearSelection();
            this._window.MoveTo(insertIndex);
            this.RefreshEntries(force: true);
        }
        catch (Exception ex)
        {
            this.ShowError(ex);
        }
    }

    private void DeleteSelection()
    {
        if (this._entries.Count == 0)
        {
            this._statusMessage = "Nothing to delete.";

            return;
        }

        var frame = this._frames.Peek();
        var targets = frame.SelectedIndices.Count > 0
            ? frame.SelectedIndices.OrderByDescending(i => i).ToList()
            : [Math.Clamp(this._window.Selection, 0, this._entries.Count - 1)];

        if (targets.Count == 0)
        {
            this._statusMessage = "Nothing to delete.";

            return;
        }

        if (frame.Target is IDictionary dictionary)
        {
            this.DeleteDictionaryEntries(dictionary, targets);

            return;
        }

        if (!this.TryGetActiveList(out var list, out _))
            return;

        if (!this.Confirm($"Delete {targets.Count} item(s)?"))
        {
            this._statusMessage = "Deletion cancelled.";

            return;
        }

        foreach (var index in targets)
            if (index >= 0 && index < list.Count)
                list.RemoveAt(index);

        frame.RemoveSelectionsAndShift(targets);
        var fallbackIndex = targets.Last();
        var nextIndex = list.Count == 0
            ? 0
            : Math.Clamp(Math.Min(fallbackIndex, list.Count - 1), 0, list.Count - 1);
        this._window.MoveTo(nextIndex);
        this._statusMessage = $"Deleted {targets.Count} item(s).";
        this.RefreshEntries(force: true);
    }

    private void DeleteDictionaryEntries(IDictionary dictionary, List<int> targetIndices)
    {
        var keys = targetIndices
            .Where(i => i >= 0 && i < this._entries.Count)
            .Select(i => this._entries[i].DictionaryKey)
            .Where(key => key != null)
            .ToList();

        if (keys.Count == 0)
        {
            this._statusMessage = "Nothing to delete.";

            return;
        }

        if (!this.Confirm($"Delete {keys.Count} entry(ies)?"))
        {
            this._statusMessage = "Deletion cancelled.";

            return;
        }

        foreach (var key in keys)
            dictionary.Remove(key!);

        this._frames.Peek().RemoveSelectionsAndShift(targetIndices);
        this._window.MoveTo(0);
        this._statusMessage = $"Deleted {keys.Count} item(s).";
        this.RefreshEntries(force: true);
    }

    private void AddEntry()
    {
        try
        {
            if (this.TryGetActiveDictionary(out var dictionary, out var keyType, out var valueType))
            {
                if (!CanInstantiateType(keyType) || !CanInstantiateType(valueType))
                {
                    this._statusMessage = "Dictionary entries cannot be created for this type.";

                    return;
                }

                this.AddDictionaryEntry(dictionary, keyType, valueType);

                return;
            }

            if (!this.TryGetListContext(out var listContext))
            {
                this._statusMessage = "Select a list to add items.";

                return;
            }

            var created = this.PromptNewElement(
                listContext.ElementType,
                $"New {listContext.ElementType.Name}",
                listContext.AllowsNullElements,
                () => this.Confirm("Cancel adding item?"),
                out var editResult);

            if (editResult == EditInteractionResult.Cancel)
            {
                this._statusMessage = "Add cancelled.";

                return;
            }

            var valueToInsert = created;
            if (editResult == EditInteractionResult.Delete && listContext.AllowsNullElements)
            {
                valueToInsert = null;
                this._statusMessage = "Item set to null.";
            }
            else if (valueToInsert == null && !listContext.AllowsNullElements)
            {
                this._statusMessage = "Value is required.";

                return;
            }
            else if (this._statusMessage.Length == 0)
            {
                this._statusMessage = "Item added.";
            }

            var insertIndex = listContext.List.Count;
            listContext.List.Insert(insertIndex, valueToInsert);
            this._window.MoveTo(insertIndex);
            this.RefreshEntries(force: true);
        }
        catch (Exception ex)
        {
            this.ShowError(ex);
        }
    }

    private void AddDictionaryEntry(IDictionary dictionary, Type keyType, Type valueType)
    {
        var key = this.PromptValueForType(keyType, "Dictionary key", true, null);
        if (key == null)
        {
            this._statusMessage = "Add cancelled.";

            return;
        }

        if (dictionary.Contains(key))
        {
            this._statusMessage = "Key already exists.";

            return;
        }

        var allowNullValue = AllowsNullAssignment(valueType, null);
        var value = this.PromptNewElement(
            valueType,
            "Dictionary value",
            allowNullValue,
            () => this.Confirm("Cancel adding entry?"),
            out var editResult);

        if (editResult == EditInteractionResult.Cancel)
        {
            this._statusMessage = "Add cancelled.";

            return;
        }

        var entryValue = editResult == EditInteractionResult.Delete ? null : value;
        if (entryValue == null && editResult != EditInteractionResult.Delete && !allowNullValue)
        {
            this._statusMessage = "Value is required.";

            return;
        }

        dictionary.Add(key, entryValue);
        this._statusMessage = editResult == EditInteractionResult.Delete ? "Entry added as null." : "Entry added.";
        this.RefreshEntries(force: true);
    }

    private void EditDictionaryKey()
    {
        if (!this.TryGetActiveDictionary(out var dictionary, out var keyType, out _))
        {
            this._statusMessage = "Not a dictionary.";

            return;
        }

        if (this._entries.Count == 0)
        {
            this._statusMessage = "Nothing selected.";

            return;
        }

        var index = Math.Clamp(this._window.Selection, 0, this._entries.Count - 1);
        var entry = this._entries[index];
        var oldKey = entry.DictionaryKey;
        if (oldKey == null)
        {
            this._statusMessage = "Entry has no key.";

            return;
        }

        var newKey = this.PromptValueForType(keyType, "Dictionary key", true, oldKey);

        if (newKey == null)
        {
            this._statusMessage = "Key edit cancelled.";

            return;
        }

        if (Equals(newKey, oldKey))
        {
            this._statusMessage = "Key unchanged.";

            return;
        }

        if (dictionary.Contains(newKey))
        {
            this._statusMessage = "Key already exists.";

            return;
        }

        var value = dictionary[oldKey];
        dictionary.Remove(oldKey);
        dictionary[newKey] = value;
        this._statusMessage = "Key updated.";
        this.RefreshEntries(force: true);
    }

    private bool TryGetActiveList(out IList list, out Type elementType)
    {
        if (this.TryGetListContext(out var context))
        {
            list = context.List;
            elementType = context.ElementType;

            return true;
        }

        list = Array.Empty<object>();
        elementType = typeof(object);

        return false;
    }

    private bool TryGetListContext(out ListContext context)
    {
        context = default;
        var frame = this._frames.Peek();

        if (frame.Kind != TerminalExplorerFrameKind.CollectionItems || frame.Target is not IList ilist)
            return false;

        var elementType = ResolveElementType(ilist.GetType());

        if (elementType == null)
            return false;

        var allowsNull = AllowsNullAssignment(elementType, frame.SourceEntry?.Property);
        context = new(frame, ilist, elementType, allowsNull);

        return true;
    }


    private static Type? ResolveElementType(Type collectionType)
    {
        if (collectionType.IsArray)
            return collectionType.GetElementType();

        if (collectionType.IsGenericType)
            return collectionType.GetGenericArguments().FirstOrDefault();

        var iface = collectionType.GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IList<>));

        return iface?.GetGenericArguments().FirstOrDefault();
    }

    private object? PromptNewElement(
        Type elementType,
        string label,
        bool allowDelete,
        Func<bool>? cancelHandler,
        out EditInteractionResult result)
    {
        result = EditInteractionResult.Save;
        var actualType = Nullable.GetUnderlyingType(elementType) ?? elementType;
        var promptLabel = string.IsNullOrWhiteSpace(label) ? $"New {actualType.Name}" : label;

        if (IsSimpleElementType(actualType))
        {
            var value = this.PromptSimpleElement(actualType, promptLabel);
            if (value == null)
                result = EditInteractionResult.Cancel;

            return value;
        }

        var instance = this.CreateInstanceForExplorer(actualType);
        if (instance == null)
        {
            this._statusMessage = "Unable to create instance.";
            result = EditInteractionResult.Cancel;

            return null;
        }

        var menuResult = this.EditObjectViaMenu(
            instance,
            promptLabel,
            cancelHandler != null,
            true,
            cancelHandler,
            allowDelete);

        result = menuResult;

        return menuResult == EditInteractionResult.Save ? instance : null;
    }

    private object? PromptValueForType(
        Type type,
        string label,
        bool allowCancel = true,
        object? currentValue = null)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (IsSimpleElementType(type))
            while (true)
            {
                var value = this.PromptSimpleElement(type, label, ConvertToString(currentValue), allowCancel);

                if (value != null)
                    return value;

                if (allowCancel)
                    return null;

                this._statusMessage = "Value is required.";
            }

        while (true)
        {
            var instance = this.CreateInstanceForExplorer(type);
            if (instance == null)
            {
                this._statusMessage = $"Unable to create {type.Name}.";

                return null;
            }

            if (currentValue != null)
                CopyEditableProperties(currentValue, instance);

            var menuResult = this.EditObjectViaMenu(
                instance,
                $"Edit {label}",
                allowCancel,
                !allowCancel);

            if (menuResult == EditInteractionResult.Save)
                return instance;

            if (menuResult == EditInteractionResult.Delete)
                return null;

            if (allowCancel)
                return null;

            this._statusMessage = "Value is required.";
        }
    }

    private bool TryGetActiveDictionary(out IDictionary dictionary, out Type keyType, out Type valueType)
    {
        var frame = this._frames.Peek();
        if (frame.Kind != TerminalExplorerFrameKind.CollectionItems || frame.Target is not IDictionary dict)
        {
            dictionary = default!;
            keyType = typeof(object);
            valueType = typeof(object);

            return false;
        }

        dictionary = dict;
        ResolveDictionaryTypes(dict.GetType(), out keyType, out valueType);

        return true;
    }

    private static void ResolveDictionaryTypes(Type type, out Type keyType, out Type valueType)
    {
        keyType = typeof(object);
        valueType = typeof(object);

        if (type.IsGenericType)
        {
            var args = type.GetGenericArguments();
            if (args.Length == 2)
            {
                keyType = args[0];
                valueType = args[1];

                return;
            }
        }

        var iface = type.GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDictionary<,>));
        if (iface != null)
        {
            var args = iface.GetGenericArguments();
            if (args.Length == 2)
            {
                keyType = args[0];
                valueType = args[1];
            }
        }
    }

    private static bool IsSimpleElementType(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;

        return type.IsPrimitive
               || type.IsEnum
               || type == typeof(string)
               || type == typeof(Guid)
               || type == typeof(DateTime)
               || type == typeof(DateOnly)
               || type == typeof(DateTimeOffset)
               || type == typeof(decimal);
    }

    private static bool AllowsNullAssignment(Type type, MemberInfo? member)
    {
        if (member?.GetCustomAttribute<TerminalNotNullAttribute>() != null)
            return false;

        var underlying = Nullable.GetUnderlyingType(type);

        if (underlying != null)
            return true;

        return !type.IsValueType;
    }

    private object? PromptSimpleElement(Type elementType, string? label = null, string? defaultValue = null, bool allowCancel = true)
    {
        var field = CreateFieldDefinitionForElement(elementType, label);
        var dialog = new TerminalFormDialog([field]);
        var defaults = new Dictionary<DataBindingKey, string?> { [field.Key] = defaultValue };
        while (true)
        {
            var hasValue = false;
            object? converted = null;
            var map = dialog.Prompt(
                _ => defaults[field.Key],
                (_, raw) =>
                {
                    try
                    {
                        converted = ConvertFromString(raw, elementType);
                        hasValue = true;

                        return null;
                    }
                    catch (Exception ex)
                    {
                        converted = null;
                        hasValue = false;

                        return ex.GetBaseException().Message;
                    }
                },
                field => field.Options);

            if (map.Count == 0)
                return null;

            if (hasValue)
                return converted;
        }
    }

    private static TerminalFormFieldDefinition CreateFieldDefinitionForElement(Type elementType, string? label = null)
    {
        elementType = Nullable.GetUnderlyingType(elementType) ?? elementType;
        var options = Array.Empty<string>();
        var editor = TerminalFormFieldEditor.Text;

        if (elementType.IsEnum)
        {
            editor = TerminalFormFieldEditor.Select;
            options = Enum.GetNames(elementType);
        }
        else if (elementType == typeof(bool))
        {
            editor = TerminalFormFieldEditor.Select;
            options = [bool.TrueString, bool.FalseString];
        }

        return new()
        {
            Key = DataBindingKey.From("value"),
            Label = label ?? $"New {elementType.Name}",
            Editor = editor,
            Options = options,
            IsRequired = true
        };
    }

    private void ApplyShiftSelection(bool shiftHeld, bool beforeMove = false)
    {
        if (!shiftHeld)
        {
            this._shiftSelectionActive = false;

            return;
        }

        if (this._entries.Count == 0 || !this.TryGetCollectionFrame(out var frame))
        {
            this._shiftSelectionActive = false;

            return;
        }

        var index = Math.Clamp(this._window.Selection, 0, this._entries.Count - 1);

        if (!this._shiftSelectionActive)
        {
            var wasMarked = frame.SelectedIndices.Contains(index);
            this._shiftSelectionState = !wasMarked;
            this._shiftSelectionActive = true;
            frame.SetSelection(index, this._shiftSelectionState);
            this._statusMessage = $"{frame.SelectedIndices.Count} item(s) marked.";
            this.RefreshEntries(force: true);

            if (beforeMove)
                return;
        }

        var current = frame.SelectedIndices.Contains(index);
        if (current == this._shiftSelectionState)
        {
            if (beforeMove)
            {
                frame.SetSelection(index, this._shiftSelectionState);
                this._statusMessage = $"{frame.SelectedIndices.Count} item(s) marked.";
                this.RefreshEntries(force: true);
            }

            return;
        }

        frame.SetSelection(index, this._shiftSelectionState);
        this._statusMessage = $"{frame.SelectedIndices.Count} item(s) marked.";
        this.RefreshEntries(force: true);
    }

    private bool TryGetCollectionFrame(out TerminalExplorerFrame frame)
    {
        frame = this._frames.Peek();

        return frame.Kind == TerminalExplorerFrameKind.CollectionItems;
    }

    private EditInteractionResult EditObjectViaMenu(
        object target,
        string title,
        bool allowCancel,
        bool escCommits = false,
        Func<bool>? cancelHandler = null,
        bool allowDelete = false)
    {
        var descriptors = CollectEditableDescriptors(target);
        if (descriptors.Count == 0)
        {
            RunExternal(() =>
            {
                Console.WriteLine("No editable properties.");
                Console.WriteLine("Press Enter to continue...");
                Console.ReadLine();
            });

            return EditInteractionResult.Save;
        }

        var menuItems = descriptors
            .Select(d => new MenuItem(
                d.Field.Label,
                () => TerminalExplorerValueFormatter.Format(d.Property.GetValue(target)),
                () =>
                {
                    this.EditDescriptorInteractive(target, d);

                    return MenuItemResult.Continue;
                }))
            .ToList();

        var exitReason = this.RunMenu(
            title,
            menuItems,
            allowCancel,
            false,
            escCommits,
            cancelHandler,
            allowDelete ? () => this.Confirm("Set value to null?") : null);

        return exitReason switch
        {
            MenuExitReason.Save => EditInteractionResult.Save,
            MenuExitReason.Delete => EditInteractionResult.Delete,
            _ => EditInteractionResult.Cancel
        };
    }

    private static List<(TerminalFormFieldDefinition Field, PropertyInfo Property)> CollectEditableDescriptors(object target)
    {
        var descriptors = new List<(TerminalFormFieldDefinition Field, PropertyInfo Property)>();
        var properties = target.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        foreach (var property in properties)
        {
            var definition = TerminalPropertyFieldDefinitionFactory.TryCreate(property, target);
            if (definition != null)
                descriptors.Add((definition, property));
        }

        return descriptors;
    }

    private void EditDescriptorInteractive(object target, (TerminalFormFieldDefinition Field, PropertyInfo Property) descriptor)
    {
        if (this.TryEditTemporalProperty(target, descriptor.Property, true))
            return;

        var dialog = new TerminalFormDialog([descriptor.Field]);
        var defaults = new Dictionary<DataBindingKey, string?>
            { [descriptor.Field.Key] = ConvertToString(descriptor.Property.GetValue(target)) };
        var map = dialog.Prompt(_ => defaults[descriptor.Field.Key], null, _ => descriptor.Field.Options);

        if (!map.TryGetValue(descriptor.Field.Key, out var raw))
            return;

        try
        {
            var converted = ConvertFromString(raw, descriptor.Property.PropertyType);
            descriptor.Property.SetValue(target, converted);
        }
        catch (Exception ex)
        {
            this.ShowError(ex);
        }
    }

    private static void CopyEditableProperties(object source, object destination)
    {
        if (source == null || destination == null)
            return;

        var sourceType = source.GetType();
        var destinationType = destination.GetType();
        var properties = sourceType.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        foreach (var property in properties)
        {
            if (!property.CanRead)
                continue;

            var targetProperty = destinationType.GetProperty(
                property.Name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            if (targetProperty?.CanWrite != true)
                continue;

            try
            {
                var value = property.GetValue(source);
                targetProperty.SetValue(destination, value);
            }
            catch { }
        }
    }

    private bool Confirm(string message)
    {
        return RunExternal(() =>
        {
            Console.Write($"{message} [y/N]: ");
            var key = Console.ReadKey(intercept: true).Key;
            Console.WriteLine();

            return key == ConsoleKey.Y;
        });
    }

    private MenuExitReason RunMenu(
        string title,
        IReadOnlyList<MenuItem> items,
        bool allowCancel,
        bool showExitCommands = true,
        bool escCommits = false,
        Func<bool>? cancelHandler = null,
        Func<bool>? deleteHandler = null)
    {
        var workingItems = items?.ToList() ?? [];
        if (workingItems.Count == 0)
        {
            Console.WriteLine("Nothing to edit.");
            Console.WriteLine("Press Enter to continue...");
            Console.ReadLine();

            return MenuExitReason.Save;
        }

        if (showExitCommands)
        {
            workingItems.Add(new("[Save & Close]", () => string.Empty, () => MenuItemResult.Save));
            if (allowCancel)
                workingItems.Add(new("[Cancel]", () => string.Empty, () => MenuItemResult.Cancel));
        }

        var selection = 0;
        while (true)
        {
            Console.Clear();
            Console.WriteLine(title);
            Console.WriteLine(new string('=', title.Length));
            for (var i = 0; i < workingItems.Count; i++)
            {
                var item = workingItems[i];
                var prefix = i == selection ? "> " : "  ";
                var valueText = item.ValueFactory() ?? string.Empty;
                var suffix = string.IsNullOrWhiteSpace(valueText) ? string.Empty : $": {valueText}";
                Console.WriteLine($"{prefix}{item.Label}{suffix}");
            }

            Console.WriteLine();
            var footerBuilder = new StringBuilder("[↑/↓] Navigate  [Enter] Edit/Select");
            if (showExitCommands)
            {
                footerBuilder.Append("  [Ctrl+Enter] Save  ");
                footerBuilder.Append(allowCancel ? "[Esc] Cancel" : "[Esc] Done");
            }
            else
            {
                footerBuilder.Append(escCommits ? "  [Esc/←] Back" : "  [Esc/←] Cancel");
            }

            if (deleteHandler != null)
                footerBuilder.Append("  [D] Delete");

            if (cancelHandler != null)
                footerBuilder.Append("  [C] Cancel");

            Console.WriteLine(footerBuilder.ToString());
            var keyInfo = Console.ReadKey(intercept: true);

            if (keyInfo.Key == ConsoleKey.Enter && (keyInfo.Modifiers & ConsoleModifiers.Control) != 0)
                return MenuExitReason.Save;

            switch (keyInfo.Key)
            {
                case ConsoleKey.UpArrow:
                    selection = selection <= 0 ? workingItems.Count - 1 : selection - 1;

                    break;
                case ConsoleKey.DownArrow:
                    selection = (selection + 1) % workingItems.Count;

                    break;
                case ConsoleKey.Enter:
                case ConsoleKey.RightArrow:
                    var outcome = workingItems[selection].Execute();

                    if (outcome == MenuItemResult.Save)
                        return MenuExitReason.Save;

                    if (outcome == MenuItemResult.Cancel)
                        return allowCancel ? MenuExitReason.Cancel : MenuExitReason.Save;

                    break;
                case ConsoleKey.C when cancelHandler != null:
                    if (cancelHandler())
                        return MenuExitReason.Cancel;

                    break;
                case ConsoleKey.D when deleteHandler != null:
                    if (deleteHandler())
                        return MenuExitReason.Delete;

                    break;
                case ConsoleKey.LeftArrow:
                case ConsoleKey.Escape:
                    if (showExitCommands)
                        return allowCancel ? MenuExitReason.Cancel : MenuExitReason.Save;

                    return escCommits ? MenuExitReason.Save : allowCancel ? MenuExitReason.Cancel : MenuExitReason.Save;
            }
        }
    }

    private MenuExitReason EditDateTimeInteractive(ref DateTime value)
    {
        var working = value;
        var outcome = this.RunMenu(
            "Edit DateTime",
            new List<MenuItem>
            {
                new(
                    "Year",
                    () => working.Year.ToString(),
                    () =>
                    {
                        if (!this.PromptInt("Year", working.Year, 1, 9999, out var year))
                            return MenuItemResult.Continue;

                        var day = Math.Min(working.Day, DateTime.DaysInMonth(year, working.Month));
                        working = new(
                            year,
                            working.Month,
                            day,
                            working.Hour,
                            working.Minute,
                            working.Second,
                            working.Millisecond,
                            working.Kind);

                        return MenuItemResult.Continue;
                    }),
                new(
                    "Month",
                    () => working.Month.ToString(),
                    () =>
                    {
                        if (!this.PromptInt("Month", working.Month, 1, 12, out var month))
                            return MenuItemResult.Continue;

                        var day = Math.Min(working.Day, DateTime.DaysInMonth(working.Year, month));
                        working = new(
                            working.Year,
                            month,
                            day,
                            working.Hour,
                            working.Minute,
                            working.Second,
                            working.Millisecond,
                            working.Kind);

                        return MenuItemResult.Continue;
                    }),
                new(
                    "Day",
                    () => working.Day.ToString(),
                    () =>
                    {
                        var maxDay = DateTime.DaysInMonth(working.Year, working.Month);

                        if (!this.PromptInt("Day", working.Day, 1, maxDay, out var dayValue))
                            return MenuItemResult.Continue;

                        working = new(
                            working.Year,
                            working.Month,
                            dayValue,
                            working.Hour,
                            working.Minute,
                            working.Second,
                            working.Millisecond,
                            working.Kind);

                        return MenuItemResult.Continue;
                    }),
                new(
                    "Hour",
                    () => working.Hour.ToString(),
                    () =>
                    {
                        if (!this.PromptInt("Hour", working.Hour, 0, 23, out var hour))
                            return MenuItemResult.Continue;

                        working = new(
                            working.Year,
                            working.Month,
                            working.Day,
                            hour,
                            working.Minute,
                            working.Second,
                            working.Millisecond,
                            working.Kind);

                        return MenuItemResult.Continue;
                    }),
                new(
                    "Minute",
                    () => working.Minute.ToString(),
                    () =>
                    {
                        if (!this.PromptInt("Minute", working.Minute, 0, 59, out var minute))
                            return MenuItemResult.Continue;

                        working = new(
                            working.Year,
                            working.Month,
                            working.Day,
                            working.Hour,
                            minute,
                            working.Second,
                            working.Millisecond,
                            working.Kind);

                        return MenuItemResult.Continue;
                    }),
                new(
                    "Second",
                    () => working.Second.ToString(),
                    () =>
                    {
                        if (!this.PromptInt("Second", working.Second, 0, 59, out var second))
                            return MenuItemResult.Continue;

                        working = new(
                            working.Year,
                            working.Month,
                            working.Day,
                            working.Hour,
                            working.Minute,
                            second,
                            working.Millisecond,
                            working.Kind);

                        return MenuItemResult.Continue;
                    }),
                new(
                    "Millisecond",
                    () => working.Millisecond.ToString(),
                    () =>
                    {
                        if (!this.PromptInt("Millisecond", working.Millisecond, 0, 999, out var ms))
                            return MenuItemResult.Continue;

                        working = working.AddMilliseconds(ms - working.Millisecond);

                        return MenuItemResult.Continue;
                    }),
                new(
                    "Ticks",
                    () => working.Ticks.ToString(),
                    () =>
                    {
                        if (!this.PromptLong("Ticks", working.Ticks, DateTime.MinValue.Ticks, DateTime.MaxValue.Ticks, out var ticks))
                            return MenuItemResult.Continue;

                        working = new(ticks, working.Kind);

                        return MenuItemResult.Continue;
                    })
            },
            true,
            false,
            true,
            () => this.Confirm("Cancel changes?"));

        if (outcome == MenuExitReason.Save)
            value = working;

        return outcome;
    }


    private MenuExitReason EditDateOnlyInteractive(ref DateOnly value)
    {
        var working = value;
        var outcome = this.RunMenu(
            "Edit Date",
            new List<MenuItem>
            {
                new(
                    "Year",
                    () => working.Year.ToString(),
                    () =>
                    {
                        if (!this.PromptInt("Year", working.Year, 1, 9999, out var year))
                            return MenuItemResult.Continue;

                        var day = Math.Min(working.Day, DateTime.DaysInMonth(year, working.Month));
                        working = new(year, working.Month, day);

                        return MenuItemResult.Continue;
                    }),
                new(
                    "Month",
                    () => working.Month.ToString(),
                    () =>
                    {
                        if (!this.PromptInt("Month", working.Month, 1, 12, out var month))
                            return MenuItemResult.Continue;

                        var day = Math.Min(working.Day, DateTime.DaysInMonth(working.Year, month));
                        working = new(working.Year, month, day);

                        return MenuItemResult.Continue;
                    }),
                new(
                    "Day",
                    () => working.Day.ToString(),
                    () =>
                    {
                        var maxDay = DateTime.DaysInMonth(working.Year, working.Month);

                        if (!this.PromptInt("Day", working.Day, 1, maxDay, out var dayValue))
                            return MenuItemResult.Continue;

                        working = new(working.Year, working.Month, dayValue);

                        return MenuItemResult.Continue;
                    })
            },
            true,
            false,
            true,
            () => this.Confirm("Cancel changes?"));

        if (outcome == MenuExitReason.Save)
            value = working;

        return outcome;
    }

    private bool PromptInt(string label, int current, int min, int max, out int result)
    {
        while (true)
        {
            Console.Write($"{label} ({min}-{max}) [{current}]: ");
            var initial = current.ToString(CultureInfo.InvariantCulture);
            var input = TerminalLineEditor.ReadLine(initial, true, out var cancelled);
            if (cancelled)
            {
                result = current;

                return false;
            }

            if (string.IsNullOrWhiteSpace(input))
            {
                result = current;

                return true;
            }

            if (int.TryParse(input, out var parsed) && parsed >= min && parsed <= max)
            {
                result = parsed;

                return true;
            }

            Console.WriteLine("Invalid value, try again.");
        }
    }

    private bool PromptLong(string label, long current, long min, long max, out long result)
    {
        while (true)
        {
            Console.Write($"{label} ({min}-{max}) [{current}]: ");
            var initial = current.ToString(CultureInfo.InvariantCulture);
            var input = TerminalLineEditor.ReadLine(initial, true, out var cancelled);
            if (cancelled)
            {
                result = current;

                return false;
            }

            if (string.IsNullOrWhiteSpace(input))
            {
                result = current;

                return true;
            }

            if (long.TryParse(input, out var parsed) && parsed >= min && parsed <= max)
            {
                result = parsed;

                return true;
            }

            Console.WriteLine("Invalid value, try again.");
        }
    }

    private void ShowError(Exception exception)
    {
        if (exception == null)
            return;

        this._statusMessage = exception.GetBaseException().Message;
        RunExternal(() =>
        {
            Console.Clear();
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("An error occurred:");
            Console.ResetColor();
            Console.WriteLine(exception);
            Console.WriteLine();
            Console.WriteLine("Press Enter or Esc to continue...");
            while (true)
            {
                var key = Console.ReadKey(intercept: true).Key;

                if (key == ConsoleKey.Enter || key == ConsoleKey.Escape)
                    break;
            }
        });
    }

    private static T RunExternal<T>(Func<T> operation)
    {
        var dispatcher = TerminalDispatcher.Current;

        return dispatcher != null ? dispatcher.RunExternal(operation) : operation();
    }

    private static void RunExternal(Action action)
    {
        RunExternal(() =>
        {
            action();

            return true;
        });
    }

    private static bool IsShiftHeld(ConsoleModifiers modifiers) => (modifiers & ConsoleModifiers.Shift) == ConsoleModifiers.Shift;

    private sealed record PropertyValueResolution(object? Value, Type ValueType);

    private sealed record MenuItem(string Label, Func<string> ValueFactory, Func<MenuItemResult> Execute);

    private enum MenuItemResult
    {
        Continue,
        Save,
        Cancel
    }

    private readonly record struct ListContext(TerminalExplorerFrame Frame, IList List, Type ElementType, bool AllowsNullElements);

    private enum MenuExitReason
    {
        Save,
        Cancel,
        Delete
    }

    private enum EditInteractionResult
    {
        Save,
        Cancel,
        Delete
    }
}