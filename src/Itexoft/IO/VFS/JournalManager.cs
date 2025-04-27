// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Buffers;
using System.Runtime.CompilerServices;

namespace Itexoft.IO.VFS;

/// <summary>
/// Maintains an append-only allocation journal that enables recovery after abrupt termination.
/// </summary>
internal sealed class JournalManager : IDisposable
{
    private const int PrimaryMetadataStart = 0;
    private const int FlagBytePosition = sizeof(long);
    private const int SecondaryMetadataStart = FlagBytePosition + 1;

    private const byte FlagPrimaryActive = 0x00;
    private const byte FlagSecondaryActive = 0x01;

    // Each region holds: [long TxId] + [int RecordCount] + the records themselves
    // Record format: each record is 1 byte for 'RecordType', 8 bytes for 'BlockIndex'
    // You can extend this format if needed for more operations.

    private const int TxIdSize = sizeof(long);
    private const int RecordCountSize = sizeof(int);
    private const int RecordHeaderSize = 1 + sizeof(long); // recordType (1 byte) + blockIndex (8 bytes)

    private const byte RecordType_Allocate = 0x10;
    private const byte RecordType_Release = 0x20;

    private const int FlagPosition = 0; // Single byte for active region flag
    private const int RegionSize = 4096; // Each region gets 4KB of space
    private const int Region1Offset = 1; // Primary region starts right after FlagPosition
    private const int Region2Offset = Region1Offset + RegionSize; // Secondary region follows

    private readonly Stream baseStream;
    private readonly List<(byte recordType, long blockIndex)> pendingRecords = [];
    private long currentTxId;
    private bool isDisposed;
    private bool isInTransaction;

    /// <summary>
    /// Initializes a new instance of the <see cref="JournalManager" /> class.
    /// </summary>
    /// <param name="journalStream">Stream used to persist journal entries.</param>
    public JournalManager(Stream journalStream)
    {
        if (journalStream == null)
            throw new ArgumentNullException(nameof(journalStream));
        if (!journalStream.CanRead || !journalStream.CanWrite || !journalStream.CanSeek)
            throw new ArgumentException("Stream must support read, write, and seek.", nameof(journalStream));

        this.baseStream = journalStream;
        if (this.baseStream.Length < GetMaxRegionSize() * 2)
            this.InitializeJournal();

        this.Recover();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (this.isDisposed)
            return;
        if (this.isInTransaction)
            this.RollbackTransaction();

        this.isDisposed = true;
    }

    /// <summary>
    /// Starts a new journal transaction and returns its identifier.
    /// </summary>
    /// <returns>The transaction identifier.</returns>
    public long BeginTransaction()
    {
        if (this.isInTransaction)
            throw new InvalidOperationException("Cannot begin a new transaction before committing or rolling back the current one.");

        this.currentTxId = GetNextTxId(this.ReadActiveRegionData().TxId);
        this.isInTransaction = true;
        this.pendingRecords.Clear();

        return this.currentTxId;
    }

    /// <summary>
    /// Records an allocation of a block within the current transaction.
    /// </summary>
    /// <param name="blockIndex">Allocated block index.</param>
    public void RecordAllocateBlock(long blockIndex)
    {
        if (!this.isInTransaction)
            throw new InvalidOperationException("No active transaction to record allocation.");

        this.pendingRecords.Add((RecordType_Allocate, blockIndex));
    }

    /// <summary>
    /// Records a block release within the current transaction.
    /// </summary>
    /// <param name="blockIndex">Released block index.</param>
    public void RecordReleaseBlock(long blockIndex)
    {
        if (!this.isInTransaction)
            throw new InvalidOperationException("No active transaction to record release.");

        this.pendingRecords.Add((RecordType_Release, blockIndex));
    }

    /// <summary>
    /// Commits the current transaction and persists recorded operations.
    /// </summary>
    public void CommitTransaction()
    {
        if (!this.isInTransaction)
            throw new InvalidOperationException("No active transaction to commit.");

        var data = this.ReadActiveRegionData();
        var newTxId = this.currentTxId > data.TxId ? this.currentTxId : data.TxId;

        var updatedTxId = newTxId;
        var recordCount = data.RecordCount;

        // Append new records
        var finalRecords = new List<(byte, long)>();

        //if (recordCount > 0)
        //{
        //    finalRecords.AddRange(data.Records);
        //}
        finalRecords.AddRange(this.pendingRecords);

        this.WriteToInactiveRegion(updatedTxId, finalRecords);
        this.SwitchActiveRegion();
        this.pendingRecords.Clear();
        this.isInTransaction = false;
    }

    /// <summary>
    /// Rolls back the current transaction, discarding pending records.
    /// </summary>
    public void RollbackTransaction()
    {
        if (!this.isInTransaction)
            throw new InvalidOperationException("No active transaction to rollback.");

        this.pendingRecords.Clear();
        this.isInTransaction = false;
    }

    /// <summary>
    /// Returns all journal records found in the active region.
    /// </summary>
    /// <returns>A tuple containing the transaction id and the record set.</returns>
    public (long TxId, List<(byte recordType, long blockIndex)> Records) GetAllRecords()
    {
        // Returns everything from the active region (for debugging or scanning).
        var data = this.ReadActiveRegionData();

        return (data.TxId, data.Records);
    }

