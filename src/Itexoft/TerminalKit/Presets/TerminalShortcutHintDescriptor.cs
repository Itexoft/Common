// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.TerminalKit.Presets;

/// <summary>
/// Represents a shortcut hint rendered in the action bar.
/// </summary>
public sealed class TerminalShortcutHintDescriptor
{
    /// <summary>
    /// Gets the formatted shortcut text (e.g., <c>[A] Add</c>).
    /// </summary>
    public string Text { get; init; } = string.Empty;
}
