// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Itexoft.Interop;

public static class UnsafeArray
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ref T AtUnchecked<T>(T[] array, int index) =>
        ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(array), index);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ref T At<T>(T[] array, int index)
    {
        if ((uint)index >= (uint)array.Length)
            throw new ArgumentOutOfRangeException(nameof(index));

        return ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(array), index);
    }
}
