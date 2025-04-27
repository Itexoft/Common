// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Itexoft.Threading.Core;

[method: MethodImpl(MethodImplOptions.AggressiveInlining)]
[StructLayout(LayoutKind.Sequential)]
file readonly struct LargeKey(ulong a, ulong b, ulong c, ulong d) : IEquatable<LargeKey>
{
    private readonly ulong a = a;
    private readonly ulong b = b;
    private readonly ulong c = c;
    private readonly ulong d = d;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(LargeKey other) => this.a == other.a && this.b == other.b && this.c == other.c && this.d == other.d;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(in LargeKey other) => this.a == other.a && this.b == other.b && this.c == other.c && this.d == other.d;

    public override bool Equals(object? obj) => obj is LargeKey other && this.Equals(other);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override int GetHashCode()
    {
        unchecked
        {
            var x = this.a * 11400714819323198485UL;
            x = (x ^ this.b) * 14029467366897019727UL;
            x = (x ^ this.c) * 1609587929392839161UL;
            x ^= this.d;

            return (int)(x ^ (x >> 32));
        }
    }

    public static bool operator ==(LargeKey left, LargeKey right) => left.Equals(right);

    public static bool operator !=(LargeKey left, LargeKey right) => !left.Equals(right);
}
