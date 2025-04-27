// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Collections.Concurrent;
using System.Reflection;
using Itexoft.Extensions;

namespace Itexoft.TerminalKit;

/// <summary>
/// Resolves declarative names for console UI components via their attributes.
/// </summary>
public static class TerminalComponentRegistry
{
    private static readonly ConcurrentDictionary<Type, string> nameCache = new();

    /// <summary>
    /// Resolves the registered name for the specified component type.
    /// </summary>
    /// <param name="componentType">Component type decorated with <see cref="TerminalComponentAttribute" />.</param>
    /// <returns>The declarative name understood by the renderer.</returns>
    public static string GetComponentName(Type componentType)
    {
        componentType.Required();

        return nameCache.GetOrAdd(componentType, ResolveName);
    }

    private static string ResolveName(Type type)
    {
        var attribute = type.GetCustomAttribute<TerminalComponentAttribute>();

        if (attribute == null)
            throw new InvalidOperationException($"Component type '{type.FullName}' must be decorated with {nameof(TerminalComponentAttribute)}.");

        return attribute.Name;
    }
}
