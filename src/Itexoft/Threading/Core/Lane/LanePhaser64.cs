// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.CompilerServices;

namespace Itexoft.Threading.Core.Lane;

public sealed class LanePhaser64
{
    private readonly ulong participantsMask;
    private ulong arrivedMask;
    private int phase;

    public LanePhaser64(LaneMask64 participants)
    {
        if (participants.IsEmpty())
            throw new ArgumentOutOfRangeException(nameof(participants));

        this.participantsMask = participants.Mask;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int ReadPhase() => Volatile.Read(ref this.phase);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ulong ReadArrivedMask() => Volatile.Read(ref this.arrivedMask);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Arrive(in Lane64 lane)
    {
        if ((this.participantsMask & lane.Bit) == 0)
            throw new ArgumentOutOfRangeException(nameof(lane));

        var original = Interlocked.Or(ref this.arrivedMask, lane.Bit);

        if ((original | lane.Bit) != this.participantsMask)
            return false;

        Interlocked.Exchange(ref this.arrivedMask, 0);
        Interlocked.Increment(ref this.phase);

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ArriveAndAwaitAdvance(in Lane64 lane)
    {
        var myPhase = Volatile.Read(ref this.phase);

        if (this.Arrive(in lane))
            return true;

        var sw = new SpinWait();

        while (Volatile.Read(ref this.phase) == myPhase)
            sw.SpinOnce();

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AwaitAdvance(int observedPhase)
    {
        var sw = new SpinWait();

        while (Volatile.Read(ref this.phase) == observedPhase)
            sw.SpinOnce();
    }
}
