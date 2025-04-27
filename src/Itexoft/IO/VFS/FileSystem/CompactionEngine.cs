// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Itexoft.Core;
using Itexoft.IO.VFS.Allocation;
using Itexoft.IO.VFS.Core;
using Itexoft.IO.VFS.Locking;
using Itexoft.IO.VFS.Metadata;
using Itexoft.IO.VFS.Metadata.Models;
using Itexoft.IO.VFS.Storage;

namespace Itexoft.IO.VFS.FileSystem;

internal sealed class CompactionEngine : IDisposable
{
    private const int maxBatchSize = 64;
    private readonly ExtentAllocator allocator;
    private readonly AlignedBufferPool bufferPool;
    private readonly CancellationTokenSource cts = new();
    private readonly DirectoryIndex directoryIndex;
    private readonly FileTable fileTable;
    private readonly LockManager lockManager;
    private readonly MetadataPersistence metadataPersistence;
    private readonly int pageSize;
    private readonly ConcurrentQueue<FileId> queue = new();
    private readonly StorageEngine storage;
    private readonly Thread worker;
    private Disposed disposed;

    public CompactionEngine(
        StorageEngine storage,
        ExtentAllocator allocator,
        FileTable fileTable,
        DirectoryIndex directoryIndex,
        MetadataPersistence metadataPersistence,
        LockManager lockManager,
        int pageSize)
    {
        this.storage = storage;
        this.allocator = allocator;
        this.fileTable = fileTable;
        this.directoryIndex = directoryIndex;
        this.metadataPersistence = metadataPersistence;
        this.lockManager = lockManager;
        this.pageSize = pageSize;
        this.bufferPool = new(pageSize, Environment.ProcessorCount * 2);

        this.worker = new(this.WorkerLoop)
        {
            IsBackground = true,
            Name = "VirtualFS.Compactor",
        };

        this.worker.Start();
    }

    public void Dispose()
    {
        if (this.disposed.Enter())
            return;

        this.cts.Cancel();
        this.queue.Enqueue(FileId.Invalid);
        this.worker.Join();
        this.bufferPool.Dispose();
        this.cts.Dispose();
    }

    public void NotifyFileChanged(FileId fileId) => this.queue.Enqueue(fileId);

    public void TriggerFullScan() => this.queue.Enqueue(FileId.Invalid);

    public void RunOnce() => this.ProcessBatch(fullScan: false);

    private void WorkerLoop()
    {
        while (!this.cts.IsCancellationRequested)
        {
            try
            {
                if (!this.queue.IsEmpty)
                {
                    this.ProcessBatch(fullScan: false);

                    continue;
                }

                Thread.Sleep(200);
            }
            catch
            {
                // ignore background errors; requests will be retried
            }
        }
    }

    private void ProcessBatch(bool fullScan)
    {
        if (this.disposed)
            return;

        var batch = new List<FileId>(maxBatchSize);

        if (fullScan)
            this.AddAllFiles(batch);
        else
        {
            while (batch.Count < maxBatchSize && this.queue.TryDequeue(out var fileId))
            {
                if (fileId == FileId.Invalid)
                {
                    fullScan = true;
                    batch.Clear();

                    break;
                }

                if (!batch.Contains(fileId))
                    batch.Add(fileId);
            }

            if (fullScan)
                this.AddAllFiles(batch);
        }

        if (batch.Count == 0)
            return;

        var changedFiles = new List<FileId>();

        foreach (var fileId in batch)
        {
            if (!this.fileTable.TryGet(fileId, out var metadata))
                continue;

            if (metadata.Extents.Length <= 1 || metadata.Length == 0)
                continue;

            try
            {
                if (this.CompactFile(fileId, metadata))
                    changedFiles.Add(fileId);
            }
            catch
            {
                // swallow and continue
            }
        }

        if (changedFiles.Count > 0)
        {
            try
            {
                this.metadataPersistence.Flush();
            }
            catch
            {
                // flush will retry later
            }
        }
    }

    private void AddAllFiles(List<FileId> batch)
    {
        try
        {
            foreach (var kvp in this.fileTable.Enumerate())
            {
                if (!batch.Contains(kvp.Key))
                    batch.Add(kvp.Key);

                if (batch.Count >= maxBatchSize)
                    break;
            }
        }
        catch
        {
            // ignore concurrent modifications
        }
    }

