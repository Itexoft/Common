// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.TerminalKit;

/// <summary>
/// Allows a property or method to be explicitly hidden or shown in the explorer, regardless of its CLR visibility.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Method)]
public sealed class TerminalVisibilityAttribute : Attribute
{
    /// <summary>
    /// Initializes the attribute with the desired visibility flag.
    /// </summary>
    public TerminalVisibilityAttribute(bool isVisible = true) => this.IsVisible = isVisible;

    /// <summary>
    /// Gets a value indicating whether the member should be rendered.
    /// </summary>
    public bool IsVisible { get; }
}