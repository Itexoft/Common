// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Itexoft.TerminalKit.ObjectExplorer;

internal enum TerminalExplorerFrameKind
{
    Object,
    CollectionRoot,
    CollectionItems,
}

internal sealed class TerminalExplorerFrame
{
    private readonly List<TerminalPendingEntryLoad> pendingLoads = [];
    private readonly Dictionary<PropertyInfo, TerminalPropertyValueState> propertyStates = new();

    private TerminalExplorerFrame(object? target, string title, TerminalExplorerFrameKind kind, TerminalExplorerEntry? sourceEntry = null)
    {
        this.Target = target;
        this.Title = title;
        this.Kind = kind;
        this.SourceEntry = sourceEntry;
        this.RefreshEntries();
    }

    public object? Target { get; }

    public string Title { get; }

    public TerminalExplorerFrameKind Kind { get; }

    public List<TerminalExplorerEntry> Entries { get; } = [];

    public HashSet<int> SelectedIndices { get; } = [];

    public TerminalExplorerEntry? SourceEntry { get; }

    public static TerminalExplorerFrame Create(object? target, string title)
    {
        if (target is IEnumerable and not string)
            return new(target, title, TerminalExplorerFrameKind.CollectionRoot);

        return new(target, title, TerminalExplorerFrameKind.Object);
    }

    public static TerminalExplorerFrame CreateItemsView(IEnumerable target, string title, TerminalExplorerEntry? sourceEntry = null) =>
        new(target, title, TerminalExplorerFrameKind.CollectionItems, sourceEntry);

    public void RefreshEntries()
    {
        this.Entries.Clear();
        this.pendingLoads.Clear();

        if (this.Target == null)
            return;

        switch (this.Kind)
        {
            case TerminalExplorerFrameKind.CollectionRoot:
                this.BuildCollectionRootEntries();

                break;
            case TerminalExplorerFrameKind.CollectionItems:
                this.BuildCollectionItemEntries();

                break;
            default:
                this.BuildObjectEntries();

                break;
        }

        this.PruneSelection(this.Entries.Count);
    }

    public void ClearSelection() => this.SelectedIndices.Clear();

    public void SetSelection(int index, bool isSelected)
    {
        if (index < 0)
            return;

        if (isSelected)
            this.SelectedIndices.Add(index);
        else
            this.SelectedIndices.Remove(index);
    }

    public void RemoveSelectionsAndShift(IEnumerable<int> removedIndices)
    {
        if (removedIndices == null)
            return;

        var ordered = removedIndices.Where(i => i >= 0).Distinct().OrderBy(i => i).ToList();

        if (ordered.Count == 0)
            return;

        foreach (var removed in ordered)
        {
            this.SelectedIndices.Remove(removed);
            var toShift = this.SelectedIndices.Where(i => i > removed).ToList();

            foreach (var index in toShift)
            {
                this.SelectedIndices.Remove(index);
                this.SelectedIndices.Add(index - 1);
            }
        }
    }

    public void PruneSelection(int maxCount)
    {
        if (this.SelectedIndices.Count == 0)
            return;

        var toRemove = new List<int>();

        foreach (var index in this.SelectedIndices)
        {
            if (index < 0 || index >= maxCount)
                toRemove.Add(index);
        }

        foreach (var index in toRemove)
            this.SelectedIndices.Remove(index);
    }

    public bool TryGetAutoItems(out IEnumerable items)
    {
        if (this.Kind == TerminalExplorerFrameKind.CollectionRoot
            && this.Entries.Count == 1
            && this.Entries[0].Kind == TerminalExplorerEntryKind.CollectionItemsNode
            && this.Entries[0].Payload is IEnumerable enumerable)
        {
            items = enumerable;

            return true;
        }

        items = Array.Empty<object>();

        return false;
    }

    private void BuildCollectionRootEntries()
    {
        if (this.Target is not IEnumerable enumerable)
            return;

        var count = TerminalExplorerValueFormatter.GetCount(enumerable);

        this.Entries.Add(
            new(
                "Items",
                "Collection",
                $"Items ({count})",
                enumerable.GetType().Name,
                TerminalExplorerEntryKind.CollectionItemsNode,
                TerminalEntryLoadStatus.Loaded,
                false,
                null,
                null,
                enumerable,
                null));
    }

