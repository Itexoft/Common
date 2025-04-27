// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Itexoft.Core;
using Itexoft.Extensions;
using Itexoft.IO.VFS.Allocation;
using Itexoft.IO.VFS.Core;
using Itexoft.IO.VFS.Locking;
using Itexoft.IO.VFS.Metadata;
using Itexoft.IO.VFS.Metadata.Models;
using Itexoft.IO.VFS.Storage;

namespace Itexoft.IO.VFS.FileSystem;

/// <summary>
/// Stream implementation exposing file content stored within the virtual file system.
/// </summary>
internal sealed class VirtualFileStream : Stream
{
    private static readonly ConditionalWeakTable<VirtualFileStream, object> debugLocks = new();
    private readonly FileAccess access;
    private readonly ExtentAllocator allocator;
    private readonly CompactionEngine? compactionEngine;
    private readonly DirectoryIndex directoryIndex;
    private readonly FileId fileId;
    private readonly FileTable fileTable;
    private readonly LockManager.LockHandle lockHandle;
    private readonly MetadataPersistence metadataPersistence;
    private readonly string name;
    private readonly Action<VirtualFileStream>? onDispose;
    private readonly byte[] pageBuffer;
    private readonly List<PageId> pages;
    private readonly int pageSize;
    private readonly FileId parentId;
    private readonly HashSet<long> pendingPages = [];
    private readonly SortedSet<long> pendingReleasePages = [];
    private readonly Dictionary<long, byte[]> stagedPages = new();
    private readonly StorageEngine storage;
    private readonly Lock streamSync = new();
    private long capacityBytes;
    private Disposed disposed;
    private long length;
    private bool metadataDirty;
    private long position;

    internal VirtualFileStream(
        StorageEngine storage,
        ExtentAllocator allocator,
        FileTable fileTable,
        DirectoryIndex directoryIndex,
        MetadataPersistence metadataPersistence,
        CompactionEngine? compactionEngine,
        LockManager.LockHandle lockHandle,
        FileId fileId,
        FileId parentId,
        string name,
        FileAccess access,
        FileMode mode,
        FileMetadata metadata,
        Action<VirtualFileStream>? onDispose)
    {
        this.storage = storage;
        this.allocator = allocator;
        this.fileTable = fileTable;
        this.directoryIndex = directoryIndex;
        this.metadataPersistence = metadataPersistence;
        this.compactionEngine = compactionEngine;
        this.lockHandle = lockHandle;
        this.fileId = fileId;
        this.parentId = parentId;
        this.name = name;
        this.access = access;
        this.onDispose = onDispose;
        this.pageSize = storage.PageSize;

        this.pages = new(ExpandExtents(metadata.Extents));
        this.length = metadata.Length;
        this.capacityBytes = (long)this.pages.Count * this.pageSize;
        this.position = mode == FileMode.Append ? this.length : 0;

        this.pageBuffer = ArrayPool<byte>.Shared.Rent(this.pageSize);

        if (mode == FileMode.Truncate && this.CanWrite)
        {
            this.TruncateInternal(0);
            this.metadataDirty = true;
        }

        if (mode == FileMode.Create || mode == FileMode.CreateNew)
        {
            this.TruncateInternal(0);
            this.metadataDirty = true;
        }
    }

    /// <inheritdoc />
    public override bool CanRead => (this.access & FileAccess.Read) != 0;

    /// <inheritdoc />
    public override bool CanSeek => true;

    /// <inheritdoc />
    public override bool CanWrite => (this.access & FileAccess.Write) != 0;

    /// <inheritdoc />
    public override long Length => this.length;

    /// <inheritdoc />
    public override long Position
    {
        get => this.position;
        set
        {
            this.disposed.ThrowIf();

            ArgumentOutOfRangeException.ThrowIfNegative(value);
            this.position = value;
        }
    }

