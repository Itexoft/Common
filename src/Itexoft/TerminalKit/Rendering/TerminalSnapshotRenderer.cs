// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Collections;
using System.Reflection;
using System.Text.Json;
using Itexoft.TerminalKit.ObjectExplorer;
using Itexoft.TerminalKit.Presets;

namespace Itexoft.TerminalKit.Rendering;

/// <summary>
/// Very lightweight console renderer that visualizes a snapshot without converting it to JSON first.
/// </summary>
internal sealed class TerminalSnapshotRenderer
{
    private static readonly string ScreenComponentName = TerminalComponentRegistry.GetComponentName(typeof(TerminalScreen));
    private static readonly string PanelComponentName = TerminalComponentRegistry.GetComponentName(typeof(TerminalPanel));
    private static readonly string ListComponentName = TerminalComponentRegistry.GetComponentName(typeof(TerminalListView));
    private static readonly string TableComponentName = TerminalComponentRegistry.GetComponentName(typeof(TerminalTableView));
    private static readonly string FormComponentName = TerminalComponentRegistry.GetComponentName(typeof(TerminalMetadataForm));
    private static readonly string LabelComponentName = TerminalComponentRegistry.GetComponentName(typeof(TerminalLabel));
    private static readonly string ShortcutComponentName = TerminalComponentRegistry.GetComponentName(typeof(TerminalShortcutHint));
    private static readonly string BreadcrumbComponentName = TerminalComponentRegistry.GetComponentName(typeof(TerminalBreadcrumb));

    private static readonly TerminalCellStyle SelectedRowStyle = new()
    {
        Background = ConsoleColor.DarkCyan,
        Foreground = ConsoleColor.Black
    };

    private static readonly TerminalCellStyle HeaderStyle = new()
    {
        Background = ConsoleColor.DarkGray,
        Foreground = ConsoleColor.White
    };

    private static readonly TerminalCellStyle HighlightStyle = new()
    {
        Background = ConsoleColor.DarkBlue,
        Foreground = ConsoleColor.White
    };

    private static readonly TerminalCellStyle MarkedRowStyle = new()
    {
        Background = ConsoleColor.DarkGray,
        Foreground = ConsoleColor.Yellow
    };

    private static readonly TerminalCellStyle MarkedSelectedRowStyle = new()
    {
        Background = ConsoleColor.DarkGreen,
        Foreground = ConsoleColor.White
    };

    private static readonly TerminalCellStyle ExecutingRowStyle = new()
    {
        Background = ConsoleColor.DarkYellow,
        Foreground = ConsoleColor.Black
    };

    private static readonly TerminalCellStyle ExecutingSelectedRowStyle = new()
    {
        Background = ConsoleColor.Yellow,
        Foreground = ConsoleColor.Black
    };

    private readonly TerminalRenderBuffer _buffer = new();
    private readonly TerminalRuntime _runtime;

    public TerminalSnapshotRenderer(TerminalSnapshot snapshot) => this._runtime = new(snapshot);

    public void Render()
    {
        this._buffer.Reset();
        this.RenderNode(this._runtime.Snapshot.Root, 0);
        this._buffer.Flush();
    }

    private void RenderNode(TerminalNode node, int indent)
    {
        if (node.Component == ScreenComponentName)
        {
            this.RenderScreen(node);

            return;
        }

        if (node.Component == PanelComponentName)
        {
            this.RenderPanel(node, indent);

            return;
        }

        if (node.Component == ListComponentName)
        {
            this.RenderList(node);

            return;
        }

        if (node.Component == TableComponentName)
        {
            this.RenderTable(node);

            return;
        }

        if (node.Component == FormComponentName)
        {
            this.RenderForm(node);

            return;
        }

        if (node.Component == BreadcrumbComponentName)
        {
            this.RenderBreadcrumb(node);

            return;
        }

        if (node.Component == LabelComponentName)
        {
            if (node.TryGetProperty(nameof(TerminalLabel.Text), out string? text))
                this._buffer.WriteLine(text ?? string.Empty);

            return;
        }

        if (node.Component == ShortcutComponentName)
        {
            if (node.TryGetProperty(nameof(TerminalShortcutHint.Text), out string? hint))
                this._buffer.WriteLine(hint ?? string.Empty);

            return;
        }

        foreach (var child in node.Children)
            this.RenderNode(child, indent);
    }

