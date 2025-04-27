// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.TerminalKit;

/// <summary>
/// Provides a friendly label and optional description for properties, methods or types rendered in the console UI.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Method | AttributeTargets.Class)]
public sealed class TerminalDisplayAttribute : Attribute
{
    /// <summary>
    /// Creates the attribute with the specified label.
    /// </summary>
    /// <param name="label">Optional caption shown to the user.</param>
    public TerminalDisplayAttribute(string? label = null) => this.Label = label;

    /// <summary>
    /// Gets the friendly caption that replaces the member name.
    /// </summary>
    public string? Label { get; }

    /// <summary>
    /// Gets or sets an optional free-form description displayed beneath the caption.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether the last loaded value should be shown while a fresh value is loading.
    /// </summary>
    public bool ShowStaleWhileRefreshing { get; init; }
}
