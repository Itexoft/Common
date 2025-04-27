// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Numerics;
using System.Runtime.CompilerServices;

namespace Itexoft.Threading.Core.Lane;

[method: MethodImpl(MethodImplOptions.AggressiveInlining)]
public readonly struct LaneMask64(ulong mask)
{
    public readonly ulong Mask = mask;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static LaneMask64 FromCount(int participants)
    {
        if ((uint)participants > 64u)
            throw new ArgumentOutOfRangeException(nameof(participants));

        if (participants == 64)
            return new(ulong.MaxValue);

        return new((1UL << participants) - 1);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains(in Lane64 lane) => (this.Mask & lane.Bit) != 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int PopCount() => BitOperations.PopCount(this.Mask);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsEmpty() => this.Mask == 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public LaneMask64 Add(in Lane64 lane) => new(this.Mask | lane.Bit);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public LaneMask64 Remove(in Lane64 lane) => new(this.Mask & ~lane.Bit);
}