    private void RenderScreen(TerminalNode node)
    {
        foreach (var child in node.Children)
            this.RenderNode(child, 0);
    }

    private void RenderBreadcrumb(TerminalNode node)
    {
        if (!node.TryGetProperty(nameof(TerminalBreadcrumb.Path), out string? path))
            return;

        var text = string.IsNullOrWhiteSpace(path) ? string.Empty : path;
        this._buffer.WriteStyledLine(text, HighlightStyle, true);
        this._buffer.WriteLine();
    }

    private void RenderPanel(TerminalNode node, int indent)
    {
        foreach (var child in node.Children)
            this.RenderNode(child, indent + 2);
    }

    private void RenderList(TerminalNode node)
    {
        var items = this.ResolveItems(node, nameof(TerminalListView.DataSource));
        var viewport = this.ResolveViewport(node, nameof(TerminalListView.ViewportState));
        var selectionIndex = this.ResolveSelectionIndex(node, nameof(TerminalListView.SelectionState));

        this._buffer.WriteLine();

        if (items.Count == 0)
        {
            if (node.TryGetProperty(nameof(TerminalListView.EmptyStateText), out string? emptyText)
                && !string.IsNullOrWhiteSpace(emptyText))
                this._buffer.WriteLine($"  {emptyText}");
            else
                this._buffer.WriteLine("  (empty)");

            this.RenderNavigationSummary(node, 0, 0, 0, selectionIndex);

            return;
        }

        var offset = viewport?.Offset ?? 0;
        var window = viewport?.WindowSize ?? items.Count;
        var visible = items.Skip(offset).Take(window).ToList();

        for (var i = 0; i < visible.Count; i++)
        {
            var absoluteIndex = offset + i;
            var prefix = selectionIndex == absoluteIndex ? ">" : " ";
            this._buffer.WriteLine($" {prefix}[{absoluteIndex}] {FormatListItem(visible[i])}");
        }

        this.RenderNavigationSummary(node, items.Count, offset, visible.Count, selectionIndex);
    }

    private void RenderTable(TerminalNode node)
    {
        var items = this.ResolveItems(node, nameof(TerminalTableView.DataSource));
        var viewport = this.ResolveViewport(node, nameof(TerminalTableView.ViewportState));
        var columns = this.ResolveTableColumns(node);
        var tableStyle = ResolveStyle(node, nameof(TerminalTableView.TableStyle));
        var columnStyles = ResolveStyleDictionary(node, nameof(TerminalTableView.ColumnStyles));
        var rules = ResolveStyleRules(node, nameof(TerminalTableView.CellStyleRules));
        var selectionIndex = this.ResolveSelectionIndex(node, nameof(TerminalTableView.SelectionState));

        this._buffer.WriteLine();

        if (columns.Count == 0 || items.Count == 0)
        {
            this._buffer.WriteLine("  (no rows)");
            this.RenderNavigationSummary(node, items.Count, 0, 0, selectionIndex);

            return;
        }

        this.RenderTableHeader(columns);

        var offset = viewport?.Offset ?? 0;
        var window = viewport?.WindowSize ?? items.Count;
        var visible = items.Skip(offset).Take(window).ToList();

        for (var i = 0; i < visible.Count; i++)
        {
            var rowIndex = offset + i;
            var isSelected = selectionIndex == rowIndex;
            this.RenderTableRow(visible[i], columns, tableStyle, columnStyles, rules, isSelected);
        }

        this.RenderNavigationSummary(node, items.Count, offset, visible.Count, selectionIndex);
    }