    private void BuildCollectionItemEntries()
    {
        if (this.Target is IDictionary dictionary)
        {
            var dictIndex = 0;

            foreach (var entry in EnumerateDictionaryEntries(dictionary))
            {
                var preview = TerminalExplorerVisuals.LoadingIndicator;
                var clrName = string.Empty;

                this.Entries.Add(
                    new(
                        $"[{entry.Key}]",
                        "Dictionary Entry",
                        preview,
                        clrName,
                        TerminalExplorerEntryKind.CollectionItem,
                        TerminalEntryLoadStatus.Loading,
                        false,
                        null,
                        null,
                        entry.Value,
                        dictIndex,
                        DictionaryKey: entry.Key,
                        IsMarked: this.SelectedIndices.Contains(dictIndex)));

                this.pendingLoads.Add(new(null, entry.Value, 0, this.Entries.Count - 1));
                dictIndex++;
            }

            return;
        }

        if (this.Target is not IEnumerable enumerable)
            return;

        var index = 0;

        foreach (var item in enumerable)
        {
            var preview = TerminalExplorerVisuals.LoadingIndicator;
            var clr = string.Empty;

            this.Entries.Add(
                new(
                    $"[{index}]",
                    "Item",
                    preview,
                    clr,
                    TerminalExplorerEntryKind.CollectionItem,
                    TerminalEntryLoadStatus.Loading,
                    false,
                    null,
                    null,
                    item,
                    index,
                    DictionaryKey: null,
                    IsMarked: this.SelectedIndices.Contains(index)));

            this.pendingLoads.Add(new(null, item, 0, this.Entries.Count - 1));
            index++;
        }
    }

    private void BuildObjectEntries()
    {
        var type = this.Target!.GetType();
        var properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        foreach (var property in properties)
        {
            if (property.GetIndexParameters().Length > 0)
                continue;

            var isPublic = (property.GetMethod?.IsPublic ?? false) || (property.SetMethod?.IsPublic ?? false);

            if (!ShouldIncludeMember(property, isPublic))
                continue;

            var state = this.GetOrCreatePropertyState(property, this.ResolveDisplay(property)?.ShowStaleWhileRefreshing ?? false);
            var version = state.BeginLoad();

            var preview = state.AllowStale && !string.IsNullOrWhiteSpace(state.Preview)
                ? TerminalExplorerVisuals.FormatRefreshing(state.Preview)
                : TerminalExplorerVisuals.LoadingIndicator;

            var typeName = state.AllowStale && !string.IsNullOrWhiteSpace(state.TypeName) ? state.TypeName! : string.Empty;
            var display = ResolveLabel(property);
            var isReadOnly = property.IsDefined(typeof(TerminalReadOnlyAttribute), true) || !property.CanWrite;
            var kindLabel = isReadOnly ? "Property (read-only)" : "Property";

            this.Entries.Add(
                new(
                    display,
                    kindLabel,
                    preview,
                    typeName,
                    TerminalExplorerEntryKind.Property,
                    TerminalEntryLoadStatus.Loading,
                    !isReadOnly,
                    property,
                    null,
                    null,
                    null));

            var entryIndex = this.Entries.Count - 1;
            this.pendingLoads.Add(new(property, null, version, entryIndex));
        }

        var methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        foreach (var method in methods)
        {
            if (method.IsSpecialName)
                continue;

            if (method.GetParameters().Length > 0)
                continue;

            if (string.Equals(method.Name, nameof(this.ToString), StringComparison.Ordinal))
                continue;

            if (method.DeclaringType == typeof(object))
                continue;

            var isPublic = method.IsPublic;

            if (!ShouldIncludeMember(method, isPublic))
                continue;

            var display = ResolveLabel(method);
            var kindLabel = method.ReturnType == typeof(void) ? "Action" : method.ReturnType.Name;

            this.Entries.Add(
                new(
                    display,
                    kindLabel,
                    string.Empty,
                    string.Empty,
                    TerminalExplorerEntryKind.Method,
                    TerminalEntryLoadStatus.Loaded,
                    false,
                    null,
                    method,
                    null,
                    null));
        }
    }

    private static IEnumerable<DictionaryEntry> EnumerateDictionaryEntries(IDictionary dictionary)
    {
        var snapshot = new List<DictionaryEntry>();

        foreach (DictionaryEntry entry in dictionary)
            snapshot.Add(entry);

        return snapshot.OrderBy(entry => FormatDictionaryKey(entry.Key), StringComparer.OrdinalIgnoreCase);
    }

    private static string FormatDictionaryKey(object? key) => key?.ToString() ?? string.Empty;

