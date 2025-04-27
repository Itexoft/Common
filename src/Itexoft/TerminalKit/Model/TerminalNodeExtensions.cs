// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Text.Json;

namespace Itexoft.TerminalKit;

/// <summary>
/// Convenience helpers for extracting typed values and bindings from <see cref="TerminalNode" /> instances.
/// </summary>
internal static class TerminalNodeExtensions
{
    public static bool TryGetProperty<T>(this TerminalNode node, string propertyName, out T? value)
    {
        ArgumentNullException.ThrowIfNull(node);
        value = default;

        if (!node.Properties.TryGetValue(propertyName, out var raw) || raw == null)
            return false;

        if (raw is T typed)
        {
            value = typed;

            return true;
        }

        if (raw is JsonElement element)
        {
            value = element.Deserialize<T>(TerminalJsonOptions.Default);

            return true;
        }

        return false;
    }

    public static bool TryGetBindingName(this TerminalNode node, string propertyName, out string stateName)
    {
        ArgumentNullException.ThrowIfNull(node);
        stateName = string.Empty;

        if (!node.TryGetProperty(propertyName, out string? expression) || string.IsNullOrWhiteSpace(expression))
            return false;

        return TerminalRuntime.TryGetStateName(expression, out stateName);
    }
}