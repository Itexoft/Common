// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.IO.VFS.Core;
using Itexoft.IO.VFS.Diagnostics;
using Itexoft.IO.VFS.Storage;

namespace Itexoft.IO.VFS.Allocation;

/// <summary>
/// Coordinates page allocation for file data and metadata, ensuring deterministic reuse semantics across concurrent consumers.
/// </summary>
internal sealed class ExtentAllocator
{
    private readonly SortedDictionary<long, PageSpan> dataFree = new();
    private readonly SortedDictionary<long, PageSpan> metadataFree = new();
    private readonly int pageSize;
    private readonly List<PageSpan> stagedData = [];
    private readonly StorageEngine storage;
    private readonly object syncRoot = new();
    private long dataTail;
    private long metadataTail;
    private long totalPages;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExtentAllocator" /> class.
    /// </summary>
    /// <param name="storage">The storage engine that backs allocation decisions.</param>
    public ExtentAllocator(StorageEngine storage)
    {
        this.storage = storage;
        this.pageSize = storage.PageSize;
        this.totalPages = 2; // reserve superblock slots
        this.metadataTail = 2;
        this.dataTail = 2;
    }

    /// <summary>
    /// Gets the total number of pages currently tracked by the allocator (available in DEBUG builds only).
    /// </summary>
    public long DebugTotalPages => Volatile.Read(ref this.totalPages);

    /// <summary>
    /// Reserves a contiguous span of pages for the specified allocation owner.
    /// </summary>
    /// <param name="pageCount">Number of pages to reserve.</param>
    /// <param name="owner">The logical owner of the allocation (file data or metadata).</param>
    /// <returns>A reservation handle that must be committed or disposed.</returns>
    public ExtentReservation Reserve(int pageCount, AllocationOwner owner = AllocationOwner.FileData)
    {
        if (pageCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(pageCount));

        PageSpan span;
        var fromFree = true;
        lock (this.syncRoot)
        {
            // To guarantee crash-tolerance during concurrent stress we currently allocate
            // fresh pages for file data instead of reusing entries from the free list.
            // Metadata pages may still come from the metadata free pool.
            span = owner == AllocationOwner.Metadata ? this.TryAllocate(this.metadataFree, pageCount) : PageSpan.Invalid;
#if DEBUG
            if (span.IsValid)
                DebugUtility.Log(
                    $"[ExtentAllocator] allocate from {(owner == AllocationOwner.Metadata ? "metadata" : "data")} free start={span.Start.Value} len={span.Length} pageCount={pageCount}");
#endif
            if (!span.IsValid)
            {
                fromFree = false;
                span = this.AllocateNewSpan(owner, pageCount);
#if DEBUG
                DebugUtility.Log(
                    $"[ExtentAllocator] allocate new {(owner == AllocationOwner.Metadata ? "metadata" : "data")} span start={span.Start.Value} len={span.Length} totalPages={this.totalPages}");
#endif
            }
        }

        if (!fromFree)
            this.storage.EnsureLength(span.EndExclusive * this.pageSize);

#if DEBUG
        this.debugTracker.Allocate(span, owner);
#endif
        return new(this, span, owner);
    }

    /// <summary>
    /// Releases a previously allocated span assumed to belong to file data.
    /// </summary>
    /// <param name="span">The span to release.</param>
    public void Free(PageSpan span) => this.Free(span, AllocationOwner.FileData);

    /// <summary>
    /// Releases a previously allocated span for the specified owner.
    /// </summary>
    /// <param name="span">The span to release.</param>
    /// <param name="owner">The owner that originally held the pages.</param>
    public void Free(PageSpan span, AllocationOwner owner)
    {
        if (!span.IsValid)
            return;

#if DEBUG
        if (owner == AllocationOwner.FileData)
        {
            var seq = Interlocked.Increment(ref this.debugFreeSeq);
            DebugUtility.Log($"[ExtentAllocator] Free data span#{seq} start={span.Start.Value} len={span.Length}");
            DebugUtility.Log(Environment.StackTrace);
        }

        if (!this.debugTracker.TryRelease(span))
        {
            DebugUtility.Log($"[ExtentAllocator] skip release for span start={span.Start.Value} len={span.Length} (already free)");

            return;
        }
#endif

        lock (this.syncRoot)
        {
            if (owner == AllocationOwner.Metadata)
                InsertAndMerge(this.metadataFree, span);
            else
                this.stagedData.Add(span);
        }
    }

