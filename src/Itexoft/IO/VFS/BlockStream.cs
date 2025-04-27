// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.CompilerServices;

namespace Itexoft.IO.VFS;

/// <summary>
/// Represents a stream that manages its data using a chain of blocks managed by an <see cref="IBlockManager" />.
/// Each <see cref="BlockStream" /> instance operates independently, maintaining its own chain of blocks.
/// </summary>
internal sealed class BlockStream : Stream
{
    #region Constants

    private const int LongSize = sizeof(long);
    private const int MetadataParts = 2;
    private const int MetadataPartLength = LongSize * MetadataParts;
    private const int PrimaryMetadataStart = 0;
    private const int FlagBytePosition = MetadataPartLength;
    private const int SecondaryMetadataStart = FlagBytePosition + 1;
    private const byte FlagMask = 0x01;
    internal const int MetadataLength = SecondaryMetadataStart + MetadataPartLength;

    #endregion

    #region Fields

    private readonly IBlockManager blockManager;
    private readonly int blockSize;
    private long startBlockIndex;
    private long position;
    private long length;
    private bool isDisposed;
    private readonly byte[] metadataBuffer = new byte[MetadataLength];

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="BlockStream" /> class for creating a new stream.
    /// </summary>
    /// <param name="blockManager">The block manager to use.</param>
    public BlockStream(IBlockManager blockManager)
        : this(blockManager, -1, false) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="BlockStream" /> class for an existing stream.
    /// </summary>
    /// <param name="blockManager">The block manager to use.</param>
    /// <param name="startBlockIndex">The starting block index of the existing stream.</param>
    public BlockStream(IBlockManager blockManager, long startBlockIndex)
        : this(blockManager, startBlockIndex, true) { }

    /// <summary>
    /// Private constructor to handle common initialization.
    /// </summary>
    /// <param name="blockManager">The block manager to use.</param>
    /// <param name="startBlockIndex">The starting block index.</param>
    /// <param name="hasStartBlock">Indicates whether the stream has an existing start block.</param>
    private BlockStream(IBlockManager blockManager, long startBlockIndex, bool hasStartBlock)
    {
        this.blockManager = new BufferedBlockManager(blockManager);
        this.blockSize = this.blockManager.BlockSize;
        this.startBlockIndex = hasStartBlock ? startBlockIndex : -1;
        this.position = 0;
        this.length = hasStartBlock ? this.CalculateStreamLength(startBlockIndex) : 0;
    }

    #endregion

    #region Properties

    /// <summary>
    /// Gets the starting block index of the stream.
    /// </summary>
    public long StartBlockIndex => this.startBlockIndex;

    /// <inheritdoc />
    public override bool CanRead => true;

    /// <inheritdoc />
    public override bool CanSeek => true;

    /// <inheritdoc />
    public override bool CanWrite => true;

    /// <inheritdoc />
    public override long Length
    {
        get
        {
            this.ThrowIfIsDisposed();

            return this.length;
        }
    }

    /// <inheritdoc />
    public override long Position
    {
        get
        {
            this.ThrowIfIsDisposed();

            return this.position;
        }
        set => this.Seek(value, SeekOrigin.Begin);
    }

    #endregion

    #region Overridden Methods

