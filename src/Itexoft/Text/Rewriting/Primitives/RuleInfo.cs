// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Text.Rewriting.Primitives;

/// <summary>
/// Public metadata about a compiled rule.
/// </summary>
public readonly record struct RuleInfo(int Id, string? Name, string? Group);

/// <summary>
/// Public description of a compiled rule including shape and metadata.
/// </summary>
public readonly record struct RuleDescriptor(
    int Id,
    string? Name,
    string? Group,
    string Dialect,
    string Kind,
    int Priority,
    int Order,
    MatchAction Action,
    int MaxMatchLength,
    string? Target);