    private void RenderTableHeader(IReadOnlyList<TerminalTableColumn> columns)
    {
        this._buffer.WriteStyled(" ", HeaderStyle);
        for (var columnIndex = 0; columnIndex < columns.Count; columnIndex++)
        {
            if (columnIndex > 0)
                this._buffer.WriteStyled("| ", HeaderStyle);

            var column = columns[columnIndex];
            var text = Pad(column.Header, column.Width);
            this._buffer.WriteStyled(text, HeaderStyle);
            this._buffer.WriteStyled(" ", HeaderStyle);
        }

        this._buffer.WriteLine();
    }

    private void RenderTableRow(
        object? rowItem,
        IReadOnlyList<TerminalTableColumn> columns,
        TerminalCellStyle? tableStyle,
        IReadOnlyDictionary<string, TerminalCellStyle> columnStyles,
        IReadOnlyList<TerminalCellStyleRule> rules,
        bool isSelected)
    {
        this._buffer.Write(" ");
        for (var columnIndex = 0; columnIndex < columns.Count; columnIndex++)
        {
            if (columnIndex > 0)
                this._buffer.Write("| ");

            var column = columns[columnIndex];
            var value = ResolveColumnValue(rowItem, column.Key.Path);
            var text = Pad(value, column.Width);
            var style = this.BuildCellStyle(tableStyle, column, columnStyles, rules, rowItem, value, isSelected);

            if (style != null)
                this.WriteStyled(text, style);
            else
                this._buffer.Write(text);

            this._buffer.Write(" ");
        }

        this._buffer.WriteLine();
    }

    private TerminalCellStyle? BuildCellStyle(
        TerminalCellStyle? tableStyle,
        TerminalTableColumn column,
        IReadOnlyDictionary<string, TerminalCellStyle> columnStyles,
        IReadOnlyList<TerminalCellStyleRule> rules,
        object? rowItem,
        string value,
        bool isSelected)
    {
        var style = tableStyle;
        style = MergeStyles(style, column.Style);
        style = MergeStyles(style, TryGetColumnStyle(columnStyles, column.Key.Path));
        style = MergeStyles(style, TryResolveRuleStyle(rules, column.Key.Path, value));

        if (rowItem is TerminalExplorerEntry entry)
        {
            if (entry.IsExecuting)
                style = MergeStyles(style, isSelected ? ExecutingSelectedRowStyle : ExecutingRowStyle);
            else if (entry.IsMarked && isSelected)
                style = MergeStyles(style, MarkedSelectedRowStyle);
            else if (entry.IsMarked)
                style = MergeStyles(style, MarkedRowStyle);
            else if (isSelected)
                style = MergeStyles(style, SelectedRowStyle);
        }
        else if (isSelected)
        {
            style = MergeStyles(style, SelectedRowStyle);
        }

        return style;
    }

    private void RenderForm(TerminalNode node)
    {
        this._buffer.WriteLine();
        this._buffer.WriteLine("Metadata Form");

        if (node.TryGetProperty(nameof(TerminalMetadataForm.Fields), out IReadOnlyList<TerminalFormFieldDefinition>? fields)
            && fields != null)
            foreach (var field in fields)
            {
                var editor = field.Editor.ToString();
                var label = string.IsNullOrWhiteSpace(field.Label) ? field.Key.Path : field.Label;
                this._buffer.WriteLine($"  - {label} [{editor}]");
            }

        var bound = ResolveBinding(node, nameof(TerminalMetadataForm.BoundItem));
        if (!string.IsNullOrEmpty(bound))
            this._buffer.WriteLine($"  Bound to state: {bound}");
    }

