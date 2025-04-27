// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Text.Json.Serialization;

namespace Itexoft.TerminalKit;

/// <summary>
/// Associates a component event with an application handler.
/// </summary>
public sealed class TerminalEventBinding
{
    /// <summary>
    /// Initializes the binding with the specified event and handler.
    /// </summary>
    /// <param name="eventKey">Event exposed by a component.</param>
    /// <param name="handler">Application handler name.</param>
    [JsonConstructor]
    public TerminalEventBinding(TerminalEventKey eventKey, string handler)
    {
        if (string.IsNullOrWhiteSpace(handler))
            throw new ArgumentException("Handler name cannot be empty.", nameof(handler));

        this.Event = eventKey;
        this.Handler = handler;
    }

    /// <summary>
    /// Gets the event exposed by the component.
    /// </summary>
    public TerminalEventKey Event { get; }

    /// <summary>
    /// Gets the handler name invoked by the runtime.
    /// </summary>
    public string Handler { get; }
}
