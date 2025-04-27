// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Diagnostics.CodeAnalysis;
using Itexoft.IO.VFS.Core;

namespace Itexoft.IO.VFS.Storage;

/// <summary>
/// Provides read-only inspection helpers for superblock metadata without mutating the underlying stream.
/// </summary>
internal static class SuperblockInspector
{
    /// <summary>
    /// Attempts to read the newest valid superblock header from the supplied seekable stream.
    /// </summary>
    /// <param name="stream">Stream to inspect. The method temporarily rewinds it to the beginning and restores the original position.</param>
    /// <param name="state">Resulting superblock state when <paramref name="hasValidSuperblock" /> is <c>true</c>.</param>
    /// <param name="pageSize">Page size declared by the inspected superblock.</param>
    /// <param name="hasValidSuperblock"><c>true</c> when a valid header was found; otherwise <c>false</c>.</param>
    public static void Inspect(Stream stream, out SuperblockLayout.SuperblockState state, out int pageSize, out bool hasValidSuperblock)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!stream.CanSeek)
            throw new ArgumentException("Stream must support seeking.", nameof(stream));

        var originalPosition = stream.Position;
        try
        {
            foreach (var candidateSize in EnumerateCandidatePageSizes())
            {
                if (stream.Length < candidateSize)
                    continue;

                if (!TryReadPage(stream, 0, candidateSize, out var buffer0))
                    continue;
                var valid0 = SuperblockLayout.TryParse(buffer0, out var state0);

                var valid1 = false;
                SuperblockLayout.SuperblockState state1 = default;
                byte[]? buffer1 = null;
                if (stream.Length >= candidateSize * 2 && TryReadPage(stream, candidateSize, candidateSize, out buffer1))
                    valid1 = SuperblockLayout.TryParse(buffer1, out state1);

                if (!valid0 && !valid1)
                    continue;

                if (!valid1 || (valid0 && state0.Generation >= state1.Generation))
                {
                    state = state0;
                    pageSize = state0.PageSize;
                }
                else
                {
                    state = state1;
                    pageSize = state1.PageSize;
                }

                hasValidSuperblock = true;

                return;
            }

            hasValidSuperblock = false;
            state = default;
            pageSize = PageSizing.DefaultPageSizeOverride ?? PageSizing.DefaultPageSize;
        }
        finally
        {
            stream.Position = originalPosition;
        }
    }

    private static IEnumerable<int> EnumerateCandidatePageSizes()
    {
        if (PageSizing.AllowTinyPages)
        {
            if (PageSizing.DefaultPageSizeOverride.HasValue)
                yield return Math.Max(PageSizing.DefaultPageSizeOverride.Value, SuperblockLayout.HeaderLength);

            yield return 4 * 1024;
        }
        else
        {
            yield return 4 * 1024;
        }

        yield return 8 * 1024;
        yield return 16 * 1024;
        yield return 32 * 1024;
        yield return 64 * 1024;
        yield return 128 * 1024;
        yield return 256 * 1024;
        yield return 512 * 1024;
        yield return 1024 * 1024;
    }

    private static bool TryReadPage(Stream stream, long offset, int length, [NotNullWhen(true)] out byte[]? buffer)
    {
        var originalPosition = stream.Position;
        buffer = null;

        try
        {
            stream.Position = offset;
            buffer = new byte[length];

            var totalRead = 0;
            while (totalRead < length)
            {
                var read = stream.Read(buffer, totalRead, length - totalRead);
                if (read == 0)
                {
                    buffer = null;

                    return false;
                }

                totalRead += read;
            }

            return true;
        }
        finally
        {
            stream.Position = originalPosition;
        }
    }
}