// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Networking.P2P;

public enum P2PControlFrameType : byte
{
    Ping = 0x01,
    Pong = 0x02,
    ResumeRequest = 0x03,
    ResumeAcknowledge = 0x04,
    FlowControl = 0x05,
    Diagnostics = 0x06,
    Shutdown = 0x07,
    Custom = 0x20
}

public readonly record struct P2PControlFrame(
    P2PControlFrameType Type,
    long TimestampUtcTicks,
    long Sequence,
    ReadOnlyMemory<byte> Payload)
{
    public static P2PControlFrame Ping(long timestampUtcTicks, long sequence) =>
        new(P2PControlFrameType.Ping, timestampUtcTicks, sequence, ReadOnlyMemory<byte>.Empty);

    public static P2PControlFrame Pong(long timestampUtcTicks, long sequence, ReadOnlyMemory<byte> payload) =>
        new(P2PControlFrameType.Pong, timestampUtcTicks, sequence, payload);
}