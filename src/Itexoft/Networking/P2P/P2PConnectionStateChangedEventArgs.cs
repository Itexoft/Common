// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Networking.P2P;

/// <summary>
/// Provides details about a state transition in the connection manager.
/// </summary>
public sealed class P2PConnectionStateChangedEventArgs : EventArgs
{
    public P2PConnectionStateChangedEventArgs(
        P2PConnectionState previous,
        P2PConnectionState current,
        P2PConnectionTransitionCause cause,
        string? evidence = null)
    {
        this.PreviousState = previous;
        this.CurrentState = current;
        this.Cause = cause;
        this.Evidence = evidence;
        this.Timestamp = DateTimeOffset.UtcNow;
    }

    public P2PConnectionState PreviousState { get; }

    public P2PConnectionState CurrentState { get; }

    /// <summary>
    /// Classification of the event that triggered the transition.
    /// </summary>
    public P2PConnectionTransitionCause Cause { get; }

    /// <summary>
    /// Short textual description of the evidence that led to the transition.
    /// </summary>
    public string? Evidence { get; }

    /// <summary>
    /// UTC timestamp at which the transition was recorded.
    /// </summary>
    public DateTimeOffset Timestamp { get; }
}

public enum P2PConnectionTransitionCause
{
    Unknown = 0,
    Manual = 1,
    DialStarted = 2,
    DialSucceeded = 3,
    DialFailed = 4,
    HandshakeStarted = 5,
    HandshakeCompleted = 6,
    HeartbeatRecovered = 7,
    HeartbeatMiss = 8,
    TransportError = 9,
    OsNetworkEvent = 10,
    ResumeAttempt = 11,
    ResumeSucceeded = 12,
    ResumeRejected = 13,
    BackoffElapsed = 14,
    Disposal = 15,
    SwitchStarted = 16,
    SwitchCompleted = 17
}