    // --------------------------------------------------
    // Internal logic: double-copy, flipping active byte
    // --------------------------------------------------


    private void InitializeJournal()
    {
        // Ensure capacity for flag + two full regions
        this.baseStream.SetLength(Region2Offset + RegionSize);

        // Zero everything out
        Span<byte> zeroBytes = new byte[Region2Offset + RegionSize];
        this.baseStream.Seek(0, SeekOrigin.Begin);
        this.baseStream.Write(zeroBytes);
        this.baseStream.Flush();

        // Initialize both regions with TxId=0 and no records
        this.WriteRegion(0, 0, [], Region1Offset);
        this.WriteRegion(0, 0, [], Region2Offset);

        // Mark the primary region as active
        this.WriteActiveFlag(FlagPrimaryActive);
    }

    private void Recover()
    {
        // We'll read active region. If the last Tx was incomplete, we can interpret records if needed.
        // If something was half-written in the inactive region, we ignore it.
        // "Double reservation" approach ensures the active region is always consistent.
        // If you need more sophisticated logic (like partial commit), extend it here.
        var data = this.ReadActiveRegionData();
        if (data.RecordCount < 0) // corrupted
            this.InitializeJournal();
    }

    private void WriteToInactiveRegion(long txId, List<(byte recordType, long blockIndex)> records)
    {
        var activeFlag = this.ReadActiveFlag();

        // If primary is active, we write to the secondary region, and vice versa
        var regionOffset = activeFlag == FlagPrimaryActive ? Region2Offset : Region1Offset;
        this.WriteRegion(txId, records.Count, records, regionOffset);
    }

    private void SwitchActiveRegion()
    {
        var flag = this.ReadActiveFlag();
        if (flag == FlagPrimaryActive)
            this.WriteActiveFlag(FlagSecondaryActive);
        else
            this.WriteActiveFlag(FlagPrimaryActive);
    }

    private (long TxId, int RecordCount, List<(byte, long)> Records) ReadActiveRegionData()
    {
        var activeFlag = this.ReadActiveFlag();
        var regionOffset = activeFlag == FlagPrimaryActive ? Region1Offset : Region2Offset;

        return this.ReadRegion(regionOffset);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private byte ReadActiveFlag()
    {
        this.baseStream.Seek(FlagPosition, SeekOrigin.Begin);
        var b = this.baseStream.ReadByte();

        return b == -1 ? FlagPrimaryActive : (byte)b;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteActiveFlag(byte flag)
    {
        this.baseStream.Seek(FlagPosition, SeekOrigin.Begin);
        this.baseStream.WriteByte(flag);
        this.baseStream.Flush();
    }

    private (long TxId, int RecordCount, List<(byte, long)> Records) ReadRegion(long regionOffset)
    {
        this.baseStream.Seek(regionOffset, SeekOrigin.Begin);
        Span<byte> header = stackalloc byte[TxIdSize + RecordCountSize];
        var bytesRead = this.baseStream.Read(header);

        if (bytesRead < header.Length)
            return (0, -1, []);

        var txId = BitConverter.ToInt64(header.Slice(0, TxIdSize));
        var recordCount = BitConverter.ToInt32(header.Slice(TxIdSize, RecordCountSize));

        if (recordCount < 0)
            return (txId, recordCount, []);

        var records = new List<(byte, long)>(recordCount);
        var recordBuffer = ArrayPool<byte>.Shared.Rent(RecordHeaderSize);
        try
        {
            for (var i = 0; i < recordCount; i++)
            {
                var r = this.baseStream.Read(recordBuffer, 0, RecordHeaderSize);

                if (r < RecordHeaderSize)
                    break;

                var rt = recordBuffer[0];
                var bIndex = BitConverter.ToInt64(recordBuffer, 1);
                records.Add((rt, bIndex));
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(recordBuffer);
        }

        return (txId, recordCount, records);
    }

    private void WriteRegion(long txId, int recordCount, List<(byte, long)> records, long regionOffset = 0)
    {
        this.baseStream.Seek(regionOffset, SeekOrigin.Begin);

        Span<byte> header = stackalloc byte[TxIdSize + RecordCountSize];
        BitConverter.TryWriteBytes(header, txId);
        BitConverter.TryWriteBytes(header.Slice(TxIdSize), recordCount);
        this.baseStream.Write(header);

        var recordBuffer = ArrayPool<byte>.Shared.Rent(RecordHeaderSize);
        try
        {
            foreach (var (rt, bIndex) in records)
            {
                recordBuffer[0] = rt;
                BitConverter.TryWriteBytes(recordBuffer.AsSpan(1), bIndex);
                this.baseStream.Write(recordBuffer, 0, RecordHeaderSize);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(recordBuffer);
        }

        this.baseStream.Flush();
    }

    private static long GetMaxRegionSize() =>

        // Adjust if you need bigger capacity for the journal.
        // For example, 4 KB for each region:
        4096;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long GetNextTxId(long currentTxId) => currentTxId + 1;
}