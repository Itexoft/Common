// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.TerminalKit;

/// <summary>
/// Strongly typed reference to a component instance inside the UI tree.
/// </summary>
public readonly record struct TerminalComponentHandle<TComponent>
    where TComponent : TerminalComponentDefinition
{
    /// <summary>
    /// Initializes the handle with the specified component identifier.
    /// </summary>
    /// <param name="id">The identifier assigned by the builder.</param>
    public TerminalComponentHandle(string id) => this.Id = id ?? throw new ArgumentNullException(nameof(id));

    /// <summary>
    /// Gets the identifier shared with the underlying console node.
    /// </summary>
    public string Id { get; }

    /// <inheritdoc />
    public override string ToString() => this.Id;
}