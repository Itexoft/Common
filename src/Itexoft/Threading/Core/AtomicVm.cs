// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Threading.Core;

public static class AtomicVm
{
    private const int opBits = 3;
    private const long opMask = (1L << opBits) - 1;

    private static long Add(ref long data, long operand)
    {
        var newValue = Interlocked.Add(ref data, operand);

        return unchecked(newValue - operand);
    }

    private static long And(ref long data, long operand) => Interlocked.And(ref data, operand);

    private static long Or(ref long data, long operand) => Interlocked.Or(ref data, operand);

    private static long SignExtend(long value, int bits)
    {
        var shift = 64 - bits;

        return (value << shift) >> shift;
    }

    public static long ProgramRead() => (long)Op.Read;

    public static long ProgramAddImm(long imm)
    {
        var payload = imm & ((1L << 60) - 1);

        return unchecked((payload << opBits) | (long)Op.AddImm);
    }

    public static long ProgramAddShifted(int shift, long delta)
    {
        var s = (long)(shift & 63);
        var d = delta & ((1L << 54) - 1);
        var payload = unchecked(s | (d << 6));

        return unchecked((payload << opBits) | (long)Op.AddShifted);
    }

    public static long ProgramOrBit(int bit)
    {
        var payload = (long)(bit & 63);

        return unchecked((payload << opBits) | (long)Op.OrBit);
    }

    public static long ProgramAndNotBit(int bit)
    {
        var payload = (long)(bit & 63);

        return unchecked((payload << opBits) | (long)Op.AndNotBit);
    }

    private static long ExpandOperand(long program, out Op op)
    {
        op = (Op)(program & opMask);

        if (op == Op.Read)
            return 0;

        var payload = program >>> opBits;

        if (op == Op.OrBit)
            return unchecked((long)(1UL << (int)(payload & 63)));

        if (op == Op.AndNotBit)
            return unchecked((long)~(1UL << (int)(payload & 63)));

        if (op == Op.AddShifted)
        {
            var shift = (int)(payload & 63);
            var delta = SignExtend(payload >> 6, 54);

            return unchecked(delta << shift);
        }

        if (op == Op.AddImm)
            return SignExtend(payload, 60);

        return 0;
    }

    public static long CompileExecute(ref long data, long program)
    {
        var operand = ExpandOperand(program, out var op);

        if (op == Op.AndNotBit)
            return And(ref data, operand);

        if (op == Op.OrBit || op == Op.Read)
            return Or(ref data, operand);

        return Add(ref data, operand);
    }

    public static int TryAcquireBit(ref long bitmap, int bit)
    {
        var old = CompileExecute(ref bitmap, ProgramOrBit(bit));
        var mask = 1L << (bit & 63);

        return (old & mask) == 0 ? bit & 63 : 64;
    }

    public static void ReleaseBit(ref long bitmap, int bit) => CompileExecute(ref bitmap, ProgramAndNotBit(bit));

    private enum Op : long
    {
        AddImm = 0,
        AddShifted = 1,
        OrBit = 2,
        AndNotBit = 3,
        Read = 4,
    }
}
