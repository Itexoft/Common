// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Itexoft.Net;

public readonly struct NetPort(ushort port)
{
    private readonly ushort port = port;

    public static implicit operator NetPort(int port) => new((ushort)port);
    public static implicit operator int(NetPort port) => port.port;
    public override string ToString() => this.port.ToString(CultureInfo.InvariantCulture);
    public override int GetHashCode() => this.port.GetHashCode();

    public override bool Equals([NotNullWhen(true)] object? obj)
    {
        if (obj is not NetPort netPort)
            return false;

        return netPort.port == this.port;
    }
}
