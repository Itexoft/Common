// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Text.Rewriting.Text.Attributes;

/// <summary>
/// Declares a tail matcher rule that operates on the buffered tail.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class TextTailRuleAttribute(int maxMatchLength) : Attribute
{
    /// <summary>
    /// Maximum length of the buffered tail inspected for a match.
    /// </summary>
    public int MaxMatchLength { get; } = maxMatchLength;

    /// <summary>
    /// Optional rule name for diagnostics and gating.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// Rule priority; lower values win.
    /// </summary>
    public int Priority { get; init; }
}
