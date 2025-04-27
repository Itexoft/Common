// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.TerminalKit;

/// <summary>
/// Typed reference to a state slice registered with the UI builder.
/// </summary>
public readonly record struct StateHandle<TState>
{
    /// <summary>
    /// Initializes the handle with the provided slice name.
    /// </summary>
    /// <param name="name">The logical name supplied to <c>WithState</c>.</param>
    public StateHandle(string name) => this.Name = name ?? throw new ArgumentNullException(nameof(name));

    /// <summary>
    /// Gets the logical name used by binding expressions.
    /// </summary>
    public string Name { get; }
}