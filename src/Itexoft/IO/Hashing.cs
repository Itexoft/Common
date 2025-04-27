// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.IO;

public class Hashing
{
    private const uint polynomial = 0xEDB88320u;
    private static readonly uint[] table = CreateTable();

    public static uint HashToUInt32(ReadOnlySpan<byte> source)
    {
        var crc = uint.MaxValue;

        foreach (var b in source)
            crc = table[(crc ^ b) & 0xFF] ^ (crc >> 8);

        return ~crc;
    }

    public static uint HashToUInt32(byte[] source) => HashToUInt32(source.AsSpan());

    private static uint[] CreateTable()
    {
        var result = new uint[256];

        for (var i = 0; i < result.Length; i++)
        {
            var crc = (uint)i;

            for (var j = 0; j < 8; j++)
                if ((crc & 1) != 0)
                    crc = polynomial ^ (crc >> 1);
                else
                    crc >>= 1;

            result[i] = crc;
        }

        return result;
    }
}