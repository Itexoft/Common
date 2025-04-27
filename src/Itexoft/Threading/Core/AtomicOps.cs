// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.CompilerServices;

namespace Itexoft.Threading;

public static class Atomic64
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong Read(ref ulong location) => Volatile.Read(ref location);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Write(ref ulong location, ulong value) => Volatile.Write(ref location, value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong Exchange(ref ulong location, ulong value) => Interlocked.Exchange(ref location, value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong Mask(int index) => 1UL << index;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TrySetBit(ref ulong location, int index)
    {
        var bit = 1UL << index;
        var original = Interlocked.Or(ref location, bit);

        return (original & bit) == 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryClearBit(ref ulong location, int index)
    {
        var bit = 1UL << index;
        var original = Interlocked.And(ref location, ~bit);

        return (original & bit) != 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsBitSet(ulong value, int index) => (value & (1UL << index)) != 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TrySetMask(ref ulong location, ulong mask)
    {
        var original = Interlocked.Or(ref location, mask);

        return (original & mask) != mask;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryClearMask(ref ulong location, ulong mask)
    {
        var original = Interlocked.And(ref location, ~mask);

        return (original & mask) != 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryAcquireMask(ref ulong location, ulong mask)
    {
        var original = Volatile.Read(ref location);

        if ((original & mask) != 0)
            return false;

        var desired = original | mask;

        return Interlocked.CompareExchange(ref location, desired, original) == original;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryReleaseMask(ref ulong location, ulong mask)
    {
        var original = Volatile.Read(ref location);

        if ((original & mask) != mask)
            return false;

        var desired = original & ~mask;

        return Interlocked.CompareExchange(ref location, desired, original) == original;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Arrive(ref ulong location, int index, ulong fullMask)
    {
        var bit = 1UL << index;
        var original = Interlocked.Or(ref location, bit);

        return (original | bit) == fullMask;
    }
}

public static class Atomic8
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong Pack(byte b0, byte b1, byte b2, byte b3, byte b4, byte b5, byte b6, byte b7) =>
        (ulong)b0
        | ((ulong)b1 << 8)
        | ((ulong)b2 << 16)
        | ((ulong)b3 << 24)
        | ((ulong)b4 << 32)
        | ((ulong)b5 << 40)
        | ((ulong)b6 << 48)
        | ((ulong)b7 << 56);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Unpack(ulong value, out byte b0, out byte b1, out byte b2, out byte b3, out byte b4, out byte b5, out byte b6, out byte b7)
    {
        b0 = (byte)value;
        b1 = (byte)(value >> 8);
        b2 = (byte)(value >> 16);
        b3 = (byte)(value >> 24);
        b4 = (byte)(value >> 32);
        b5 = (byte)(value >> 40);
        b6 = (byte)(value >> 48);
        b7 = (byte)(value >> 56);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte GetLane(ulong value, int lane) => (byte)(value >> (lane << 3));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong WithLane(ulong value, int lane, byte laneValue)
    {
        var shift = lane << 3;
        var mask = 0xFFUL << shift;

        return (value & ~mask) | ((ulong)laneValue << shift);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte OrLaneFlags(ref ulong location, int lane, byte flags)
    {
        var shift = lane << 3;
        var mask = (ulong)flags << shift;
        var original = Interlocked.Or(ref location, mask);

        return (byte)(original >> shift);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte ClearLaneFlags(ref ulong location, int lane, byte flags)
    {
        var shift = lane << 3;
        var mask = ~((ulong)flags << shift);
        var original = Interlocked.And(ref location, mask);

        return (byte)(original >> shift);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryWriteLane(ref ulong location, int lane, byte value, out byte originalLane, out byte newLane)
    {
        var original = Volatile.Read(ref location);

        originalLane = GetLane(original, lane);
        newLane = value;

        var desired = WithLane(original, lane, value);

        return Interlocked.CompareExchange(ref location, desired, original) == original;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryAddLane(ref ulong location, int lane, byte delta, out byte originalLane, out byte newLane)
    {
        var original = Volatile.Read(ref location);

        originalLane = GetLane(original, lane);
        newLane = unchecked((byte)(originalLane + delta));

        var desired = WithLane(original, lane, newLane);

        return Interlocked.CompareExchange(ref location, desired, original) == original;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryIncrementLane(ref ulong location, int lane, out byte originalLane, out byte newLane) =>
        TryAddLane(ref location, lane, 1, out originalLane, out newLane);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryDecrementLane(ref ulong location, int lane, out byte originalLane, out byte newLane) =>
        TryAddLane(ref location, lane, unchecked((byte)-1), out originalLane, out newLane);
}

public static class Atomic32
{
    private const ulong lowMask = 0xFFFFFFFFUL;
    private const ulong highMask = 0xFFFFFFFF00000000UL;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong Pack(int low, int high) => (ulong)(uint)low | ((ulong)(uint)high << 32);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Unpack(ulong value, out int low, out int high)
    {
        low = unchecked((int)(uint)value);
        high = unchecked((int)(uint)(value >> 32));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Low(ulong value) => unchecked((int)(uint)value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int High(ulong value) => unchecked((int)(uint)(value >> 32));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong Read(ref ulong location) => Volatile.Read(ref location);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Read(ref ulong location, out int low, out int high) => Unpack(Volatile.Read(ref location), out low, out high);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong Exchange(ref ulong location, int low, int high) => Interlocked.Exchange(ref location, Pack(low, high));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool CompareExchange(
        ref ulong location,
        int low,
        int high,
        int expectedLow,
        int expectedHigh,
        out int originalLow,
        out int originalHigh)
    {
        var expected = Pack(expectedLow, expectedHigh);
        var desired = Pack(low, high);
        var original = Interlocked.CompareExchange(ref location, desired, expected);

        Unpack(original, out originalLow, out originalHigh);

        return original == expected;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int OrLow(ref ulong location, int mask)
    {
        var delta = (ulong)(uint)mask;
        var original = Interlocked.Or(ref location, delta);

        return Low(original | delta);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int OrHigh(ref ulong location, int mask)
    {
        var delta = (ulong)(uint)mask << 32;
        var original = Interlocked.Or(ref location, delta);

        return High(original | delta);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int AndLow(ref ulong location, int mask)
    {
        var andMask = (ulong)(uint)mask | highMask;
        var original = Interlocked.And(ref location, andMask);

        return Low(original & andMask);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int AndHigh(ref ulong location, int mask)
    {
        var andMask = lowMask | ((ulong)(uint)mask << 32);
        var original = Interlocked.And(ref location, andMask);

        return High(original & andMask);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryWriteLow(ref ulong location, int value, out int originalLow, out int originalHigh)
    {
        var original = Volatile.Read(ref location);

        originalLow = Low(original);
        originalHigh = High(original);

        var desired = (original & highMask) | (uint)value;

        return Interlocked.CompareExchange(ref location, desired, original) == original;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryWriteHigh(ref ulong location, int value, out int originalLow, out int originalHigh)
    {
        var original = Volatile.Read(ref location);

        originalLow = Low(original);
        originalHigh = High(original);

        var desired = (original & lowMask) | ((ulong)(uint)value << 32);

        return Interlocked.CompareExchange(ref location, desired, original) == original;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryAddLow(ref ulong location, int delta, out int originalLow, out int originalHigh, out int newLow)
    {
        var original = Volatile.Read(ref location);

        originalLow = Low(original);
        originalHigh = High(original);

        newLow = unchecked(originalLow + delta);

        var desired = (original & highMask) | (uint)newLow;

        return Interlocked.CompareExchange(ref location, desired, original) == original;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryAddHigh(ref ulong location, int delta, out int originalLow, out int originalHigh, out int newHigh)
    {
        var original = Volatile.Read(ref location);

        originalLow = Low(original);
        originalHigh = High(original);

        newHigh = unchecked(originalHigh + delta);

        var desired = (original & lowMask) | ((ulong)(uint)newHigh << 32);

        return Interlocked.CompareExchange(ref location, desired, original) == original;
    }
}