    private void RenderNavigationSummary(TerminalNode node, int total, int offset, int visibleCount, int? selectionIndex)
    {
        if (!ShouldRenderNavigationSummary(node))
            return;

        var safeOffset = Math.Max(0, offset);
        var safeVisible = Math.Max(visibleCount, total > 0 ? 1 : 0);

        var start = total == 0
            ? 0
            : Math.Clamp(safeOffset + 1, 1, total);
        var end = total == 0
            ? 0
            : Math.Clamp(safeOffset + safeVisible, start, total);

        var infoParts = new List<string>
        {
            $"[{start}-{end}] / {total}"
        };

        if (node.TryGetProperty(nameof(TerminalCollectionComponent.StatusMessage), out string? status)
            && !string.IsNullOrWhiteSpace(status))
            infoParts.Add(status);

        var hints = new List<string>();
        if (node.TryGetProperty(nameof(TerminalCollectionComponent.NavigationHint), out string? hint)
            && !string.IsNullOrWhiteSpace(hint))
            hints.AddRange(hint.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        var firstLineParts = new List<string>(infoParts);
        if (hints.Count > 0)
            firstLineParts.Add(hints[0]);

        var summary = " " + string.Join("  ·  ", firstLineParts);
        this._buffer.WriteStyledLine(summary, HighlightStyle, true);

        for (var i = 1; i < hints.Count; i++)
            this._buffer.WriteStyledLine(" " + hints[i], HighlightStyle, true);
    }

    private static bool ShouldRenderNavigationSummary(TerminalNode node)
    {
        if (node.TryGetProperty(nameof(TerminalCollectionComponent.ShowNavigationSummary), out bool enabled))
            return enabled;

        return true;
    }

    private List<object?> ResolveItems(TerminalNode node, string propertyName)
    {
        var result = new List<object?>();
        var bindingName = ResolveBinding(node, propertyName);

        if (string.IsNullOrWhiteSpace(bindingName))
            return result;

        var data = this._runtime.GetStateSlice(bindingName);
        if (data is JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Array)
                foreach (var entry in element.EnumerateArray())
                    result.Add(entry.ValueKind is JsonValueKind.Object or JsonValueKind.Array ? entry.ToString() : entry.ToString());

            return result;
        }

        if (data is IEnumerable enumerable)
            foreach (var entry in enumerable)
                result.Add(entry);

        return result;
    }

    private TerminalViewportState? ResolveViewport(TerminalNode node, string propertyName)
    {
        var stateName = ResolveBinding(node, propertyName);

        if (string.IsNullOrWhiteSpace(stateName))
            return null;

        var slice = this._runtime.GetStateSlice(stateName);

        return slice switch
        {
            TerminalViewportState state => state,
            JsonElement element when element.ValueKind == JsonValueKind.Object => element.Deserialize<TerminalViewportState>(
                TerminalJsonOptions.Default),
            _ => null
        };
    }

    private List<TerminalTableColumn> ResolveTableColumns(TerminalNode node)
    {
        if (node.TryGetProperty(nameof(TerminalTableView.Columns), out IReadOnlyList<TerminalTableColumn>? columns) && columns != null)
            return columns.ToList();

        return [];
    }

