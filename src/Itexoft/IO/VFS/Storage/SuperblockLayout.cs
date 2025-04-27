// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace Itexoft.IO.VFS.Storage;

internal static class SuperblockLayout
{
    private const ulong magic = 0x56494F4653000001UL; // ASCII: V I O F S \0 \0 \x01
    internal const int headerLength = 40;

    internal static bool TryParse(ReadOnlySpan<byte> page, out SuperblockState state)
    {
        state = default;

        if (page.Length < headerLength)
            return false;

        var magic = BinaryPrimitives.ReadUInt64LittleEndian(page);

        if (magic != SuperblockLayout.magic)
            return false;

        var version = BinaryPrimitives.ReadUInt32LittleEndian(page[8..]);

        if (version != 1)
            return false;

        var pageSize = BinaryPrimitives.ReadInt32LittleEndian(page[12..]);
        var generation = BinaryPrimitives.ReadInt64LittleEndian(page[16..]);
        var slot = page[24];
        var headerChecksum = BinaryPrimitives.ReadUInt32LittleEndian(page[28..]);
        var payloadChecksum = BinaryPrimitives.ReadUInt32LittleEndian(page[32..]);

        Span<byte> headerCopy = stackalloc byte[headerLength];
        page[..headerLength].CopyTo(headerCopy);
        headerCopy[28] = 0;
        headerCopy[29] = 0;
        headerCopy[30] = 0;
        headerCopy[31] = 0;

        if (ComputeChecksum(headerCopy) != headerChecksum)
            return false;

        if (ComputeChecksum(page[headerLength..]) != payloadChecksum)
            return false;

        state = new(pageSize, generation, slot);

        return true;
    }

    internal static void Write(Span<byte> destination, in SuperblockState state, ReadOnlySpan<byte> payload)
    {
        if (destination.Length < headerLength)
            throw new ArgumentException("Destination span too small for header.", nameof(destination));

        if (payload.Length > destination.Length - headerLength)
            throw new ArgumentException("Payload exceeds available superblock space.", nameof(payload));

        payload.CopyTo(destination[headerLength..]);

        if (payload.Length < destination.Length - headerLength)
            destination[(headerLength + payload.Length)..].Clear();

        BinaryPrimitives.WriteUInt64LittleEndian(destination, magic);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[8..], 1);
        BinaryPrimitives.WriteInt32LittleEndian(destination[12..], state.PageSize);
        BinaryPrimitives.WriteInt64LittleEndian(destination[16..], state.Generation);
        destination[24] = state.ActiveSlot;
        BinaryPrimitives.WriteUInt32LittleEndian(destination[32..], ComputeChecksum(destination[headerLength..]));

        destination[28] = 0;
        destination[29] = 0;
        destination[30] = 0;
        destination[31] = 0;
        BinaryPrimitives.WriteUInt32LittleEndian(destination[28..], ComputeChecksum(destination[..headerLength]));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint ComputeChecksum(ReadOnlySpan<byte> data)
    {
        uint sum1 = 0xffff, sum2 = 0xffff;
        var index = 0;

        while (index < data.Length)
        {
            var chunk = Math.Min(data.Length - index, 360);

            for (var i = 0; i < chunk; i++)
            {
                sum1 = (sum1 + data[index++]) % 65521;
                sum2 = (sum2 + sum1) % 65521;
            }
        }

        return (sum2 << 16) | sum1;
    }

    internal readonly record struct SuperblockState(int PageSize, long Generation, byte ActiveSlot);
}
