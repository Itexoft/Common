// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.TerminalKit.Dsl;

/// <summary>
/// Strongly typed alias used to reference shared state slices inside console scenes.
/// </summary>
public readonly record struct TerminalStateKey
{
    /// <summary>
    /// Initializes the key with the provided name.
    /// </summary>
    /// <param name="name">Logical identifier that links states with components.</param>
    public TerminalStateKey(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("State key cannot be empty.", nameof(name));

        this.Name = name;
    }

    /// <summary>
    /// Gets the logical state identifier.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Returns the key name.
    /// </summary>
    public override string ToString() => this.Name;

    /// <summary>
    /// Creates a key from the provided string literal.
    /// </summary>
    public static TerminalStateKey From(string name) => new(name);
}
