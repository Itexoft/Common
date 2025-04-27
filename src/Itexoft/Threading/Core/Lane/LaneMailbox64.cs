// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Numerics;
using System.Runtime.CompilerServices;
using Itexoft.Interop;

namespace Itexoft.Threading.Core.Lane;

public sealed class LaneMailbox64<T>
{
    private readonly T[] slots = new T[64];
    private ulong pendingMask;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ulong ReadPendingMask() => Volatile.Read(ref this.pendingMask);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryPost(in Lane64 lane, in T value)
    {
        if ((uint)lane.Index > 63u)
            throw new ArgumentOutOfRangeException(nameof(lane));

        var bit = lane.Bit;

        if ((Volatile.Read(ref this.pendingMask) & bit) != 0)
            return false;

        UnsafeArray.AtUnchecked(this.slots, lane.Index) = value;
        Interlocked.Or(ref this.pendingMask, bit);

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ulong DrainMask() => Interlocked.Exchange(ref this.pendingMask, 0);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryTakeNext(ref ulong mask, out int laneIndex, out T value)
    {
        if (mask == 0)
        {
            laneIndex = -1;
            value = default!;

            return false;
        }

        laneIndex = BitOperations.TrailingZeroCount(mask);
        mask &= mask - 1;

        ref var slot = ref UnsafeArray.AtUnchecked(this.slots, laneIndex);
        value = slot;
        slot = default!;

        return true;
    }
}
