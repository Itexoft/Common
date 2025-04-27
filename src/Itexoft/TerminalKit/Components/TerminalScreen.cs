// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.TerminalKit;

/// <summary>
/// Top-level surface that hosts header/body/footer slots.
/// </summary>
[TerminalComponent(nameof(TerminalScreen))]
public sealed class TerminalScreen : TerminalContainerComponent
{
    public TerminalScreen() => this.Slots = [nameof(this.HeaderSlot), nameof(this.BodySlot), nameof(this.FooterSlot)];

    /// <summary>
    /// Gets the window title shown by renderers that support it.
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// Gets the optional theme identifier (light, dark, etc).
    /// </summary>
    public string? Theme { get; init; }

    /// <summary>
    /// Gets the name of the header slot.
    /// </summary>
    public string HeaderSlot => "header";

    /// <summary>
    /// Gets the name of the primary content slot.
    /// </summary>
    public string BodySlot => "body";

    /// <summary>
    /// Gets the name of the footer slot.
    /// </summary>
    public string FooterSlot => "footer";
}