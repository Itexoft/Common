// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Reflection;

namespace Itexoft.TerminalKit.ObjectExplorer;

internal enum TerminalExplorerEntryKind
{
    Property,
    Method,
    CollectionItemsNode,
    CollectionItem
}

internal enum TerminalEntryLoadStatus
{
    Loading,
    Loaded,
    Failed
}

internal static class TerminalExplorerVisuals
{
    public const string LoadingIndicator = "⌛";
    public const string RefreshingIndicator = "⟳";

    public static string FormatRefreshing(string? value) =>
        string.IsNullOrWhiteSpace(value) ? LoadingIndicator : $"{value} {RefreshingIndicator}";
}

internal sealed record TerminalExplorerEntry(
    string Display,
    string KindLabel,
    string ValuePreview,
    string ClrTypeName,
    TerminalExplorerEntryKind Kind,
    TerminalEntryLoadStatus LoadStatus,
    bool IsEditable,
    PropertyInfo? Property,
    MethodInfo? Method,
    object? Payload,
    int? CollectionIndex,
    bool IsMarked = false,
    bool IsExecuting = false,
    object? DictionaryKey = null);