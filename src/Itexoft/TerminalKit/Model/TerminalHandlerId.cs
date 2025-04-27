// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.TerminalKit;

/// <summary>
/// Strongly typed identifier for user handlers.
/// </summary>
public readonly record struct TerminalHandlerId
{
    /// <summary>
    /// Initializes the identifier with the supplied name.
    /// </summary>
    /// <param name="name">The symbolic handler name.</param>
    public TerminalHandlerId(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Handler id cannot be empty.", nameof(name));

        this.Name = name;
    }

    /// <summary>
    /// Gets the symbolic handler name recognized by the runtime.
    /// </summary>
    public string Name { get; }

    /// <inheritdoc />
    public override string ToString() => this.Name;

    /// <summary>
    /// Creates a handler id using the specified string literal.
    /// </summary>
    public static TerminalHandlerId From(string name) => new(name);
}
