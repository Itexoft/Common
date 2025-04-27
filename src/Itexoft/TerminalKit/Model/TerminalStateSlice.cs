// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Text.Json.Serialization;

namespace Itexoft.TerminalKit;

/// <summary>
/// Serializable fragment of UI state, e.g. current items or viewport metadata.
/// </summary>
public sealed class TerminalStateSlice
{
    /// <summary>
    /// Initializes a state slice with a logical name and value.
    /// </summary>
    /// <param name="name">Logical identifier shared with bindings.</param>
    /// <param name="value">The serialized state payload.</param>
    [JsonConstructor]
    public TerminalStateSlice(string name, object? value)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("State slice name cannot be empty.", nameof(name));

        this.Name = name;
        this.Value = value;
    }

    /// <summary>
    /// Gets the logical state name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the serialized value.
    /// </summary>
    public object? Value { get; }
}