// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.TerminalKit;

/// <summary>
/// Declares that the decorated property should be displayed but never edited through the console explorer.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class TerminalReadOnlyAttribute : Attribute
{
    /// <summary>
    /// Initializes the attribute with the desired read-only flag.
    /// </summary>
    public TerminalReadOnlyAttribute(bool isReadOnly = true) => this.IsReadOnly = isReadOnly;

    /// <summary>
    /// Gets a value indicating whether the console UI must suppress editing.
    /// </summary>
    public bool IsReadOnly { get; }
}