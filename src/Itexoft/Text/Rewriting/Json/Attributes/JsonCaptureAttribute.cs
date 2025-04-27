// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Text.Rewriting.Json.Attributes;

/// <summary>
/// Declares a rule that captures a JSON value addressed by pointer.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class JsonCaptureAttribute(string pointer) : Attribute
{
    /// <summary>
    /// JSON pointer identifying the value to capture.
    /// </summary>
    public string Pointer { get; } = pointer;

    /// <summary>
    /// Optional rule name for diagnostics and gating.
    /// </summary>
    public string? Name { get; init; }
}
