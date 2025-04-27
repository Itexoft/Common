// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Networking.Core;

namespace Itexoft.Networking.P2P;

public sealed class P2PHandshakeException : Exception
{
    public P2PHandshakeException(FailureSeverity severity, string message)
        : base(message) => this.Severity = severity;

    public P2PHandshakeException(FailureSeverity severity, string message, Exception innerException)
        : base(message, innerException) => this.Severity = severity;

    public FailureSeverity Severity { get; }
}