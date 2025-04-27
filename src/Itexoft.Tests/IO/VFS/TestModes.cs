// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.CompilerServices;

namespace Itexoft.IO.VFS.Tests;

internal readonly record struct TestMode(string Name, bool EnableMirroring, int IoChunkSize)
{
    public override string ToString() => this.Name;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteAll(Stream stream, ReadOnlySpan<byte> payload)
    {
        if (payload.Length == 0)
            return;

        var chunk = Math.Clamp(this.IoChunkSize, 1, payload.Length);
        var offset = 0;
        while (offset < payload.Length)
        {
            var size = Math.Min(chunk, payload.Length - offset);
            stream.Write(payload.Slice(offset, size));
            offset += size;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteAll(Stream stream, byte[] payload) => this.WriteAll(stream, payload.AsSpan());

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int ReadExact(Stream stream, Span<byte> destination)
    {
        var total = 0;
        while (total < destination.Length)
        {
            var chunk = Math.Min(Math.Max(this.IoChunkSize, 1), destination.Length - total);
            var read = stream.Read(destination.Slice(total, chunk));

            if (read == 0)
                throw new EndOfStreamException();
            total += read;
        }

        return total;
    }
}

internal static class TestModes
{
    public static IEnumerable<TestMode> All
    {
        get
        {
            yield return new("Memory_DefaultChunk", false, 64 * 1024);
            yield return new("Memory_Chunk1", false, 1);
            yield return new("Mirrored_DefaultChunk", true, 64 * 1024);
            yield return new("Mirrored_Chunk1", true, 1);
        }
    }
}