    /// <summary>
    /// Rebuilds allocator state from a set of spans known to be in use.
    /// </summary>
    /// <param name="usedSpans">Collection of spans that must remain allocated.</param>
    public void InitializeFromUsedPages(IEnumerable<PageSpan> usedSpans)
    {
        lock (this.syncRoot)
        {
            this.dataFree.Clear();
            this.metadataFree.Clear();
            this.stagedData.Clear();
            this.metadataTail = 2;
            this.dataTail = 2;

            var sorted = new List<PageSpan>();
            foreach (var span in usedSpans)
                if (span.IsValid)
                    sorted.Add(span);

            sorted.Sort(static (a, b) => a.Start.Value.CompareTo(b.Start.Value));

            long cursor = 2;
            long maxPage = 2;
#if DEBUG
            DebugUtility.Log($"[ExtentAllocator] initialize from used spans count={sorted.Count}");
#endif
#if DEBUG
            this.debugTracker.Reset(sorted);
#endif
            foreach (var span in sorted)
            {
                if (span.Start.Value > cursor)
                {
                    var gapLength = span.Start.Value - cursor;
                    if (gapLength > 0)
                        this.dataFree[cursor] = new(new(cursor), (int)gapLength);
                }

                var spanEnd = span.EndExclusive;
                cursor = Math.Max(cursor, spanEnd);
                if (spanEnd > maxPage)
                    maxPage = spanEnd;
            }

            var highestUsed = Math.Max(maxPage, cursor);
            if (this.totalPages < highestUsed)
                this.totalPages = highestUsed;
            this.metadataTail = Math.Max(this.metadataTail, 2);
            this.dataTail = Math.Max(this.dataTail, this.totalPages);
#if DEBUG
            DebugUtility.Log(
                $"[ExtentAllocator] init complete totalPages={this.totalPages} metadataTail={this.metadataTail} dataTail={this.dataTail}");
#endif
        }
    }

    /// <summary>
    /// Commits staged data spans into the general free list making them available for reuse.
    /// </summary>
    public void ReleaseStagedData()
    {
        lock (this.syncRoot)
        {
            if (this.stagedData.Count == 0)
                return;

#if DEBUG
            DebugUtility.Log($"[ExtentAllocator] release staged data count={this.stagedData.Count}");
#endif
            foreach (var span in this.stagedData)
            {
#if DEBUG
                DebugUtility.Log($"[ExtentAllocator] staged -> free span start={span.Start.Value} len={span.Length}");
#endif
                InsertAndMerge(this.dataFree, span);
            }

            this.stagedData.Clear();
        }
    }

    /// <summary>
    /// Marks a span as belonging to metadata, ensuring it will not be reused for file data allocations.
    /// </summary>
    /// <param name="span">The metadata span to register.</param>
    public void MarkMetadataRange(PageSpan span)
    {
        if (!span.IsValid)
            return;

        lock (this.syncRoot)
        {
            RemoveOverlap(this.dataFree, span);
            RemoveOverlap(this.metadataFree, span);
            var end = span.EndExclusive;
            if (end > this.metadataTail)
            {
                this.metadataTail = end;
                if (this.metadataTail > this.dataTail)
                    this.dataTail = this.metadataTail;
            }

            if (end > this.totalPages)
                this.totalPages = end;
#if DEBUG
            this.debugTracker.MarkMetadata(span);
#endif
        }
    }

    private PageSpan TryAllocate(SortedDictionary<long, PageSpan> freeList, int pageCount)
    {
        var selectedKey = long.MinValue;
        var selectedSpan = PageSpan.Invalid;

        foreach (var kvp in freeList)
            if (kvp.Value.Length >= pageCount)
            {
                selectedKey = kvp.Key;
                selectedSpan = kvp.Value;

                break;
            }

        if (!selectedSpan.IsValid)
            return PageSpan.Invalid;

        freeList.Remove(selectedKey);

        if (selectedSpan.Length == pageCount)
            return selectedSpan;

        var allocated = new PageSpan(selectedSpan.Start, pageCount);
        var remainderStart = selectedSpan.Start.Value + pageCount;
        freeList[remainderStart] = new(new(remainderStart), selectedSpan.Length - pageCount);

        return allocated;
    }

    private static void InsertAndMerge(SortedDictionary<long, PageSpan> freeList, PageSpan span)
    {
        var start = span.Start.Value;
        var end = span.EndExclusive;

        long? lowerKey = null;
        long? higherKey = null;

        foreach (var key in freeList.Keys)
        {
            if (key < start)
                lowerKey = key;
            if (key > start)
            {
                higherKey = key;

                break;
            }
        }

        if (lowerKey.HasValue)
        {
            var lowerSpan = freeList[lowerKey.Value];
            if (lowerSpan.EndExclusive == start)
            {
                start = lowerSpan.Start.Value;
                span = new(new(start), lowerSpan.Length + span.Length);
                freeList.Remove(lowerKey.Value);
            }
        }

        if (higherKey.HasValue)
        {
            var higherSpan = freeList[higherKey.Value];
            if (end == higherSpan.Start.Value)
            {
                span = new(new(start), span.Length + higherSpan.Length);
                end = span.EndExclusive;
                freeList.Remove(higherKey.Value);
            }
        }

        freeList[start] = span;
    }

    private static void RemoveOverlap(SortedDictionary<long, PageSpan> freeList, PageSpan removal)
    {
        if (freeList.Count == 0)
            return;

        var keys = new List<long>(freeList.Keys);
        var removalStart = removal.Start.Value;
        var removalEnd = removal.EndExclusive;

        foreach (var key in keys)
        {
            var span = freeList[key];
            var spanStart = span.Start.Value;
            var spanEnd = span.EndExclusive;

            if (removalEnd <= spanStart || removalStart >= spanEnd)
                continue;

            freeList.Remove(key);

            if (spanStart < removalStart)
            {
                var left = new PageSpan(new(spanStart), (int)(removalStart - spanStart));
                freeList[left.Start.Value] = left;
            }

            if (removalEnd < spanEnd)
            {
                var right = new PageSpan(new(removalEnd), (int)(spanEnd - removalEnd));
                freeList[right.Start.Value] = right;
            }
        }
    }

