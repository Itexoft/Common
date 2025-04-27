// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.TerminalKit.Presets;

/// <summary>
/// Binds runtime state objects to preset state slice names.
/// </summary>
public sealed class TerminalCrudScreenState
{
    /// <summary>
    /// Gets the collection of items rendered by the preset.
    /// </summary>
    public object Items { get; init; } = Array.Empty<object>();

    /// <summary>
    /// Gets the selection metadata shared across components.
    /// </summary>
    public TerminalSelectionState Selection { get; init; } = new();

    /// <summary>
    /// Gets the viewport window for scrollable components.
    /// </summary>
    public TerminalViewportState Viewport { get; init; } = new();

    /// <summary>
    /// Gets additional state slices exposed to the DSL.
    /// </summary>
    public IReadOnlyDictionary<string, object?> Additional { get; init; } = new Dictionary<string, object?>();
}
