// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.CompilerServices;

namespace Itexoft.Threading.Core;

[InlineArray(64)]
public struct Inline64<T>
{
    private T element0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ref T Get(ref Inline64<T> value, int index)
    {
        if ((uint)index > 63u)
            throw new ArgumentOutOfRangeException(nameof(index));

        return ref Unsafe.Add(ref value.element0, index);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ref T GetUnchecked(ref Inline64<T> value, int index) =>
        ref Unsafe.Add(ref value.element0, index);
}
