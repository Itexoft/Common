// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Networking.P2P;

public enum P2PHandshakeMessageType
{
    ClientHello,
    ServerHello,
    ResumeRequest,
    ResumeAcknowledge,
    Error
}

public sealed class P2PHandshakeEnvelope
{
    public P2PHandshakeMessageType Type { get; set; }
    public string? NodeId { get; set; }
    public string? SessionId { get; set; }
    public long SessionEpoch { get; set; }
    public string? ResumeToken { get; set; }
    public long TimestampTicks { get; set; }
    public string? Message { get; set; }
    public IReadOnlyDictionary<string, string>? Metadata { get; set; }
}