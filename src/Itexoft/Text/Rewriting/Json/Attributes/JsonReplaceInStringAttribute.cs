// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Text.Rewriting.Json.Attributes;

/// <summary>
/// Declares a rule that rewrites string values either at a specific pointer or by predicate.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class JsonReplaceInStringAttribute : Attribute
{
    /// <summary>
    /// Optional JSON pointer; when omitted, predicate-based replacers are used.
    /// </summary>
    public string? Pointer { get; init; }

    /// <summary>
    /// Optional rule name for diagnostics and gating.
    /// </summary>
    public string? Name { get; init; }
}