    /// <inheritdoc />
    public override void Flush() => this.ThrowIfIsDisposed();

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count)
    {
        this.ValidateBufferParameters(buffer, offset, count);
        this.ThrowIfIsDisposed();

        if (count == 0 || this.position >= this.length)
            return 0;

        var totalBytesRead = 0;
        var currentBlock = -1L;
        var blockOffset = 0;

        while (count > 0 && this.position < this.length)
            using (this.blockManager.Lock())
            {
                if (currentBlock < 0)

                    // Get block index and block offset at the current position
                    (currentBlock, blockOffset) = this.GetBlockAtPosition(this.position);

                if (currentBlock == -1)

                    // If no block is available, stop the reading loop
                    break;

                // Determine the number of bytes available to read in the current block
                var bytesAvailableInBlock = Math.Min(this.blockSize - MetadataLength - blockOffset, this.length - this.position);
                var bytesToRead = (int)Math.Min(count, bytesAvailableInBlock);

                // Read data from the current block
                this.blockManager.ReadBlock(currentBlock, MetadataLength + blockOffset, buffer, offset, bytesToRead);

                // Update position and counters
                this.position += bytesToRead;
                offset += bytesToRead;
                count -= bytesToRead;
                totalBytesRead += bytesToRead;

                blockOffset += bytesToRead;

                // Move to the next block if needed
                if (blockOffset >= this.blockSize - MetadataLength)
                {
                    var nextBlock = this.GetNextBlockIndex(currentBlock);

                    if (nextBlock <= 0)

                        // If there is no next block, stop reading
                        break;
                    currentBlock = nextBlock;
                    blockOffset = 0;
                }
                else
                {
                    currentBlock = -1;
                }
            }

        return totalBytesRead;
    }

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count)
    {
        this.ValidateBufferParameters(buffer, offset, count);
        this.ThrowIfIsDisposed();

        var currentBlock = -1L;
        var blockOffset = 0;

        while (count > 0)
            using (this.blockManager.Lock())
            {
                if (currentBlock < 0)
                    (currentBlock, blockOffset) = this.GetBlockAtPosition(this.position, true);

                if (currentBlock == -1)
                    throw new InvalidOperationException("Failed to allocate blocks to reach desired position.");

                var bytesAvailableInBlock = this.blockSize - MetadataLength - blockOffset;
                var bytesToWrite = Math.Min(count, bytesAvailableInBlock);

                // Write data to the current block
                this.blockManager.WriteBlock(currentBlock, MetadataLength + blockOffset, buffer, offset, bytesToWrite);

                // Update position and counters
                this.position += bytesToWrite;
                offset += bytesToWrite;
                count -= bytesToWrite;

                // Update the stream length if necessary
                if (this.position > this.length)
                {
                    this.length = this.position;
                    this.UpdateStartBlockLength(); // Updates metadata in start block
                }

                blockOffset += bytesToWrite;

                // Move to the next block if needed
                if (blockOffset >= this.blockSize - MetadataLength && count > 0)
                {
                    var nextBlock = this.GetNextBlockIndex(currentBlock);
                    if (nextBlock <= 0)
                    {
                        // Allocate a new block
                        var newBlockIndex = this.blockManager.AllocateBlock();

                        // Update the current block's next block index to point to the new block
                        this.WriteMetadata(currentBlock, null, newBlockIndex);

                        // Initialize metadata for the new block
                        this.WriteMetadata(newBlockIndex, currentBlock, 0);

                        nextBlock = newBlockIndex;
                    }

                    currentBlock = nextBlock;
                    blockOffset = 0;
                }
            }
    }

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin)
    {
        this.ThrowIfIsDisposed();
        var newPosition = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => this.position + offset,
            SeekOrigin.End => this.length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin), "Invalid SeekOrigin.")
        };

        if (newPosition < 0)
            throw new IOException("Cannot seek to a negative position.");
        this.position = newPosition;

        return this.position;
    }

    /// <inheritdoc />
    public override void SetLength(long value)
    {
        this.ThrowIfIsDisposed();

        if (value < 0)
            throw new ArgumentOutOfRangeException(nameof(value), "Length cannot be negative.");

        using (this.blockManager.Lock())
        {
            if (value < this.length)
            {
                // Truncate the stream to the new length
                this.TrimBlocks((value + this.blockSize - MetadataLength - 1) / (this.blockSize - MetadataLength));
                this.length = value;
                if (this.position > this.length)
                    this.position = this.length;
                this.UpdateStartBlockLength();
            }
            else if (value > this.length)
            {
                // Extend the stream's length
                this.length = value;
                this.UpdateStartBlockLength();

                // Allocate necessary blocks to cover the new length
                this.AllocateBlocks((value + this.blockSize - MetadataLength - 1) / (this.blockSize - MetadataLength));
            }
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (this.isDisposed)
            return;

        if (disposing)
            this.Flush();

        base.Dispose(disposing);
        this.isDisposed = true;
    }

    #endregion

    #region Private Methods

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ThrowIfIsDisposed()
    {
        if (this.isDisposed)
            throw new ObjectDisposedException(this.GetType().FullName);
    }

    private (long blockIndex, int blockOffset) GetBlockAtPosition(long position, bool forWrite = false)
    {
        if (this.startBlockIndex == -1)
        {
            if (forWrite)
            {
                this.startBlockIndex = this.blockManager.AllocateBlock();
                this.InitializeBlockMetadata(this.startBlockIndex);

                return (this.startBlockIndex, 0);
            }

            return (-1, 0);
        }

        var currentPosition = 0L;
        var currentBlock = this.startBlockIndex;

        while (true)
        {
            if (position < currentPosition + (this.blockSize - MetadataLength))
                return (currentBlock, (int)(position - currentPosition));

            currentPosition += this.blockSize - MetadataLength;
            var (_, nextBlock) = this.ReadMetadata(currentBlock);
            nextBlock = nextBlock > 0 ? nextBlock : -1;

            if (nextBlock <= 0)
            {
                if (forWrite)
                {
                    var newBlock = this.blockManager.AllocateBlock();
                    this.InitializeBlockMetadata(newBlock);
                    this.WriteMetadata(currentBlock, null, newBlock);
                    this.WriteMetadata(newBlock, currentBlock, 0);
                    currentBlock = newBlock;
                }
                else
                {
                    return (-1, 0);
                }
            }
            else
            {
                currentBlock = nextBlock;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void UpdateStartBlockLength()
    {
        if (this.startBlockIndex != -1)
            this.WriteMetadata(this.startBlockIndex, -this.length, null);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteMetadata(long blockIndex, long? value1, long? value2)
    {
        this.blockManager.ReadBlock(blockIndex, 0, this.metadataBuffer, 0, MetadataLength);

        var isPrimaryActive = (this.metadataBuffer[FlagBytePosition] & FlagMask) == 0;
        var activeStart = isPrimaryActive ? PrimaryMetadataStart : SecondaryMetadataStart;
        var inactiveStart = isPrimaryActive ? SecondaryMetadataStart : PrimaryMetadataStart;

        if (value1.HasValue)
            Array.Copy(BitConverter.GetBytes(value1.Value), 0, this.metadataBuffer, activeStart, LongSize);

        if (value2.HasValue)
            Array.Copy(BitConverter.GetBytes(value2.Value), 0, this.metadataBuffer, activeStart + LongSize, LongSize);

        this.blockManager.WriteBlock(blockIndex, inactiveStart, this.metadataBuffer, activeStart, MetadataPartLength);

        this.metadataBuffer[FlagBytePosition] ^= FlagMask;
        this.blockManager.WriteBlock(blockIndex, FlagBytePosition, this.metadataBuffer, FlagBytePosition, 1);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private (long, long) ReadMetadata(long blockIndex)
    {
        this.blockManager.ReadBlock(blockIndex, 0, this.metadataBuffer, 0, MetadataLength);
        var isPrimaryActive = (this.metadataBuffer[FlagBytePosition] & FlagMask) == 0;
        var activeStart = isPrimaryActive ? PrimaryMetadataStart : SecondaryMetadataStart;

        return (BitConverter.ToInt64(this.metadataBuffer, activeStart), BitConverter.ToInt64(this.metadataBuffer, activeStart + LongSize));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void InitializeBlockMetadata(long blockIndex)
    {
        Array.Clear(this.metadataBuffer, 0, MetadataLength);
        this.blockManager.WriteBlock(blockIndex, 0, this.metadataBuffer, 0, MetadataLength);
    }

    private void TrimBlocks(long blocksNeeded)
    {
        if (this.startBlockIndex < 0)
            return;

        using (this.blockManager.Lock())
        {
            var currentBlockIndex = this.startBlockIndex;
            var totalBlockCount = 0L;
            long lastBlockIndex = 0;

            do
            {
                lastBlockIndex = currentBlockIndex;
                var (_, nextBlockIndex) = this.ReadMetadata(currentBlockIndex);
                currentBlockIndex = nextBlockIndex;
                totalBlockCount++;
            }
            while (currentBlockIndex > 0);

            for (var i = totalBlockCount; i > blocksNeeded; i--)
            {
                var (prevBlockIndex, _) = this.ReadMetadata(lastBlockIndex);
                this.blockManager.FreeBlock(lastBlockIndex);
                lastBlockIndex = prevBlockIndex;
            }

            if (blocksNeeded == 0)
                this.startBlockIndex = -1;
            else

                // Ensure the tail block does not point to a freed block
                this.WriteMetadata(lastBlockIndex, null, 0);
        }
    }

    private void AllocateBlocks(long blocksNeeded)
    {
        if (this.startBlockIndex == -1)
        {
            this.startBlockIndex = this.blockManager.AllocateBlock();
            this.InitializeBlockMetadata(this.startBlockIndex);
        }

        var currentBlock = this.startBlockIndex;
        long currentBlockNumber = 1;

        while (currentBlock != -1 && currentBlockNumber < blocksNeeded)
        {
            var nextBlock = this.GetNextBlockIndex(currentBlock);

            if (nextBlock <= 0)
                break;
            currentBlock = nextBlock;
            currentBlockNumber++;
        }

        while (currentBlockNumber < blocksNeeded)
        {
            var newBlock = this.blockManager.AllocateBlock();
            this.InitializeBlockMetadata(newBlock);
            this.WriteMetadata(currentBlock, null, newBlock);
            this.WriteMetadata(newBlock, currentBlock, 0);
            currentBlock = newBlock;
            currentBlockNumber++;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private long GetNextBlockIndex(long currentBlock)
    {
        var (_, nextBlock) = this.ReadMetadata(currentBlock);

        return nextBlock > 0 ? nextBlock : -1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private long CalculateStreamLength(long startBlock)
    {
        if (startBlock == -1)
            return 0;
        using (this.blockManager.Lock())
        {
            var (primary, _) = this.ReadMetadata(startBlock);

            return Math.Abs(primary);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ValidateBufferParameters(byte[] buffer, int offset, int count)
    {
        if (buffer == null)
            throw new ArgumentNullException(nameof(buffer));
        if (offset < 0)
            throw new ArgumentOutOfRangeException(nameof(offset), "Offset cannot be negative.");
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count), "Count cannot be negative.");
        if (offset + count > buffer.Length)
            throw new ArgumentException("The sum of offset and count is larger than the buffer length.");
    }

    #endregion
}