    private bool CompactFile(FileId fileId, FileMetadata initialMetadata)
    {
        using var handle = this.lockManager.AcquireExclusive(fileId);

        var metadata = this.fileTable.Get(fileId);

        if (metadata.Extents.Length <= 1 || metadata.Length == 0)
            return false;

        if (!this.directoryIndex.TryFindByTarget(fileId, out var parentId, out var entry))
            return false;

        var totalPages = (int)((metadata.Length + this.pageSize - 1) / this.pageSize);

        if (totalPages <= 0)
            return false;

        using var reservation = this.allocator.Reserve(totalPages, ExtentAllocator.AllocationOwner.FileData);
        var span = reservation.Span;

        using var alignedBuffer = this.bufferPool.Lease();
        var buffer = alignedBuffer.Span;

        var remaining = metadata.Length;
        var newPageIndex = 0;

        foreach (var oldExtent in metadata.Extents)
        {
            for (var offset = 0; offset < oldExtent.Length && remaining > 0; offset++)
            {
                var pageId = new PageId(oldExtent.Start.Value + offset);
                this.storage.ReadPage(pageId, buffer);
                var bytesToWrite = (int)Math.Min(this.pageSize, remaining);

                if (bytesToWrite < this.pageSize)
                    ZeroSpan(buffer[bytesToWrite..]);

                this.storage.WritePage(new(span.Start.Value + newPageIndex), buffer);
                newPageIndex++;
                remaining -= bytesToWrite;
            }
        }

        reservation.Commit();
        var newExtent = new PageSpan(span.Start, span.Length);

        foreach (var oldExtent in metadata.Extents)
            this.allocator.Free(oldExtent, ExtentAllocator.AllocationOwner.FileData);

        var updatedMetadata = this.fileTable.Update(
            fileId,
            current => current with
            {
                Extents = ImmutableArray<PageSpan>.Empty.Add(newExtent),
                ModifiedUtc = DateTime.UtcNow,
                AccessedUtc = DateTime.UtcNow,
            });

        var updatedEntry = entry with
        {
            ModifiedUtc = updatedMetadata.ModifiedUtc,
            AccessedUtc = updatedMetadata.AccessedUtc,
            Generation = updatedMetadata.Generation,
        };

        this.directoryIndex.Upsert(parentId, entry.Name, updatedEntry);

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ZeroSpan(Span<byte> span) => Unsafe.InitBlockUnaligned(ref MemoryMarshal.GetReference(span), 0, (uint)span.Length);

    private sealed class AlignedBufferPool(int bufferSize, int capacity) : IDisposable
    {
        private readonly ConcurrentBag<BufferHolder> buffers = [];
        private readonly int capacity = Math.Max(capacity, 1);
        private int created;

        public void Dispose()
        {
            while (this.buffers.TryTake(out var holder))
            {
                holder.Dispose();
                Interlocked.Decrement(ref this.created);
            }
        }

        public AlignedBufferLease Lease()
        {
            if (!this.buffers.TryTake(out var holder))
            {
                if (Interlocked.Increment(ref this.created) <= this.capacity)
                    holder = new(bufferSize);
                else
                {
                    Interlocked.Decrement(ref this.created);
                    holder = new(bufferSize);
                }
            }

            return new(this, holder);
        }

        private void Return(BufferHolder holder) => this.buffers.Add(holder);

        internal unsafe sealed class BufferHolder : IDisposable
        {
            private readonly int size;
            private byte* pointer;

            public BufferHolder(int size)
            {
                this.size = size;
                this.pointer = (byte*)NativeMemory.AlignedAlloc((nuint)size, (nuint)4096);

                if (this.pointer == null)
                    throw new OutOfMemoryException();
            }

            public Span<byte> Span
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get
                {
                    if (this.pointer is null)
                        throw new ObjectDisposedException(nameof(BufferHolder));

                    return new(this.pointer, this.size);
                }
            }

            public void Dispose()
            {
                if (this.pointer is not null)
                {
                    NativeMemory.AlignedFree(this.pointer);
                    this.pointer = null;
                }
            }
        }

        internal sealed class AlignedBufferLease : IDisposable
        {
            private readonly AlignedBufferPool owner;
            private BufferHolder? holder;

            internal AlignedBufferLease(AlignedBufferPool owner, BufferHolder holder)
            {
                this.owner = owner;
                this.holder = holder;
            }

            public Span<byte> Span
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get
                {
                    if (this.holder is null)
                        throw new ObjectDisposedException(nameof(AlignedBufferLease));

                    return this.holder.Span;
                }
            }

            public void Dispose()
            {
                var current = Interlocked.Exchange(ref this.holder, null);

                if (current is not null)
                    this.owner.Return(current);
            }
        }
    }
}
