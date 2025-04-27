// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Itexoft.Core;

[StructLayout(LayoutKind.Sequential, Size = 4)]
public readonly record struct Latch
{
    public static implicit operator bool(Latch latch) => Volatile.Read(ref Unsafe.As<Latch, uint>(ref latch)) != 0;
    public static implicit operator Latch(bool latch) => Unsafe.As<bool, Latch>(ref latch);
    public static implicit operator int(Latch latch) => Volatile.Read(ref Unsafe.As<Latch, int>(ref latch));
    public static implicit operator uint(Latch latch) => Volatile.Read(ref Unsafe.As<Latch, uint>(ref latch));
}

public static class LatchExtensions
{
    extension(ref Latch latch)
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Try() => Interlocked.Increment(ref Unsafe.As<Latch, uint>(ref latch)) != 1;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Reset() => Interlocked.Exchange(ref Unsafe.As<Latch, uint>(ref latch), 0) != 1;
    }
}
