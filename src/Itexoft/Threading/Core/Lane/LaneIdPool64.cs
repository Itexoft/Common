// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Numerics;
using System.Runtime.CompilerServices;

namespace Itexoft.Threading.Core.Lane;

public sealed class LaneIdPool64
{
    private ulong inUse;

    public ulong InUseMask
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Volatile.Read(ref this.inUse);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryAcquire(int index, out Lane64 lane)
    {
        if ((uint)index > 63u)
            throw new ArgumentOutOfRangeException(nameof(index));

        var bit = 1UL << index;
        var original = Interlocked.Or(ref this.inUse, bit);

        if ((original & bit) != 0)
        {
            lane = default;

            return false;
        }

        lane = new(index);

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryAcquire(out Lane64 lane)
    {
        var sw = new SpinWait();

        while (true)
        {
            var snapshot = Volatile.Read(ref this.inUse);
            var free = ~snapshot;

            if (free == 0)
            {
                lane = default;

                return false;
            }

            var index = BitOperations.TrailingZeroCount(free);

            if (this.TryAcquire(index, out lane))
                return true;

            sw.SpinOnce();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Lane64 Acquire()
    {
        var sw = new SpinWait();

        Lane64 lane;

        while (!this.TryAcquire(out lane))
            sw.SpinOnce();

        return lane;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Release(in Lane64 lane)
    {
        if ((uint)lane.Index > 63u)
            throw new ArgumentOutOfRangeException(nameof(lane));

        Interlocked.And(ref this.inUse, ~lane.Bit);
    }
}
