// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.TerminalKit;

/// <summary>
/// Generic container for grouping child components.
/// </summary>
[TerminalComponent(nameof(TerminalPanel))]
public sealed class TerminalPanel : TerminalContainerComponent
{
    public TerminalPanel() => this.Slots = [nameof(this.ContentSlot)];

    /// <summary>
    /// Gets the optional layout identifier understood by the renderer.
    /// </summary>
    public string? Layout { get; init; }

    /// <summary>
    /// Gets the single child slot name.
    /// </summary>
    public string ContentSlot => "content";
}
