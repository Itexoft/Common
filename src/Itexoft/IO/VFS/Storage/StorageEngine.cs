// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Buffers;
using System.Runtime.CompilerServices;
using Itexoft.IO.VFS.Core;
using Itexoft.IO.VFS.Diagnostics;
#if DEBUG
using Itexoft.IO.VFS.FileSystem;
#endif

namespace Itexoft.IO.VFS.Storage;

/// <summary>
/// Provides page-oriented direct access to the underlying stream used by the virtual file system.
/// </summary>
internal sealed class StorageEngine : IDisposable
{
    private const int SuperblockSlots = 2;

    private const int MinSuperblockSlotSize = 4 * 1024;
    private static readonly ConditionalWeakTable<Stream, object> StreamGates = new();
    private readonly byte[] fallbackSuperblock;
    private readonly object ioGate;
    private readonly Stream? mirrorStream;
    private readonly bool ownsMirrorStream;
    private readonly int pageSize;

    private readonly Stream stream;
    private readonly byte[] superblockCache;
    private readonly SpinLock superblockLock = new(enableThreadOwnerTracking: false);
    private readonly long superblockRegionLength;
    private readonly int superblockSlotSize;
    private byte activeSlot;
    private int disposed;
    private long fallbackGeneration;
    private byte fallbackSlot;
    private bool fallbackValid;
    private long generation;
    private long mirrorGeneration;

    private StorageEngine(Stream stream, Stream? mirrorStream, int pageSize, bool ownsMirrorStream)
    {
        this.stream = stream;
        this.mirrorStream = mirrorStream;
        this.ownsMirrorStream = ownsMirrorStream;
        this.pageSize = pageSize;
        this.superblockSlotSize = Math.Max(Math.Max(pageSize, SuperblockLayout.HeaderLength), MinSuperblockSlotSize);
        this.superblockRegionLength = (long)SuperblockSlots * this.superblockSlotSize;
        this.superblockCache = new byte[this.superblockSlotSize];
        this.fallbackSuperblock = new byte[this.superblockSlotSize];
        this.ioGate = StreamGates.GetValue(stream, _ => new());
#if DEBUG
        this.DebugId = Interlocked.Increment(ref nextDebugId);
        this.DebugName = $"{stream.GetType().Name}#{this.DebugId}";
        DebugPageTracker.RegisterStorage(this, this.DebugName);
#endif
    }

    /// <summary>
    /// Gets the page size the engine was initialized with.
    /// </summary>
    public int PageSize => this.pageSize;

    internal bool IsMirrored => this.mirrorStream is not null;
    internal long PrimaryGeneration => this.generation;
    internal long MirrorGeneration => this.mirrorStream is null ? this.generation : this.mirrorGeneration;

    /// <summary>
    /// Gets the effective payload size available for the logical superblock contents.
    /// </summary>
    public int SuperblockPayloadLength => this.superblockSlotSize - SuperblockLayout.HeaderLength;

    internal byte ActiveSlot => this.activeSlot;
    internal int SuperblockSlotSize => this.superblockSlotSize;

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref this.disposed, 1) != 0)
            return;

        this.stream.Flush();
        this.FlushMirror();
        if (this.ownsMirrorStream && this.mirrorStream is not null)
            this.mirrorStream.Dispose();
#if DEBUG
        DebugPageTracker.UnregisterStorage(this);
