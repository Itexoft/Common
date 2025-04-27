// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Text.RegularExpressions;
using Itexoft.Text.Rewriting.Primitives;

namespace Itexoft.Text.Rewriting.Text.Attributes;

/// <summary>
/// Declares a regex-based text rule to be compiled into a rewrite plan.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class TextRegexRuleAttribute(string pattern, int maxMatchLength) : Attribute
{
    /// <summary>
    /// Regular expression pattern that must end at the buffered tail.
    /// </summary>
    public string Pattern { get; } = pattern;

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

    /// <summary>
    /// Regex compilation options.
    /// </summary>
    public RegexOptions Options { get; init; } = RegexOptions.Compiled;

    /// <summary>
    /// Action applied to the match.
    /// </summary>
    public MatchAction Action { get; init; } = MatchAction.Hook;

    /// <summary>
    /// Fixed replacement used when <see cref="Action" /> is <see cref="MatchAction.Replace" />.
    /// </summary>
    public string? Replacement { get; init; }
}
