// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Itexoft.Extensions;

namespace Itexoft.Core;

[StructLayout(LayoutKind.Sequential), SkipLocalsInit]
public readonly record struct LString
{
    private const int slotCount = 8;
    private readonly long value;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public LString(string text) => this.value = Pack(text);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public LString(long packed) => this.value = packed;

    public int Length
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => CountNonZeroBytes(this.value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override string ToString() => Unpack(this.value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator LString(string text) => new(text);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator string(LString value) => value.ToString();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator long(LString value) => value.value;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator LString(long packed) => new(packed);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryCreate(string text, out LString value)
    {
        if (text is null)
        {
            value = default;

            return false;
        }

        value = new(Pack(text));

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long Pack(string text)
    {
        text.Required();

        var span = text.AsSpan();
        ulong packed = 0;
        var limit = span.Length < slotCount ? span.Length : slotCount;

        for (var i = 0; i < limit; i++)
            packed |= (ulong)(byte)span[i] << (8 * i);

        return unchecked((long)packed);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string Unpack(long packed)
    {
        Span<char> chars = stackalloc char[slotCount];
        var count = 0;

        for (var i = 0; i < slotCount; i++)
        {
            var b = (byte)((ulong)packed >> (8 * i));

            if (b == 0)
                break;

            chars[count++] = (char)b;
        }

        return new(chars[..count]);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int CountNonZeroBytes(long packed)
    {
        var len = 0;

        for (; len < slotCount; len++)
        {
            var b = (byte)((ulong)packed >> (8 * len));

            if (b == 0)
                break;
        }

        return len;
    }
}
