// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.CompilerServices;

namespace Itexoft.Threading.Core.Lane;

public struct LaneLatch64
{
    private readonly ulong participantsMask;
    private ulong arrivedMask;

    public LaneLatch64(LaneMask64 participants)
    {
        if (participants.IsEmpty())
            throw new ArgumentOutOfRangeException(nameof(participants));

        this.participantsMask = participants.Mask;
    }

    public ulong ArrivedMask
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Volatile.Read(ref this.arrivedMask);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Arrive(in Lane64 lane)
    {
        if ((this.participantsMask & lane.Bit) == 0)
            throw new ArgumentOutOfRangeException(nameof(lane));

        var original = Interlocked.Or(ref this.arrivedMask, lane.Bit);

        return (original | lane.Bit) == this.participantsMask;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ArriveAndWait(in Lane64 lane)
    {
        if (this.Arrive(in lane))
            return true;

        var sw = new SpinWait();

        while (Volatile.Read(ref this.arrivedMask) != this.participantsMask)
            sw.SpinOnce();

        return false;
    }
}
