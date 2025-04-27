// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Itexoft.Threading;

internal static class Compiler
{
    private const int dataBits = 6;
    private const ulong dataMask = (1UL << dataBits) - 1;

    private const int metaShift = dataBits;
    private const int metaBits = 64 - dataBits;
    private const ulong metaMask = (1UL << metaBits) - 1;

    private const byte opNop = 0;
    private const byte opAcquireFirstZeroMetaBit = 1;
    private const byte opMetaOr = 2;
    private const byte opMetaAnd = 3;
    private const byte opMetaAdd = 4;
    private const byte opExchangeMeta = 5;
    private const byte opUpdateData = 6;

    private static readonly long mpAcquireFirstZeroMetaBit = Code(opAcquireFirstZeroMetaBit, 0);
    private static readonly long mpMetaAdd1 = Code(opMetaAdd, 1);
    private static readonly long mpMetaOrBit0 = Code(opMetaOr, 1);
    private static readonly long mpMetaClearAll = Code(opMetaAnd, 0);
    private static readonly long mpDataSet010101 = Code(opUpdateData, 0x15);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ref long Word(ref byte state) => ref Unsafe.As<byte, long>(ref state);

    public static long Code(byte op, ulong imm58)
    {
        imm58 &= metaMask;

        return unchecked((long)((imm58 << dataBits) | (ulong)(op & (byte)dataMask)));
    }

    public static byte Execute(ref byte state, long microcode)
    {
        var op = (byte)((ulong)microcode & dataMask);
        var imm = (ulong)microcode >> dataBits;

        if (op == opAcquireFirstZeroMetaBit)
            return AcquireFirstZeroMetaBit(ref state);

        if (op == opMetaOr)
            return OrMeta(ref state, imm);

        if (op == opMetaAnd)
            return AndMeta(ref state, imm);

        if (op == opMetaAdd)
            return AddMeta(ref state, SignExtend58(imm));

        if (op == opExchangeMeta)
            return ExchangeMeta(ref state, imm);

        if (op == opUpdateData)
            return UpdateData(ref state, imm);

        return ReadData(ref state);
    }

    public static byte Execute(Span<byte> state, long microcode)
    {
        if ((uint)state.Length < 8u)
            throw new ArgumentOutOfRangeException(nameof(state));

        return Execute(ref MemoryMarshal.GetReference(state), microcode);
    }

    public static unsafe byte Execute(byte* state, long microcode) => Execute(ref Unsafe.AsRef<byte>(state), microcode);

    public static byte ReadData(ref byte state)
    {
        var s = (ulong)Word(ref state);

        return (byte)(s & dataMask);
    }

    private static long SignExtend58(ulong imm)
    {
        const ulong signBit = 1UL << (metaBits - 1);

        if ((imm & signBit) == 0)
            return (long)imm;

        return unchecked((long)(imm | ~metaMask));
    }

    public static byte AddMeta(ref byte state, long metaDelta)
    {
        ref var w = ref Word(ref state);
        var delta = metaDelta << metaShift;
        var newValue = Interlocked.Add(ref w, delta);

        return (byte)((ulong)newValue & dataMask);
    }

    public static byte OrMeta(ref byte state, ulong metaOrMask)
    {
        ref var w = ref Word(ref state);
        var mask = (long)(metaOrMask << metaShift);
        var old = Interlocked.Or(ref w, mask);
        var newValue = (ulong)old | (ulong)mask;

        return (byte)(newValue & dataMask);
    }

    public static byte AndMeta(ref byte state, ulong metaKeepMask)
    {
        ref var w = ref Word(ref state);
        var mask = (long)((metaKeepMask << metaShift) | dataMask);
        var old = Interlocked.And(ref w, mask);
        var newValue = (ulong)old & (ulong)mask;

        return (byte)(newValue & dataMask);
    }

    public static byte ExchangeMeta(ref byte state, ulong newMeta)
    {
        ref var w = ref Word(ref state);
        var snapshot = (ulong)w;
        var next = unchecked((long)((newMeta << metaShift) | (snapshot & dataMask)));
        Interlocked.Exchange(ref w, next);

        return (byte)(snapshot & dataMask);
    }

    public static byte UpdateData(ref byte state, ulong imm)
    {
        var setMask = (byte)(imm & dataMask);
        var clearMask = (byte)((imm >> dataBits) & dataMask);

        if (clearMask == 0)
            return OrData(ref state, setMask);

        if (setMask == 0)
            return AndData(ref state, clearMask);

        return CasUpdateData(ref state, setMask, clearMask);
    }

    public static byte OrData(ref byte state, byte setMask)
    {
        setMask &= (byte)dataMask;

        ref var w = ref Word(ref state);
        var mask = (long)(ulong)setMask;
        var old = Interlocked.Or(ref w, mask);
        var newValue = (ulong)old | (ulong)mask;

        return (byte)(newValue & dataMask);
    }

    public static byte AndData(ref byte state, byte clearMask)
    {
        clearMask &= (byte)dataMask;

        ref var w = ref Word(ref state);
        var maskU = ~((ulong)clearMask & dataMask);
        var old = Interlocked.And(ref w, (long)maskU);
        var newValue = (ulong)old & maskU;

        return (byte)(newValue & dataMask);
    }

    private static byte CasUpdateData(ref byte state, byte setMask, byte clearMask)
    {
        ref var w = ref Word(ref state);
        var s = w;
        var su = (ulong)s;
        var data = (byte)(su & dataMask);
        var nextData = (byte)((data & ~clearMask) | setMask);
        var next = unchecked((long)((su & ~dataMask) | nextData));
        var old = Interlocked.CompareExchange(ref w, next, s);

        if (old != s)
            return 0;

        return nextData;
    }

    public static byte AcquireFirstZeroMetaBit(ref byte state)
    {
        ref var w = ref Word(ref state);
        var s = w;
        var su = (ulong)s;

        var meta = (su >> metaShift) & metaMask;
        var rightZero = ~meta & (meta + 1) & metaMask;

        if (rightZero == 0)
            return 0;

        var rel = BitOperations.TrailingZeroCount(rightZero);
        var absIndex = (byte)(metaShift + rel);

        var next = (su & ~dataMask) | absIndex;
        next |= rightZero << metaShift;

        var old = Interlocked.CompareExchange(ref w, unchecked((long)next), s);

        if (old != s)
            return 0;

        return absIndex;
    }

    public static byte TrySetMetaBit(ref byte state, byte metaIndex)
    {
        if (metaIndex >= metaBits)
            return 0;

        ref var w = ref Word(ref state);
        var bit = 1UL << (metaShift + metaIndex);
        var old = Interlocked.Or(ref w, unchecked((long)bit));

        return (byte)(((ulong)old & bit) == 0 ? 1 : 0);
    }

    public static byte TryClearMetaBit(ref byte state, byte metaIndex)
    {
        if (metaIndex >= metaBits)
            return 0;

        ref var w = ref Word(ref state);
        var bit = 1UL << (metaShift + metaIndex);
        var mask = unchecked((long)~bit);
        var old = Interlocked.And(ref w, mask);

        return (byte)(((ulong)old & bit) != 0 ? 1 : 0);
    }

    public ref struct Machine
    {
        public ref long State;

        public Machine(long initial) => this.State = initial;

        public ref byte Data => ref Unsafe.As<long, byte>(ref this.State);

        public Span<byte> AsSpan() => MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref this.State, 1));
    }
}
