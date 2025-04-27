// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Net;

public enum NetConnectionState : byte
{
    Disconnected = 0,

    Connecting = 10,
    Handshake = 20,
    Established = 30,

    Degraded = 40,
    Switching = 50,
    Reconnecting = 60,

    Failed = 200,
    Disposed = 250,
}

/// <summary>
/// Classification of transport failures used to select recovery strategy.
/// Byte-sized to serialize directly into diagnostic frames.
/// </summary>
public enum NetFailureSeverity : byte
{
    None = 0,
    Soft = 50,
    Hard = 100,
    Fatal = 200,
}

public enum NetDisconnectReason
{
    Manual = 0,
    Shutdown = 100,
    FatalError = 200,
}

public enum NetConnectionTransitionCause
{
    Unknown = 0,

    Manual = 10,

    DialStarted = 100,
    DialSucceeded = 110,
    DialFailed = 120,

    HandshakeStarted = 200,
    HandshakeCompleted = 210,

    HeartbeatRecovered = 300,
    HeartbeatMiss = 310,

    TransportError = 400,
    OsNetworkEvent = 410,

    ResumeAttempt = 500,
    ResumeSucceeded = 510,
    ResumeRejected = 520,

    BackoffElapsed = 600,

    SwitchStarted = 700,
    SwitchCompleted = 710,

    Disposal = 1000,
}
