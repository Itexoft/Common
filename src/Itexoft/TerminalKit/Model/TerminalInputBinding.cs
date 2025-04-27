// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Text.Json.Serialization;

namespace Itexoft.TerminalKit;

/// <summary>
/// Represents a gesture mapped to a logical action inside the console UI.
/// </summary>
public sealed class TerminalInputBinding
{
    /// <summary>
    /// Initializes the binding between a gesture and an action.
    /// </summary>
    /// <param name="mode">The navigation mode that interprets the gesture.</param>
    /// <param name="gesture">Human-readable gesture string.</param>
    /// <param name="action">Action identifier triggered by the gesture.</param>
    [JsonConstructor]
    public TerminalInputBinding(TerminalNavigationMode mode, string gesture, string action)
    {
        if (string.IsNullOrWhiteSpace(gesture))
            throw new ArgumentException("Gesture cannot be empty.", nameof(gesture));

        if (string.IsNullOrWhiteSpace(action))
            throw new ArgumentException("Action cannot be empty.", nameof(action));

        this.Mode = mode;
        this.Gesture = gesture;
        this.Action = action;
    }

    /// <summary>
    /// Gets the navigation mode that interprets the gesture.
    /// </summary>
    public TerminalNavigationMode Mode { get; }

    /// <summary>
    /// Gets the textual gesture description (e.g., <c>Ctrl+S</c>).
    /// </summary>
    public string Gesture { get; }

    /// <summary>
    /// Gets the logical action name.
    /// </summary>
    public string Action { get; }
}
