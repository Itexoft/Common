// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.IO.VFS;

/// <summary>
/// Defines methods for allocating, freeing, reading, and writing blocks of data.
/// </summary>
internal interface IBlockManager
{
    /// <summary>
    /// Gets block size.
    /// </summary>
    int BlockSize { get; }

    /// <summary>
    /// Gets total blocks.
    /// </summary>
    long TotalBlocks { get; }

    /// <summary>
    /// Gets allocated blocks.
    /// </summary>
    long AllocatedBlocks { get; }

    /// <summary>
    /// Allocates a new block and returns its index.
    /// </summary>
    /// <returns>The index of the allocated block.</returns>
    long AllocateBlock();

    /// <summary>
    /// Frees the specified block.
    /// </summary>
    /// <param name="blockIndex">The index of the block to free.</param>
    void FreeBlock(long blockIndex);

    /// <summary>
    /// Writes data to the specified block.
    /// </summary>
    /// <param name="blockIndex">The index of the block to write to.</param>
    /// <param name="positionOffset">The byte offset in the block at which to begin writing data.</param>
    /// <param name="data">The data to write.</param>
    /// <param name="offset">The zero-based byte offset in <paramref name="data" /> at which to begin copying bytes to the block.</param>
    /// <param name="count">The number of bytes to write.</param>
    void WriteBlock(long blockIndex, int positionOffset, byte[] data, int offset, int count);

    /// <summary>
    /// Reads data from the specified block.
    /// </summary>
    /// <param name="blockIndex">The index of the block to read from.</param>
    /// <param name="positionOffset">The byte offset in the block at which to begin reading data.</param>
    /// <param name="buffer">The buffer to store the read data.</param>
    /// <param name="offset">The zero-based byte offset in <paramref name="buffer" /> at which to begin storing the data read from the block.</param>
    /// <param name="count">The number of bytes to read.</param>
    void ReadBlock(long blockIndex, int positionOffset, byte[] buffer, int offset, int count);

    /// <summary>
    /// Acquires a disposable synchronization scope that guards access to the block manager.
    /// </summary>
    /// <returns>A disposable lock handle.</returns>
    IDisposable Lock();
}