// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Networking.Reliable;

/// <summary>
/// Provides details when the state of a reliable connection changes.
/// </summary>
public sealed class ReliableConnectionStateChangedEventArgs : EventArgs
{
    /// <summary>
    /// Creates a new instance describing a state transition.
    /// </summary>
    /// <param name="previous">Previous state.</param>
    /// <param name="current">Current state.</param>
    /// <param name="cause">Human-readable cause of the transition.</param>
    /// <param name="evidence">Optional evidence such as endpoint or exception message.</param>
    public ReliableConnectionStateChangedEventArgs(
        ReliableConnectionState previous,
        ReliableConnectionState current,
        string cause,
        string? evidence)
    {
        this.PreviousState = previous;
        this.CurrentState = current;
        this.Cause = cause;
        this.Evidence = evidence;
    }

    /// <summary>
    /// Previous connection state.
    /// </summary>
    public ReliableConnectionState PreviousState { get; }

    /// <summary>
    /// Current connection state.
    /// </summary>
    public ReliableConnectionState CurrentState { get; }

    /// <summary>
    /// Human-readable reason registered by the connection manager.
    /// </summary>
    public string Cause { get; }

    /// <summary>
    /// Optional evidence such as endpoint, exception message, etc.
    /// </summary>
    public string? Evidence { get; }
}