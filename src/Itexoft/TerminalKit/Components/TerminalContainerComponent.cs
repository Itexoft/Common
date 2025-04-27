// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.TerminalKit;

/// <summary>
/// Component that can host child regions.
/// </summary>
public abstract class TerminalContainerComponent : TerminalComponentDefinition
{
    /// <summary>
    /// Gets the names of child slots rendered by the component.
    /// </summary>
    public string[] Slots { get; init; } = [];
}