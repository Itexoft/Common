// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.TerminalKit;

/// <summary>
/// Declares metadata for a strongly typed console UI component.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class TerminalComponentAttribute : Attribute
{
    /// <summary>
    /// Initializes the attribute with a user-friendly component identifier.
    /// </summary>
    /// <param name="name">The unique name used to register the component.</param>
    public TerminalComponentAttribute(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Component name cannot be empty.", nameof(name));

        this.Name = name;
    }

    /// <summary>
    /// Gets the unique identifier assigned to the component type.
    /// </summary>
    public string Name { get; }
}
