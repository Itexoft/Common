// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.CompilerServices;

namespace Itexoft.Threading.Core.Lane;

public struct Epoch64
{
    private int epoch;
    private ulong epoch0Mask;
    private ulong epoch1Mask;

    public ulong Epoch0Mask
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Volatile.Read(ref this.epoch0Mask);
    }

    public ulong Epoch1Mask
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Volatile.Read(ref this.epoch1Mask);
    }

    public int Epoch
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Volatile.Read(ref this.epoch);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Enter(in Lane64 lane)
    {
        if ((uint)lane.Index > 63u)
            throw new ArgumentOutOfRangeException(nameof(lane));

        var sw = new SpinWait();

        while (true)
        {
            var e = Volatile.Read(ref this.epoch);

            if ((e & 1) == 0)
                Interlocked.Or(ref this.epoch0Mask, lane.Bit);
            else
                Interlocked.Or(ref this.epoch1Mask, lane.Bit);

            if (Volatile.Read(ref this.epoch) == e)
                return e;

            if ((e & 1) == 0)
                Interlocked.And(ref this.epoch0Mask, ~lane.Bit);
            else
                Interlocked.And(ref this.epoch1Mask, ~lane.Bit);

            sw.SpinOnce();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Exit(in Lane64 lane, int enteredEpoch)
    {
        if ((uint)lane.Index > 63u)
            throw new ArgumentOutOfRangeException(nameof(lane));

        if ((enteredEpoch & 1) == 0)
        {
            Interlocked.And(ref this.epoch0Mask, ~lane.Bit);

            return;
        }

        Interlocked.And(ref this.epoch1Mask, ~lane.Bit);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int AdvanceAndWait()
    {
        var old = Interlocked.Increment(ref this.epoch) - 1;
        var sw = new SpinWait();

        if ((old & 1) == 0)
        {
            while (Volatile.Read(ref this.epoch0Mask) != 0)
                sw.SpinOnce();

            return old + 1;
        }

        while (Volatile.Read(ref this.epoch1Mask) != 0)
            sw.SpinOnce();

        return old + 1;
    }
}
