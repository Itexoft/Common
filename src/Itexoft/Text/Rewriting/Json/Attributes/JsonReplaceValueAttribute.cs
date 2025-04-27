// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Text.Rewriting.Json.Attributes;

/// <summary>
/// Declares a rule that replaces a value at the specified JSON pointer.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class JsonReplaceValueAttribute(string pointer, string replacement) : Attribute
{
    /// <summary>
    /// JSON pointer identifying the value to replace.
    /// </summary>
    public string Pointer { get; } = pointer;

    /// <summary>
    /// Replacement string emitted for the targeted value.
    /// </summary>
    public string Replacement { get; } = replacement;

    /// <summary>
    /// Optional rule name for diagnostics and gating.
    /// </summary>
    public string? Name { get; init; }
}
