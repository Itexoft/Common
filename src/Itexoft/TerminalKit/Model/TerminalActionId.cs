// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.TerminalKit;

/// <summary>
/// Strongly typed identifier for input actions.
/// </summary>
public readonly record struct TerminalActionId
{
    /// <summary>
    /// Initializes the identifier with the supplied symbolic name.
    /// </summary>
    /// <param name="name">The declarative gesture/action name.</param>
    public TerminalActionId(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Action id cannot be empty.", nameof(name));

        this.Name = name;
    }

    /// <summary>
    /// Gets the symbolic name associated with the action.
    /// </summary>
    public string Name { get; }

    /// <inheritdoc />
    public override string ToString() => this.Name;

    /// <summary>
    /// Creates an identifier directly from the provided literal.
    /// </summary>
    public static TerminalActionId From(string name) => new(name);
}