    private int? ResolveSelectionIndex(TerminalNode node, string propertyName)
    {
        var stateName = ResolveBinding(node, propertyName);

        if (string.IsNullOrWhiteSpace(stateName))
            return null;

        var slice = this._runtime.GetStateSlice(stateName);

        return slice switch
        {
            int index => index,
            JsonElement element when element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var value) => value,
            _ => null
        };
    }

    private static TerminalCellStyle? ResolveStyle(TerminalNode node, string propertyName)
    {
        if (!node.Properties.TryGetValue(propertyName, out var raw) || raw == null)
            return null;

        if (raw is TerminalCellStyle style)
            return style;

        if (raw is JsonElement element && element.ValueKind == JsonValueKind.Object)
            return element.Deserialize<TerminalCellStyle>(TerminalJsonOptions.Default);

        return null;
    }

    private static IReadOnlyDictionary<string, TerminalCellStyle> ResolveStyleDictionary(TerminalNode node, string propertyName)
    {
        if (!node.Properties.TryGetValue(propertyName, out var raw) || raw == null)
            return new Dictionary<string, TerminalCellStyle>(StringComparer.Ordinal);

        if (raw is IReadOnlyDictionary<string, TerminalCellStyle> dictionary)
            return dictionary;

        if (raw is Dictionary<string, TerminalCellStyle> dict)
            return dict;

        if (raw is JsonElement element && element.ValueKind == JsonValueKind.Object)
        {
            var map = new Dictionary<string, TerminalCellStyle>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                var style = JsonSerializer.Deserialize<TerminalCellStyle>(property.Value.GetRawText(), TerminalJsonOptions.Default);
                if (style != null)
                    map[property.Name] = style;
            }

            return map;
        }

        return new Dictionary<string, TerminalCellStyle>(StringComparer.Ordinal);
    }

    private static IReadOnlyList<TerminalCellStyleRule> ResolveStyleRules(TerminalNode node, string propertyName)
    {
        if (!node.Properties.TryGetValue(propertyName, out var raw) || raw == null)
            return [];

        if (raw is IReadOnlyList<TerminalCellStyleRule> rules)
            return rules;

        if (raw is TerminalCellStyleRule[] array)
            return array;

        if (raw is JsonElement element && element.ValueKind == JsonValueKind.Array)
        {
            var list = new List<TerminalCellStyleRule>();
            foreach (var entry in element.EnumerateArray())
            {
                var rule = entry.Deserialize<TerminalCellStyleRule>(TerminalJsonOptions.Default);
                if (rule != null)
                    list.Add(rule);
            }

            return list;
        }

        return [];
    }

    private static TerminalCellStyle? TryGetColumnStyle(IReadOnlyDictionary<string, TerminalCellStyle> styles, string key) =>
        styles.TryGetValue(key, out var style) ? style : null;

    private static TerminalCellStyle? TryResolveRuleStyle(IReadOnlyList<TerminalCellStyleRule> rules, string columnKey, string value)
    {
        foreach (var rule in rules)
        {
            if (!string.Equals(rule.Column.Path, columnKey, StringComparison.Ordinal))
                continue;

            if (rule.ValueStyles.TryGetValue(value, out var style))
                return style;
        }

        return null;
    }

    private static TerminalCellStyle? MergeStyles(TerminalCellStyle? baseStyle, TerminalCellStyle? overrideStyle)
    {
        if (baseStyle == null && overrideStyle == null)
            return null;

        return new()
        {
            Foreground = overrideStyle?.Foreground ?? baseStyle?.Foreground,
            Background = overrideStyle?.Background ?? baseStyle?.Background
        };
    }

    private void WriteStyled(string text, TerminalCellStyle? style) => this._buffer.WriteStyled(text, style);

    private static string FormatListItem(object? item)
    {
        if (item == null)
            return "(null)";

        if (item is string text)
            return text;

        if (item is JsonElement element)
            return element.ToString();

        var type = item.GetType();
        var name = TryGetStringProperty(type, item, "Name")
                   ?? TryGetStringProperty(type, item, "Login") ?? TryGetStringProperty(type, item, "Id");

        return string.IsNullOrWhiteSpace(name) ? item.ToString() ?? type.Name : name;
    }

    private static string? TryGetStringProperty(Type type, object instance, string propertyName)
    {
        var property = type.GetProperty(
            propertyName,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

        if (property?.GetValue(instance) is string value && !string.IsNullOrWhiteSpace(value))
            return value;

        return null;
    }

    private static string? ResolveBinding(TerminalNode node, string propertyName) =>
        node.TryGetBindingName(propertyName, out var stateName) ? stateName : null;

    private static string Pad(string? value, int width)
    {
        var text = value ?? string.Empty;

        if (width <= 0)
            return string.Empty;

        if (text.Length <= width)
            return text.PadRight(width);

        if (width <= 1)
            return "…";

        return text[..(width - 1)] + "…";
    }

    private static string ResolveColumnValue(object? item, string path)
    {
        if (item == null || string.IsNullOrWhiteSpace(path))
            return string.Empty;

        if (item is JsonElement jsonElement)
            return ResolveJsonColumn(jsonElement, path);

        var segments = path.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var current = item;
        foreach (var segment in segments)
        {
            if (current == null)
                return string.Empty;

            var property = current.GetType().GetProperty(
                segment,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

            if (property == null)
                return string.Empty;

            current = property.GetValue(current);
        }

        return current?.ToString() ?? string.Empty;
    }

    private static string ResolveJsonColumn(JsonElement element, string path)
    {
        var current = element;
        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out var next))
                return string.Empty;

            current = next;
        }

        return current.ToString();
    }
}