#endif
    }

    /// <summary>
    /// Creates and initializes a storage engine over the provided stream.
    /// </summary>
    /// <param name="stream">The backing stream that must support seeking, reading, and writing.</param>
    /// <param name="pageSize">Page size, in bytes, that the engine should use for I/O operations.</param>
    /// <returns>A ready-to-use storage engine instance.</returns>
    public static StorageEngine Open(Stream stream, int pageSize)
    {
        var engine = new StorageEngine(stream, null, pageSize, false);
        engine.Initialize();

        return engine;
    }

    /// <summary>
    /// Creates a storage engine that mirrors every write to a secondary stream.
    /// </summary>
    /// <param name="primary">Primary backing stream.</param>
    /// <param name="mirror">Secondary mirror stream that receives replicated writes.</param>
    /// <param name="pageSize">Page size, in bytes.</param>
    /// <param name="ownsMirrorStream">When <c>true</c>, the storage engine will dispose the mirror stream.</param>
    /// <returns>An initialized <see cref="StorageEngine" /> instance.</returns>
    public static StorageEngine OpenMirrored(Stream primary, Stream mirror, int pageSize, bool ownsMirrorStream)
    {
        var engine = new StorageEngine(primary, mirror, pageSize, ownsMirrorStream);
        engine.Initialize();

        return engine;
    }

    internal bool TryReadFallbackSuperblock(Span<byte> destination)
    {
        if (!this.fallbackValid || destination.Length < this.superblockSlotSize)
            return false;

        this.fallbackSuperblock.AsSpan(0, this.superblockSlotSize).CopyTo(destination);

        return true;
    }

    /// <summary>
    /// Reads the logical superblock payload into the supplied buffer.
    /// </summary>
    /// <param name="destination">Destination span that must be at least <see cref="SuperblockPayloadLength" /> bytes long.</param>
    public void ReadSuperblockPayload(Span<byte> destination)
    {
        if (destination.Length < this.SuperblockPayloadLength)
            throw new ArgumentException("Destination span too small for superblock payload.", nameof(destination));

        var source = this.superblockCache.AsSpan(SuperblockLayout.HeaderLength, this.SuperblockPayloadLength);
        source.CopyTo(destination);
    }

    /// <summary>
    /// Writes the logical superblock payload using double-buffered rotation and maintains a fallback copy.
    /// </summary>
    /// <param name="payload">The payload to persist.</param>
    public void WriteSuperblockPayload(ReadOnlySpan<byte> payload)
    {
        if (payload.Length > this.SuperblockPayloadLength)
            throw new ArgumentException("Payload exceeds superblock capacity.", nameof(payload));

        var lockTaken = false;
        try
        {
            this.superblockLock.Enter(ref lockTaken);

            var nextSlot = (byte)(1 - this.activeSlot);
            var nextGeneration = this.generation + 1;

            this.superblockCache.AsSpan().CopyTo(this.fallbackSuperblock);
            this.fallbackValid = true;
            this.fallbackSlot = this.activeSlot;
            this.fallbackGeneration = this.generation;

            var buffer = ArrayPool<byte>.Shared.Rent(this.superblockSlotSize);
            try
            {
                var state = new SuperblockLayout.SuperblockState(this.pageSize, nextGeneration, nextSlot);
                var span = buffer.AsSpan(0, this.superblockSlotSize);
                SuperblockLayout.Write(span, state, payload);
                this.WritePhysicalPage(nextSlot, span);
                span.CopyTo(this.superblockCache);
                this.activeSlot = nextSlot;
                this.generation = nextGeneration;
                this.mirrorGeneration = nextGeneration;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer, true);
            }
        }
        finally
        {
            if (lockTaken)
                this.superblockLock.Exit();
        }
    }

    /// <summary>
    /// Reads a physical page into the provided buffer.
    /// </summary>
    /// <param name="pageId">Identifier of the page to read.</param>
    /// <param name="destination">Destination span which must match the page size.</param>
    public void ReadPage(PageId pageId, Span<byte> destination)
    {
        if (!pageId.IsValid)
            throw new ArgumentOutOfRangeException(nameof(pageId));
        if (destination.Length != this.pageSize)
            throw new ArgumentException("Destination span must match page size.", nameof(destination));

        this.ReadPhysicalPage((long)pageId.Value, destination);
    }

    /// <summary>
    /// Writes a physical page from the provided buffer.
    /// </summary>
    /// <param name="pageId">Identifier of the page to write.</param>
    /// <param name="source">Source span which must match the page size.</param>
    public void WritePage(PageId pageId, ReadOnlySpan<byte> source)
    {
        if (!pageId.IsValid)
            throw new ArgumentOutOfRangeException(nameof(pageId));
        if (source.Length != this.pageSize)
            throw new ArgumentException("Source span must match page size.", nameof(source));

        this.WritePhysicalPage((long)pageId.Value, source);
    }

    /// <summary>
    /// Ensures that the backing stream is at least the specified length, extending it if needed.
    /// </summary>
    /// <param name="bytes">Target length in bytes.</param>
    public void EnsureLength(long bytes)
    {
        if (bytes < 0)
            throw new ArgumentOutOfRangeException(nameof(bytes));

        lock (this.ioGate)
        {
            if (bytes <= this.stream.Length)
            {
                this.EnsureMirrorLength(bytes);

                return;
            }

            try
            {
                this.stream.SetLength(bytes);
                this.EnsureMirrorLength(bytes);
            }
#if DEBUG
            catch (Exception ex)
            {
                DebugUtility.Log($"[StorageEngine] EnsureLength failed requested={bytes} current={this.stream.Length} error={ex}");

                throw;
            }
#else
            catch (Exception)
            {
                throw;
            }
#endif
        }
    }

    private void Initialize()
    {
        var requiredLength = this.superblockRegionLength;
        if (this.stream.Length < requiredLength)
        {
            this.ResizeStream(requiredLength);
            this.InitializeEmpty();

            return;
        }

        var buffer0 = ArrayPool<byte>.Shared.Rent(this.superblockSlotSize);
        var buffer1 = ArrayPool<byte>.Shared.Rent(this.superblockSlotSize);
        try
        {
            var span0 = buffer0.AsSpan(0, this.superblockSlotSize);
            var span1 = buffer1.AsSpan(0, this.superblockSlotSize);

            var valid0 = this.TryReadSlot(0, span0, out var state0);
            var valid1 = this.TryReadSlot(1, span1, out var state1);

            if (!valid0 && !valid1)
            {
                this.InitializeEmpty();

                return;
            }

            this.fallbackValid = false;

            if (!valid1 || (valid0 && state0.Generation >= state1.Generation))
            {
                span0.CopyTo(this.superblockCache);
                this.activeSlot = state0.ActiveSlot;
                this.generation = state0.Generation;

                if (valid1)
                {
                    span1.CopyTo(this.fallbackSuperblock);
                    this.fallbackValid = true;
                    this.fallbackSlot = state1.ActiveSlot;
                    this.fallbackGeneration = state1.Generation;
                }
            }
            else
            {
                span1.CopyTo(this.superblockCache);
                this.activeSlot = state1.ActiveSlot;
                this.generation = state1.Generation;

                if (valid0)
                {
                    span0.CopyTo(this.fallbackSuperblock);
                    this.fallbackValid = true;
                    this.fallbackSlot = state0.ActiveSlot;
                    this.fallbackGeneration = state0.Generation;
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer0);
            ArrayPool<byte>.Shared.Return(buffer1);
        }

        this.mirrorGeneration = this.generation;
    }

    private void InitializeEmpty()
    {
        Array.Clear(this.superblockCache);
        var state = new SuperblockLayout.SuperblockState(this.pageSize, 0, 0);
        var span = this.superblockCache.AsSpan();
        SuperblockLayout.Write(span, state, ReadOnlySpan<byte>.Empty);
        this.activeSlot = 0;
        this.generation = 0;
        this.fallbackValid = false;
        this.WritePhysicalPage(0, span);
        this.WritePhysicalPage(1, span);
        this.mirrorGeneration = this.generation;
    }

    private void ReadPhysicalPage(long pageIndex, Span<byte> destination) =>
        this.ReadPhysicalPageInternal(pageIndex, destination, DebugIoScope.Current);

    internal void ReadPhysicalPageUnsafe(long pageIndex, Span<byte> destination) =>
        this.ReadPhysicalPageInternal(pageIndex, destination, null);

    private void ReadPhysicalPageInternal(long pageIndex, Span<byte> destination, string? debugContext)
    {
        var expectedLength = this.GetPageLength(pageIndex);

        if (destination.Length != expectedLength)
            throw new ArgumentException(
                $"Destination span length {destination.Length} does not match page length {expectedLength}.",
                nameof(destination));

        lock (this.ioGate)
        {
            var offset = this.GetPageOffset(pageIndex);
            if (offset >= this.stream.Length)
            {
                destination.Clear();
#if DEBUG
                var context = DebugIoScope.Current;
                if (context is not null)
                    DebugUtility.Log($"[StorageEngine] read slot={pageIndex} context={context} beyond-length -> zeros");
#endif
                return;
            }

            this.stream.Seek(offset, SeekOrigin.Begin);
            var totalRead = 0;
            while (totalRead < destination.Length)
            {
                var read = this.stream.Read(destination.Slice(totalRead));
                if (read == 0)
                {
#if DEBUG
                    var ioContext = debugContext ?? DebugIoScope.Current;
                    DebugUtility.Log(
                        $"[StorageEngine][ReadShort] slot={pageIndex} context={ioContext} totalRead={totalRead} length={destination.Length} streamLength={this.stream.Length} position={this.stream.Position}");
#endif
                    destination.Slice(totalRead).Clear();

                    break;
                }

                totalRead += read;
            }

            if (totalRead < destination.Length)
                destination.Slice(totalRead).Clear();

            if (destination.Length > 0)
            {
                var first = destination[0];

#if DEBUG
                var trackerInfo = DebugPageTracker.DescribePage(this, pageIndex);
                var ioContext = debugContext ?? DebugIoScope.Current;
                if (ioContext is not null)
                    DebugUtility.Log($"[StorageEngine] read slot={pageIndex} context={ioContext} first=0x{first:X2} tracked={trackerInfo}");
#endif
                if (DebugPageTracker.TryGetPage(this, pageIndex, out var info) && first != info.SampleByte)
                    DebugBreakHelper.Trigger(
                        $"StorageEngine read mismatch slot={pageIndex} expected=0x{info.SampleByte:X2} actual=0x{first:X2} owner={info.Owner}#{info.FileId} seq={info.Sequence}");
            }
        }
    }

    private void WritePhysicalPage(long pageIndex, ReadOnlySpan<byte> source) =>
        this.WritePhysicalPageInternal(pageIndex, source, DebugIoScope.Current);

    internal void WritePhysicalPageUnsafe(long pageIndex, ReadOnlySpan<byte> source) =>
        this.WritePhysicalPageInternal(pageIndex, source, null);

    private void WritePhysicalPageInternal(long pageIndex, ReadOnlySpan<byte> source, string? debugContext)
    {
        var expectedLength = this.GetPageLength(pageIndex);

        if (source.Length != expectedLength)
            throw new ArgumentException($"Source span length {source.Length} does not match page length {expectedLength}.", nameof(source));

        lock (this.ioGate)
        {
            var offset = this.GetPageOffset(pageIndex);
            var requiredLength = offset + source.Length;
            if (requiredLength > this.stream.Length)
                this.stream.SetLength(requiredLength);
            this.EnsureMirrorLength(requiredLength);

#if DEBUG
            var ioContext = debugContext ?? DebugIoScope.Current;
            if (DebugUtility.Enabled && this.stream is VirtualFileStream baseStream && baseStream.DebugIsDisposed)
                DebugUtility.Log(
                    $"[StorageEngine] write target disposed slot={pageIndex} context={ioContext ?? "<null>"} target={baseStream.DebugIdentifier}");
#endif

            this.stream.Seek(offset, SeekOrigin.Begin);

            this.stream.Write(source);
            this.WriteMirror(pageIndex, source, offset);

#if DEBUG
            if (ioContext is not null && source.Length > 0)
            {
                var first = source[0];
                var trackerInfo = DebugPageTracker.DescribePage(this, pageIndex);
                DebugUtility.Log($"[StorageEngine] write slot={pageIndex} context={ioContext} first=0x{first:X2} tracked={trackerInfo}");

                var verifyBuffer = ArrayPool<byte>.Shared.Rent(source.Length);
                try
                {
                    var originalPosition = this.stream.Position;
                    this.stream.Seek(offset, SeekOrigin.Begin);
                    var totalRead = 0;
                    while (totalRead < source.Length)
                    {
                        var read = this.stream.Read(verifyBuffer, totalRead, source.Length - totalRead);
                        if (read == 0)
                        {
                            DebugUtility.Log(
                                $"[StorageEngine][WriteVerify] slot={pageIndex} context={ioContext} short-read={totalRead} expected={source.Length}");

                            break;
                        }

                        totalRead += read;
                    }

                    this.stream.Seek(originalPosition, SeekOrigin.Begin);

                    if (totalRead > 0)
                    {
                        var verifyFirst = verifyBuffer[0];
                        if (verifyFirst != first)
                            DebugBreakHelper.Trigger(
                                $"WriteVerify mismatch slot={pageIndex} context={ioContext} expected=0x{first:X2} actual=0x{verifyFirst:X2}");
                        else
                            DebugUtility.Log(
                                $"[StorageEngine][WriteVerify] slot={pageIndex} context={ioContext} verified=0x{verifyFirst:X2} readBytes={totalRead}");
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(verifyBuffer, true);
                }
            }
#endif
        }
    }

    private void EnsureMirrorLength(long bytes)
    {
        if (this.mirrorStream is null)
            return;

        if (bytes <= this.mirrorStream.Length)
            return;

        this.mirrorStream.SetLength(bytes);
    }

    private void ResizeMirror(long bytes)
    {
        if (this.mirrorStream is null)
            return;

        this.mirrorStream.SetLength(bytes);
    }

    private void WriteMirror(long pageIndex, ReadOnlySpan<byte> source, long offset)
    {
        if (this.mirrorStream is null)
            return;

        var mirror = this.mirrorStream;
        var requiredLength = offset + source.Length;
        if (requiredLength > mirror.Length)
            mirror.SetLength(requiredLength);

        mirror.Seek(offset, SeekOrigin.Begin);
        mirror.Write(source);
#if DEBUG
        if (DebugUtility.Enabled)
        {
            var context = DebugIoScope.Current;
            if (context is not null && source.Length > 0)
                DebugUtility.Log($"[StorageEngine] mirror write slot={pageIndex} context={context} first=0x{source[0]:X2}");
        }
#endif
    }

    private void FlushMirror()
    {
        if (this.mirrorStream is null)
            return;

        if (this.mirrorStream is FileStream fileStream)
            fileStream.Flush(true);
        else
            this.mirrorStream.Flush();
    }

    private void ResizeStream(long length)
    {
        lock (this.ioGate)
        {
            this.stream.SetLength(length);
            this.ResizeMirror(length);
        }
    }

    private long GetPageOffset(long pageIndex)
    {
        if (pageIndex < SuperblockSlots)
            return pageIndex * (long)this.superblockSlotSize;

        return this.superblockRegionLength + (pageIndex - SuperblockSlots) * (long)this.pageSize;
    }

    private int GetPageLength(long pageIndex) => pageIndex < SuperblockSlots ? this.superblockSlotSize : this.pageSize;

    private bool TryReadSlot(int slot, Span<byte> destination, out SuperblockLayout.SuperblockState state)
    {
        if (destination.Length < this.superblockSlotSize)
        {
            state = default;

            return false;
        }

        this.ReadSuperblockSlot(slot, destination.Slice(0, this.superblockSlotSize));

        return SuperblockLayout.TryParse(destination, out state);
    }

    private void ReadSuperblockSlot(int slot, Span<byte> destination)
    {
        lock (this.ioGate)
        {
            var offset = slot * (long)this.superblockSlotSize;
            this.stream.Seek(offset, SeekOrigin.Begin);
            var totalRead = 0;
            while (totalRead < destination.Length)
            {
                var read = this.stream.Read(destination.Slice(totalRead));
                if (read == 0)
                {
                    destination.Slice(totalRead).Clear();

                    break;
                }

                totalRead += read;
            }

            if (totalRead < destination.Length)
                destination.Slice(totalRead).Clear();
        }
    }
#if DEBUG
    private static long nextDebugId;
    internal long DebugId { get; }
    internal string DebugName { get; }
#endif
}