    internal static string FormatTypeName(Type type)
    {
        if (type.IsArray)
            return FormatTypeName(type.GetElementType()!) + "[]";

        if (type.IsGenericType)
        {
            var name = type.Name;
            var tickIndex = name.IndexOf('`');

            if (tickIndex >= 0)
                name = name[..tickIndex];

            var arguments = string.Join(", ", type.GetGenericArguments().Select(FormatTypeName));

            return $"{name}<{arguments}>";
        }

        return type.FullName ?? type.Name;
    }

    private static string ResolveLabel(MemberInfo member)
    {
        var display = member.GetCustomAttribute<TerminalDisplayAttribute>();

        return string.IsNullOrWhiteSpace(display?.Label) ? member.Name : display!.Label!;
    }

    private static object? SafeGet(Func<object?> getter)
    {
        try
        {
            return getter();
        }
        catch
        {
            return "[error]";
        }
    }

    private static bool ShouldIncludeMember(MemberInfo member, bool isPublic)
    {
        var visibility = member.GetCustomAttribute<TerminalVisibilityAttribute>();

        return visibility?.IsVisible ?? isPublic;
    }

    private TerminalDisplayAttribute? ResolveDisplay(MemberInfo member) => member.GetCustomAttribute<TerminalDisplayAttribute>();

    internal bool TryGetPropertyState(PropertyInfo property, [NotNullWhen(true)] out TerminalPropertyValueState? state) =>
        this.propertyStates.TryGetValue(property, out state);

    internal IEnumerable<TerminalPendingEntryLoad> ConsumePendingLoads(Func<TerminalPendingEntryLoad, bool>? predicate = null)
    {
        if (this.pendingLoads.Count == 0)
            return [];

        var snapshot = predicate == null ? this.pendingLoads.ToArray() : this.pendingLoads.Where(predicate).ToArray();

        if (snapshot.Length == 0)
            return [];

        foreach (var item in snapshot)
            this.pendingLoads.Remove(item);

        return snapshot;
    }

    internal void UpdatePropertyEntry(PropertyInfo property, TerminalPropertyValueState state)
    {
        var index = this.Entries.FindIndex(e => e.Property == property);

        if (index < 0)
            return;

        var entry = this.Entries[index];

        var preview = state.Status switch
        {
            TerminalEntryLoadStatus.Loaded => state.Preview ?? string.Empty,
            TerminalEntryLoadStatus.Failed => $"Error: {state.Error?.GetBaseException().Message}",
            _ => TerminalExplorerVisuals.LoadingIndicator,
        };

        var typeName = state.Status == TerminalEntryLoadStatus.Loaded ? state.TypeName ?? string.Empty : string.Empty;

        this.Entries[index] = entry with
        {
            ValuePreview = preview,
            ClrTypeName = typeName,
            LoadStatus = state.Status,
        };
    }

    internal void UpdateItemEntry(int entryIndex, string preview, string typeName, TerminalEntryLoadStatus status)
    {
        if (entryIndex < 0 || entryIndex >= this.Entries.Count)
            return;

        var entry = this.Entries[entryIndex];

        if (entry.Kind != TerminalExplorerEntryKind.CollectionItem)
            return;

        this.Entries[entryIndex] = entry with
        {
            ValuePreview = preview,
            ClrTypeName = typeName,
            LoadStatus = status,
        };
    }

    private TerminalPropertyValueState GetOrCreatePropertyState(PropertyInfo property, bool allowStale)
    {
        if (!this.propertyStates.TryGetValue(property, out var state))
        {
            state = new();
            this.propertyStates[property] = state;
        }

        state.AllowStale = allowStale;

        return state;
    }
}

internal sealed record TerminalPendingEntryLoad(PropertyInfo? Property, object? Item, int Version, int EntryIndex);

internal sealed class TerminalPropertyValueState
{
    public bool AllowStale { get; set; }

    public TerminalEntryLoadStatus Status { get; private set; } = TerminalEntryLoadStatus.Loaded;

    public string? Preview { get; private set; }

    public string? TypeName { get; private set; }

    public object? Value { get; private set; }

    public Exception? Error { get; private set; }

    public int Version { get; private set; }

    public int BeginLoad()
    {
        this.Status = TerminalEntryLoadStatus.Loading;
        this.Version++;

        return this.Version;
    }

    public void Complete(int version, string preview, string typeName, object? value)
    {
        if (version != this.Version)
            return;

        this.Status = TerminalEntryLoadStatus.Loaded;
        this.Preview = preview;
        this.TypeName = typeName;
        this.Value = value;
        this.Error = null;
    }

    public void Fail(int version, Exception exception)
    {
        if (version != this.Version)
            return;

        this.Status = TerminalEntryLoadStatus.Failed;
        this.Error = exception;
    }
}