    /// <inheritdoc />
    public override void Flush()
    {
        lock (this.streamSync)
        {
            this.disposed.ThrowIf();

            if (!this.CanWrite || !this.metadataDirty)
                return;

            this.PersistMetadata();
        }
    }

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count)
    {
        lock (this.streamSync)
        {
            this.disposed.ThrowIf();

            if (!this.CanRead)
                throw new NotSupportedException("Stream is not readable.");

            ValidateBuffer(buffer, offset, count);

            if (this.position >= this.length || count == 0)
                return 0;

            var remaining = (int)Math.Min(count, this.length - this.position);
            var startOffset = offset;

            while (remaining > 0)
            {
                var locator = this.LocatePage(this.position);

                if (locator.pageIndex < 0)
                    break;

                var pageId = this.pages[locator.pageIndex];
                var offsetInPage = locator.offsetInPage;
                var bytesAvailable = Math.Min(this.pageSize - offsetInPage, remaining);

                var pageSpan = this.GetReadablePageSpan(pageId);
                var sourceSpan = pageSpan.Slice(offsetInPage, bytesAvailable);
                var destinationSpan = buffer.AsSpan(offset, bytesAvailable);
                CopySpan(sourceSpan, destinationSpan);
                offset += bytesAvailable;
                remaining -= bytesAvailable;
                this.position += bytesAvailable;
            }

            return offset - startOffset;
        }
    }

    /// <inheritdoc />
    public override int Read(Span<byte> buffer)
    {
        var rented = ArrayPool<byte>.Shared.Rent(buffer.Length);

        try
        {
            var read = this.Read(rented, 0, buffer.Length);
            rented.AsSpan(0, read).CopyTo(buffer);

            return read;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented, true);
        }
    }

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count)
    {
        lock (this.streamSync)
        {
            this.disposed.ThrowIf();

            if (!this.CanWrite)
                throw new NotSupportedException("Stream is not writable.");

            ValidateBuffer(buffer, offset, count);

            if (count == 0)
                return;

            var targetEnd = this.position + count;

            if (targetEnd > this.length)
            {
                this.EnsureCapacity(targetEnd);
                this.ZeroFill(this.length, this.position);
                this.length = targetEnd;
                this.metadataDirty = true;
            }

            var remaining = count;
            var sourceOffset = offset;

            while (remaining > 0)
            {
                var locator = this.LocatePage(this.position);

                if (locator.pageIndex < 0)
                    throw new IOException("File extents are not sufficient for write operation.");

                var pageIndex = locator.pageIndex;
                var offsetInPage = locator.offsetInPage;
                var bytesToWrite = Math.Min(this.pageSize - offsetInPage, remaining);
                var requiresBuffer = bytesToWrite != this.pageSize || offsetInPage != 0;

                var context = this.PreparePageForWrite(pageIndex, requiresBuffer);
                var pageId = context.PageId;
                var writablePage = this.GetWritablePageSpan(pageId, context.WasFresh, requiresBuffer);

                if (!requiresBuffer)
                {
                    var sourceSpan = buffer.AsSpan(sourceOffset, this.pageSize);
                    var destinationSpan = writablePage[..this.pageSize];
                    CopySpan(sourceSpan, destinationSpan);
                }
                else
                {
                    var sourceSpan = buffer.AsSpan(sourceOffset, bytesToWrite);
                    var destinationSpan = writablePage.Slice(offsetInPage, bytesToWrite);
                    CopySpan(sourceSpan, destinationSpan);
                }

                this.storage.WritePage(pageId, writablePage);

                sourceOffset += bytesToWrite;
                remaining -= bytesToWrite;
                this.position += bytesToWrite;
            }

            this.metadataDirty = true;
        }
    }

    /// <inheritdoc />
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        var rented = ArrayPool<byte>.Shared.Rent(buffer.Length);

        try
        {
            buffer.CopyTo(rented);
            this.Write(rented, 0, buffer.Length);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented, true);
        }
    }

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin)
    {
        lock (this.streamSync)
        {
            this.disposed.ThrowIf();

            var newPosition = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => this.position + offset,
                SeekOrigin.End => this.length + offset,
                _ => throw new ArgumentOutOfRangeException(nameof(origin)),
            };

            if (newPosition < 0)
                throw new IOException("Cannot seek to a negative position.");

            this.position = newPosition;

            return this.position;
        }
    }

    /// <inheritdoc />
    public override void SetLength(long value)
    {
        lock (this.streamSync)
        {
            this.disposed.ThrowIf();

            if (!this.CanWrite)
                throw new NotSupportedException("Stream is not writable.");

            ArgumentOutOfRangeException.ThrowIfNegative(value);

            if (value == this.length)
                return;

            if (value < this.length)
                this.TruncateInternal(value);
            else
            {
                this.EnsureCapacity(value);
                this.ZeroFill(this.length, value);
            }

            this.length = value;

            if (this.position > this.length)
                this.position = this.length;

            this.metadataDirty = true;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (this.disposed.Enter())
            return;

        if (disposing)
        {
            try
            {
                if (this.CanWrite && this.metadataDirty)
                    this.PersistMetadata();
            }
            finally
            {
                this.ClearStagedPagesWithoutFlush();
                this.lockHandle.Dispose();
                ArrayPool<byte>.Shared.Return(this.pageBuffer, true);
                this.onDispose?.Invoke(this);
            }
        }

        base.Dispose(disposing);
    }

    private void PersistMetadata()
    {
        var updatedMetadata = this.fileTable.Update(
            this.fileId,
            current => current with
            {
                Length = this.length,
                Extents = [..CoalescePages(this.pages)],
                ModifiedUtc = DateTime.UtcNow,
                AccessedUtc = DateTime.UtcNow,
            });

        var entry = new DirectoryEntry
        {
            Name = this.name,
            TargetId = this.fileId,
            Kind = FileKind.File,
            Attributes = updatedMetadata.Attributes,
            CreatedUtc = updatedMetadata.CreatedUtc,
            ModifiedUtc = updatedMetadata.ModifiedUtc,
            AccessedUtc = updatedMetadata.AccessedUtc,
            Generation = updatedMetadata.Generation,
        };

        this.directoryIndex.Upsert(this.parentId, this.name, entry);
        this.FlushStagedPages();
        this.StagePendingReleases();
        this.metadataPersistence.Flush();
        this.compactionEngine?.NotifyFileChanged(this.fileId);
        this.metadataDirty = false;
    }

    private void EnsureCapacity(long requiredLength)
    {
        if (requiredLength <= this.capacityBytes)
            return;

        var requiredPages = (int)((requiredLength + this.pageSize - 1) / this.pageSize);
        var currentPages = (int)(this.capacityBytes / this.pageSize);
        var additionalPages = requiredPages - currentPages;

        if (additionalPages <= 0)
            return;

        using var reservation = this.allocator.Reserve(additionalPages, ExtentAllocator.AllocationOwner.FileData);
        var span = reservation.Span;

        for (var i = 0; i < span.Length; i++)
        {
            var pageIdValue = span.Start.Value + i;
            this.pages.Add(new(pageIdValue));
            this.pendingPages.Add(pageIdValue);
        }

        reservation.Commit();

        this.capacityBytes += (long)span.Length * this.pageSize;
        this.metadataDirty = true;
    }

    private void ZeroFill(long from, long to)
    {
        if (to <= from)
            return;

        var remaining = to - from;
        var current = from;

        while (remaining > 0)
        {
            var locator = this.LocatePage(current);

            if (locator.pageIndex < 0)
                break;

            var offsetInPage = locator.offsetInPage;
            var bytesToWrite = (int)Math.Min(this.pageSize - offsetInPage, remaining);
            var requiresBuffer = bytesToWrite != this.pageSize || offsetInPage != 0;
            var context = this.PreparePageForWrite(locator.pageIndex, requiresBuffer);
            var pageId = context.PageId;
            var pageSpan = this.GetWritablePageSpan(pageId, context.WasFresh, requiresBuffer);

            if (!requiresBuffer)
            {
                ZeroSpan(pageSpan);
                this.storage.WritePage(pageId, pageSpan);
            }
            else
            {
                ZeroSpan(pageSpan.Slice(offsetInPage, bytesToWrite));
                this.storage.WritePage(pageId, pageSpan);
            }

            if (context.WasFresh)
                this.pendingPages.Add(pageId.Value);

            current += bytesToWrite;
            remaining -= bytesToWrite;
        }
    }

    private void TruncateInternal(long newLength)
    {
        if (newLength >= this.length)
            return;

        var newPageCount = (int)((newLength + this.pageSize - 1) / this.pageSize);
        var currentPageCount = this.pages.Count;

        if (newPageCount < currentPageCount)
        {
            var pagesToRelease = this.pages.GetRange(newPageCount, currentPageCount - newPageCount);

            foreach (var span in CoalescePages(pagesToRelease))
            {
                this.RemoveStagedRange(span);
                this.RemovePendingReleaseRange(span);
                this.allocator.Free(span, ExtentAllocator.AllocationOwner.FileData);
                this.RemovePendingRange(span);
            }

            this.pages.RemoveRange(newPageCount, currentPageCount - newPageCount);
        }

        if (newLength > 0 && newLength % this.pageSize != 0 && this.pages.Count > 0)
        {
            var lastPageIndex = this.pages.Count - 1;
            var pageId = this.pages[lastPageIndex];
            var pageSpan = this.GetWritablePageSpan(pageId, false, true);
            var cutoff = (int)(newLength % this.pageSize);
            ZeroSpan(pageSpan.Slice(cutoff, this.pageSize - cutoff));
        }

        this.capacityBytes = (long)this.pages.Count * this.pageSize;
    }

    private PageWriteContext PreparePageForWrite(int pageIndex, bool requiresBuffer)
    {
        var originalPageId = this.pages[pageIndex];
        var wasFresh = this.pendingPages.Remove(originalPageId.Value);

        if (wasFresh)
        {
            if (requiresBuffer)
                Array.Clear(this.pageBuffer, 0, this.pageBuffer.Length);

            return new(originalPageId, true);
        }

        if (requiresBuffer)
            this.storage.ReadPage(originalPageId, this.pageBuffer.AsSpan(0, this.pageSize));

        var replacement = this.AllocateDataPage();
        this.RemoveStagedPage(originalPageId.Value);
        this.pages[pageIndex] = replacement;
        this.pendingReleasePages.Add(originalPageId.Value);

        if (requiresBuffer)
        {
            var stagedBuffer = ArrayPool<byte>.Shared.Rent(this.pageSize);
            Buffer.BlockCopy(this.pageBuffer, 0, stagedBuffer, 0, this.pageSize);
            this.stagedPages[replacement.Value] = stagedBuffer;
        }

        return new(replacement, false);
    }

    private PageId AllocateDataPage()
    {
        using var reservation = this.allocator.Reserve(1, ExtentAllocator.AllocationOwner.FileData);
        var span = reservation.Span;
        reservation.Commit();

        return span.Start;
    }

    private void RemovePendingRange(PageSpan span)
    {
        if (!span.IsValid || this.pendingPages.Count == 0)
            return;

        var end = span.Start.Value + span.Length;

        for (var page = span.Start.Value; page < end; page++)
            this.pendingPages.Remove(page);
    }

    private void RemovePendingReleaseRange(PageSpan span)
    {
        if (!span.IsValid || this.pendingReleasePages.Count == 0)
            return;

        var end = span.Start.Value + span.Length;

        for (var page = span.Start.Value; page < end; page++)
            this.pendingReleasePages.Remove(page);
    }

    private void StagePendingReleases()
    {
        if (this.pendingReleasePages.Count == 0)
            return;

        Span<PageSpan> stackBuffer = stackalloc PageSpan[16];
        var spans = stackBuffer;

        if (this.pendingReleasePages.Count > stackBuffer.Length)
            spans = new PageSpan[this.pendingReleasePages.Count];

        var spanIndex = 0;
        long? currentStart = null;
        long currentLength = 0;

        foreach (var page in this.pendingReleasePages)
        {
            if (currentStart is null)
            {
                currentStart = page;
                currentLength = 1;

                continue;
            }

            if (page == currentStart.Value + currentLength)
                currentLength++;
            else
            {
                spans[spanIndex++] = new(new(currentStart.Value), (int)currentLength);
                currentStart = page;
                currentLength = 1;
            }
        }

        if (currentStart is not null)
            spans[spanIndex++] = new(new(currentStart.Value), (int)currentLength);

        for (var i = 0; i < spanIndex; i++)
        {
            var span = spans[i];
            this.RemoveStagedRange(span);
            this.allocator.Free(span, ExtentAllocator.AllocationOwner.FileData);
        }

        this.pendingReleasePages.Clear();
    }

    private (int pageIndex, int offsetInPage) LocatePage(long fileOffset)
    {
        if (fileOffset < 0)
            return (-1, 0);

        var pageIndex = (int)(fileOffset / this.pageSize);

        if (pageIndex >= this.pages.Count)
            return (-1, 0);

        var offsetInPage = (int)(fileOffset % this.pageSize);

        return (pageIndex, offsetInPage);
    }

    private Span<byte> GetWritablePageSpan(PageId pageId, bool wasFresh, bool requiresBuffer)
    {
        if (!this.stagedPages.TryGetValue(pageId.Value, out var buffer))
        {
            buffer = ArrayPool<byte>.Shared.Rent(this.pageSize);
            this.storage.ReadPage(pageId, buffer.AsSpan(0, this.pageSize));
            this.stagedPages[pageId.Value] = buffer;
        }

        if (wasFresh || !requiresBuffer)
            Array.Clear(buffer, 0, buffer.Length);

        return buffer.AsSpan(0, this.pageSize);
    }

    private ReadOnlySpan<byte> GetReadablePageSpan(PageId pageId)
    {
        if (this.stagedPages.TryGetValue(pageId.Value, out var buffer))
            return buffer.AsSpan(0, this.pageSize);

        this.storage.ReadPage(pageId, this.pageBuffer.AsSpan(0, this.pageSize));

        return this.pageBuffer.AsSpan(0, this.pageSize);
    }

    private void FlushStagedPages()
    {
        if (this.stagedPages.Count == 0)
            return;

        foreach (var kvp in this.stagedPages)
            ArrayPool<byte>.Shared.Return(kvp.Value, true);

        this.stagedPages.Clear();
    }

    private void RemoveStagedPage(long pageId)
    {
        if (this.stagedPages.Remove(pageId, out var buffer))
            ArrayPool<byte>.Shared.Return(buffer, true);
    }

    private void RemoveStagedRange(PageSpan span)
    {
        if (!span.IsValid)
            return;

        var end = span.Start.Value + span.Length;

        for (var page = span.Start.Value; page < end; page++)
            this.RemoveStagedPage(page);
    }

    private void ClearStagedPagesWithoutFlush()
    {
        if (this.stagedPages.Count == 0)
            return;

        foreach (var buffer in this.stagedPages.Values)
            ArrayPool<byte>.Shared.Return(buffer, true);

        this.stagedPages.Clear();
    }

    private static IEnumerable<PageId> ExpandExtents(IEnumerable<PageSpan> spans)
    {
        foreach (var span in spans)
        {
            if (!span.IsValid)
                continue;

            var start = span.Start.Value;
            var end = start + span.Length;

            for (var page = start; page < end; page++)
                yield return new(page);
        }
    }

    private static IEnumerable<PageSpan> CoalescePages(IEnumerable<PageId> pages)
    {
        long? runStart = null;
        long previous = 0;
        var length = 0;

        foreach (var page in pages)
        {
            var value = page.Value;

            if (runStart is null)
            {
                runStart = value;
                previous = value;
                length = 1;

                continue;
            }

            if (value == previous + 1)
            {
                previous = value;
                length++;

                continue;
            }

            yield return new(new(runStart.Value), length);

            runStart = value;
            previous = value;
            length = 1;
        }

        if (runStart is not null && length > 0)
            yield return new(new(runStart.Value), length);
    }

    private static void ValidateBuffer(byte[] buffer, int offset, int count)
    {
        buffer.Required();

        if ((uint)offset > buffer.Length)
            throw new ArgumentOutOfRangeException(nameof(offset));

        if ((uint)count > buffer.Length - offset)
            throw new ArgumentOutOfRangeException(nameof(count));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void CopySpan(ReadOnlySpan<byte> source, Span<byte> destination) => Unsafe.CopyBlockUnaligned(
        ref MemoryMarshal.GetReference(destination),
        ref MemoryMarshal.GetReference(source),
        (uint)source.Length);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ZeroSpan(Span<byte> span) => Unsafe.InitBlockUnaligned(ref MemoryMarshal.GetReference(span), 0, (uint)span.Length);

    private readonly struct PageWriteContext(PageId pageId, bool wasFresh)
    {
        public PageId PageId { get; } = pageId;
        public bool WasFresh { get; } = wasFresh;
    }
#if DEBUG
    internal bool DebugIsDisposed { get; private set; }

    internal string DebugIdentifier => $"{this.name}#{this.fileId.Value}";
#endif
}
