// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.TerminalKit.Presets;

/// <summary>
/// Describes a reusable viewport binding that can be attached to any scrollable component.
/// </summary>
public sealed class TerminalScrollableOptions
{
    /// <summary>
    /// Gets or sets the state slice name that holds viewport data.
    /// </summary>
    public string StateName { get; set; } = "Viewport";

    /// <summary>
    /// Gets or sets the property name that stores the offset.
    /// </summary>
    public string OffsetProperty { get; set; } = "Offset";

    /// <summary>
    /// Gets or sets the property name that stores the window size.
    /// </summary>
    public string WindowSizeProperty { get; set; } = "WindowSize";

    /// <summary>
    /// Gets or sets the default window size applied when state is missing.
    /// </summary>
    public int? DefaultWindowSize { get; set; } = 10;
}
