// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.CompilerServices;

namespace Itexoft.IO.VFS.Core;

internal static class PageSizing
{
    public const int DefaultPageSize = 64 * 1024;
    private const int MinPageSize = 4 * 1024;
    private const int MaxPowerOfTwo = 1 << 20; // 1 MiB pages as a sanity cap

    internal static bool AllowTinyPages { get; set; }
    internal static int? DefaultPageSizeOverride { get; set; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Normalize(int? requested)
    {
        var size = requested ?? DefaultPageSizeOverride ?? DefaultPageSize;

        var min = AllowTinyPages ? 1 : MinPageSize;

        if (size < min || size > MaxPowerOfTwo || !IsPowerOfTwo(size))
            throw new ArgumentOutOfRangeException(
                nameof(requested),
                size,
                AllowTinyPages
                    ? "Page size must be a power of two between 1 byte and 1 MiB while tiny pages are enabled."
                    : "Page size must be power of two between 4 KiB and 1 MiB.");

        return size;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsPowerOfTwo(int value) => (value & (value - 1)) == 0;
}