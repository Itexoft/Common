// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Text.Rewriting.Primitives;

/// <summary>
/// Per-rule metric snapshot.
/// </summary>
public readonly record struct RuleStat(int RuleId, string? Name, string? Group, long Hits, TimeSpan Elapsed);
