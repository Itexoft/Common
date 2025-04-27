// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.InteropServices;

namespace Itexoft.IO.VFS;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct FileEntry
{
    /// <summary>
    /// Gets or sets the block index where the file entry resides.
    /// </summary>
    public long BlockIndex;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileEntry" /> struct.
    /// </summary>
    /// <param name="blockIndex">The block index associated with the entry.</param>
    public FileEntry(long blockIndex) => this.BlockIndex = blockIndex;

    /// <summary>
    /// Gets the serialized size of the structure in bytes.
    /// </summary>
    public static int Size => Marshal.SizeOf<FileEntry>();

    /// <summary>
    /// Deserializes a <see cref="FileEntry" /> from the specified buffer.
    /// </summary>
    /// <param name="bytes">Buffer containing the serialized entry.</param>
    /// <param name="offset">Offset within the buffer.</param>
    /// <returns>The reconstructed file entry.</returns>
    public static FileEntry FromBytes(byte[] bytes, int offset)
    {
        var blockIndex = BitConverter.ToInt64(bytes, offset);

        return new(blockIndex);
    }

    /// <summary>
    /// Serializes the entry into the provided buffer.
    /// </summary>
    /// <param name="bytes">Destination buffer.</param>
    /// <param name="offset">Offset within the buffer.</param>
    public void ToBytes(byte[] bytes, int offset)
    {
        var blockIndexBytes = BitConverter.GetBytes(this.BlockIndex);
        Array.Copy(blockIndexBytes, 0, bytes, offset, blockIndexBytes.Length);
    }

    /// <summary>
    /// Implicitly converts a block index into a <see cref="FileEntry" />.
    /// </summary>
    /// <param name="value">Block index value.</param>
    public static implicit operator FileEntry(long value) => new(value);

    /// <summary>
    /// Implicitly converts a <see cref="FileEntry" /> into its block index.
    /// </summary>
    /// <param name="value">File entry value.</param>
    public static implicit operator long(FileEntry value) => value.BlockIndex;
}