    private PageSpan AllocateNewSpan(AllocationOwner owner, int pageCount)
    {
        long start;
        if (owner == AllocationOwner.Metadata)
        {
            if (this.metadataTail < this.dataTail)
                this.metadataTail = this.dataTail;
            start = this.metadataTail;
            this.metadataTail += pageCount;
            if (this.metadataTail > this.dataTail)
                this.dataTail = this.metadataTail;
        }
        else
        {
            if (this.dataTail < this.metadataTail)
                this.dataTail = this.metadataTail;
            start = this.dataTail;
            this.dataTail += pageCount;
        }

        var newTotal = owner == AllocationOwner.Metadata ? this.metadataTail : this.dataTail;
        if (newTotal > this.totalPages)
            this.totalPages = newTotal;

#if DEBUG
        DebugUtility.Log(
            $"[ExtentAllocator] allocate new {owner} span start={start} len={pageCount} dataTail={this.dataTail} metadataTail={this.metadataTail}");
#endif
        return new(new(start), pageCount);
    }

    /// <summary>
    /// Represents a pending reservation that can be committed or automatically released.
    /// </summary>
    internal struct ExtentReservation : IDisposable
    {
        private readonly ExtentAllocator owner;
        private readonly AllocationOwner allocationOwner;
        private readonly PageSpan span;
        private bool completed;

        /// <summary>
        /// Initializes a new instance of the <see cref="ExtentReservation" /> struct.
        /// </summary>
        /// <param name="owner">Allocator that produced the reservation.</param>
        /// <param name="span">The span of pages reserved.</param>
        /// <param name="allocationOwner">The logical owner of the reservation.</param>
        public ExtentReservation(ExtentAllocator owner, PageSpan span, AllocationOwner allocationOwner)
        {
            this.owner = owner;
            this.span = span;
            this.allocationOwner = allocationOwner;
            this.completed = false;
        }

        /// <summary>
        /// Gets the underlying page span represented by the reservation.
        /// </summary>
        public PageSpan Span => this.span;

        /// <summary>
        /// Marks the reservation as successful so that the allocator does not roll it back during disposal.
        /// </summary>
        public void Commit()
        {
            this.completed = true;
        }

        /// <summary>
        /// Releases the reservation if it has not been committed.
        /// </summary>
        public void Dispose()
        {
            if (!this.completed)
                this.owner.Free(this.span, this.allocationOwner);
        }
    }

    internal enum AllocationOwner
    {
        FileData,
        Metadata
    }

#if DEBUG
    private sealed class DebugTracker
    {
        private readonly object gate = new();
        private readonly Dictionary<long, (int AllocationId, AllocationOwner Owner)> pages = new();
        private int nextAllocationId;

        internal void Allocate(PageSpan span, AllocationOwner owner)
        {
            var allocationId = Interlocked.Increment(ref this.nextAllocationId);
            var end = span.Start.Value + span.Length;
            lock (this.gate)
            {
                for (var page = span.Start.Value; page < end; page++)
                    if (!this.pages.TryAdd(page, (allocationId, owner)))
                    {
                        var previous = this.pages[page];
                        var stack = Environment.StackTrace;

                        throw new InvalidOperationException(
                            $"Page {page} is already allocated (existing allocation {previous.AllocationId}, new {allocationId}, owner {owner}). Stack:\n{stack}");
                    }
            }
        }

        internal bool TryRelease(PageSpan span)
        {
            var end = span.Start.Value + span.Length;
            lock (this.gate)
            {
                for (var page = span.Start.Value; page < end; page++)
                    if (!this.pages.Remove(page))
                        return false;
            }

            return true;
        }

        internal void Reset(IEnumerable<PageSpan> usedSpans)
        {
            lock (this.gate)
            {
                this.pages.Clear();
                this.nextAllocationId = 0;
                foreach (var span in usedSpans)
                {
                    if (!span.IsValid)
                        continue;
                    var end = span.Start.Value + span.Length;
                    for (var page = span.Start.Value; page < end; page++)
                        this.pages.TryAdd(page, (0, AllocationOwner.FileData));
                }
            }
        }

        internal void MarkMetadata(PageSpan span)
        {
            var end = span.Start.Value + span.Length;
            lock (this.gate)
            {
                for (var page = span.Start.Value; page < end; page++)
                    if (this.pages.TryGetValue(page, out var value))
                        this.pages[page] = (value.AllocationId, AllocationOwner.Metadata);
                    else
                        this.pages[page] = (0, AllocationOwner.Metadata);
            }
        }
    }
#endif
#if DEBUG
    private readonly DebugTracker debugTracker = new();
    private long debugFreeSeq;
#endif
}