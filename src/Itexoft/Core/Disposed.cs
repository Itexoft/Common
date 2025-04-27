// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Itexoft.Core;

[StructLayout(LayoutKind.Sequential, Size = 4)]
public readonly record struct Disposed
{
    public static implicit operator bool(Disposed disposed) => Unsafe.As<Disposed, uint>(ref disposed) != 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ThrowIf([CallerMemberName] string callerMember = "", [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0)
    {
        if (this)
            throw new ObjectDisposedException(callerMember, $"Cannot access a disposed object: {callerMember} ({callerFile}:{callerLine}).");
    }
}

public static class DisposedExtensions
{
    extension(ref Disposed disposed)
    {
        public uint Count => Unsafe.As<Disposed, uint>(ref disposed);
        public bool Enter() => Interlocked.Increment(ref Unsafe.As<Disposed, uint>(ref disposed)) != 1;
    }
}
