// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Text.Rewriting.Json.Attributes;

/// <summary>
/// Declares a rule that renames a JSON property addressed by pointer.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class JsonRenamePropertyAttribute(string pointer, string newName) : Attribute
{
    /// <summary>
    /// JSON pointer pointing at the property to rename.
    /// </summary>
    public string Pointer { get; } = pointer;

    /// <summary>
    /// New property name to emit.
    /// </summary>
    public string NewName { get; } = newName;

    /// <summary>
    /// Optional rule name for diagnostics and gating.
    /// </summary>
    public string? Name { get; init; }
}
