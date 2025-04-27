// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.CompilerServices;

namespace Itexoft.Threading.Core.Lane;

public readonly struct Lane64
{
    public readonly int Index;
    public readonly ulong Bit;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Lane64(int index)
    {
        if ((uint)index > 63u)
            throw new ArgumentOutOfRangeException(nameof(index));

        this.Index = index;
        this.Bit = 1UL << index;
    }
}
