// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Buffers;
using System.Runtime.CompilerServices;
using Itexoft.Core;
using Itexoft.IO.VFS.Core;
#if DEBUG
#endif

namespace Itexoft.IO.VFS.Storage;

/// <summary>
/// Provides page-oriented direct access to the underlying stream used by the virtual file system.
/// </summary>
internal sealed class StorageEngine : IDisposable
{
    private const int superblockSlots = 2;

    private const int minSuperblockSlotSize = 4 * 1024;
    private static readonly ConditionalWeakTable<Stream, object> streamGates = new();
    private readonly byte[] fallbackSuperblock;
    private readonly object ioGate;
    private readonly Stream? mirrorStream;
    private readonly bool ownsMirrorStream;

    private readonly Stream stream;
    private readonly byte[] superblockCache;
    private readonly long superblockRegionLength;
    private Disposed disposed;
    private long fallbackGeneration;
    private byte fallbackSlot;
    private bool fallbackValid;
    private long mirrorGeneration;
    private SpinLock superblockLock = new(enableThreadOwnerTracking: false);

    private StorageEngine(Stream stream, Stream? mirrorStream, int pageSize, bool ownsMirrorStream)
    {
        this.stream = stream;
        this.mirrorStream = mirrorStream;
        this.ownsMirrorStream = ownsMirrorStream;
        this.PageSize = pageSize;
        this.SuperblockSlotSize = Math.Max(Math.Max(pageSize, SuperblockLayout.headerLength), minSuperblockSlotSize);
        this.superblockRegionLength = (long)superblockSlots * this.SuperblockSlotSize;
        this.superblockCache = new byte[this.SuperblockSlotSize];
        this.fallbackSuperblock = new byte[this.SuperblockSlotSize];
        this.ioGate = streamGates.GetValue(stream, _ => new());
    }

    /// <summary>
    /// Gets the page size the engine was initialized with.
    /// </summary>
    public int PageSize { get; }

    internal bool IsMirrored => this.mirrorStream is not null;
    internal long PrimaryGeneration { get; private set; }

    internal long MirrorGeneration => this.mirrorStream is null ? this.PrimaryGeneration : this.mirrorGeneration;

    /// <summary>
    /// Gets the effective payload size available for the logical superblock contents.
    /// </summary>
    public int SuperblockPayloadLength => this.SuperblockSlotSize - SuperblockLayout.headerLength;

    internal byte ActiveSlot { get; private set; }

    internal int SuperblockSlotSize { get; }

    /// <inheritdoc />
    public void Dispose()
    {
        if (this.disposed.Enter())
            return;

        this.stream.Flush();
        this.FlushMirror();

        if (this.ownsMirrorStream && this.mirrorStream is not null)
            this.mirrorStream.Dispose();
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
        if (!this.fallbackValid || destination.Length < this.SuperblockSlotSize)
            return false;

        this.fallbackSuperblock.AsSpan(0, this.SuperblockSlotSize).CopyTo(destination);

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

        var source = this.superblockCache.AsSpan(SuperblockLayout.headerLength, this.SuperblockPayloadLength);
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

            var nextSlot = (byte)(1 - this.ActiveSlot);
            var nextGeneration = this.PrimaryGeneration + 1;

            this.superblockCache.AsSpan().CopyTo(this.fallbackSuperblock);
            this.fallbackValid = true;
            this.fallbackSlot = this.ActiveSlot;
            this.fallbackGeneration = this.PrimaryGeneration;

            var buffer = ArrayPool<byte>.Shared.Rent(this.SuperblockSlotSize);

            try
            {
                var state = new SuperblockLayout.SuperblockState(this.PageSize, nextGeneration, nextSlot);
                var span = buffer.AsSpan(0, this.SuperblockSlotSize);
                SuperblockLayout.Write(span, state, payload);
                this.WritePhysicalPage(nextSlot, span);
                span.CopyTo(this.superblockCache);
                this.ActiveSlot = nextSlot;
                this.PrimaryGeneration = nextGeneration;
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

        if (destination.Length != this.PageSize)
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

        if (source.Length != this.PageSize)
            throw new ArgumentException("Source span must match page size.", nameof(source));

        this.WritePhysicalPage((long)pageId.Value, source);
    }

    /// <summary>
    /// Ensures that the backing stream is at least the specified length, extending it if needed.
    /// </summary>
    /// <param name="bytes">Target length in bytes.</param>
    public void EnsureLength(long bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);

        lock (this.ioGate)
        {
            if (bytes <= this.stream.Length)
            {
                this.EnsureMirrorLength(bytes);

                return;
            }

            this.stream.SetLength(bytes);
            this.EnsureMirrorLength(bytes);
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

        var buffer0 = ArrayPool<byte>.Shared.Rent(this.SuperblockSlotSize);
        var buffer1 = ArrayPool<byte>.Shared.Rent(this.SuperblockSlotSize);

        try
        {
            var span0 = buffer0.AsSpan(0, this.SuperblockSlotSize);
            var span1 = buffer1.AsSpan(0, this.SuperblockSlotSize);

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
                this.ActiveSlot = state0.ActiveSlot;
                this.PrimaryGeneration = state0.Generation;

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
                this.ActiveSlot = state1.ActiveSlot;
                this.PrimaryGeneration = state1.Generation;

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

        this.mirrorGeneration = this.PrimaryGeneration;
    }

    private void InitializeEmpty()
    {
        Array.Clear(this.superblockCache);
        var state = new SuperblockLayout.SuperblockState(this.PageSize, 0, 0);
        var span = this.superblockCache.AsSpan();
        SuperblockLayout.Write(span, state, ReadOnlySpan<byte>.Empty);
        this.ActiveSlot = 0;
        this.PrimaryGeneration = 0;
        this.fallbackValid = false;
        this.WritePhysicalPage(0, span);
        this.WritePhysicalPage(1, span);
        this.mirrorGeneration = this.PrimaryGeneration;
    }

    private void ReadPhysicalPage(long pageIndex, Span<byte> destination) =>
        this.ReadPhysicalPageInternal(pageIndex, destination);

    internal void ReadPhysicalPageUnsafe(long pageIndex, Span<byte> destination) =>
        this.ReadPhysicalPageInternal(pageIndex, destination);

    private void ReadPhysicalPageInternal(long pageIndex, Span<byte> destination)
    {
        var expectedLength = this.GetPageLength(pageIndex);

        if (destination.Length != expectedLength)
        {
            throw new ArgumentException(
                $"Destination span length {destination.Length} does not match page length {expectedLength}.",
                nameof(destination));
        }

        lock (this.ioGate)
        {
            var offset = this.GetPageOffset(pageIndex);

            if (offset >= this.stream.Length)
            {
                destination.Clear();

                return;
            }

            this.stream.Seek(offset, SeekOrigin.Begin);
            var totalRead = 0;

            while (totalRead < destination.Length)
            {
                var read = this.stream.Read(destination[totalRead..]);

                if (read == 0)
                {
                    destination[totalRead..].Clear();

                    break;
                }

                totalRead += read;
            }

            if (totalRead < destination.Length)
                destination[totalRead..].Clear();
        }
    }

    private void WritePhysicalPage(long pageIndex, ReadOnlySpan<byte> source) =>
        this.WritePhysicalPageInternal(pageIndex, source);

    private void WritePhysicalPageInternal(long pageIndex, ReadOnlySpan<byte> source)
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
            this.stream.Seek(offset, SeekOrigin.Begin);

            this.stream.Write(source);
            this.WriteMirror(pageIndex, source, offset);
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
        if (pageIndex < superblockSlots)
            return pageIndex * (long)this.SuperblockSlotSize;

        return this.superblockRegionLength + (pageIndex - superblockSlots) * (long)this.PageSize;
    }

    private int GetPageLength(long pageIndex) => pageIndex < superblockSlots ? this.SuperblockSlotSize : this.PageSize;

    private bool TryReadSlot(int slot, Span<byte> destination, out SuperblockLayout.SuperblockState state)
    {
        if (destination.Length < this.SuperblockSlotSize)
        {
            state = default;

            return false;
        }

        this.ReadSuperblockSlot(slot, destination[..this.SuperblockSlotSize]);

        return SuperblockLayout.TryParse(destination, out state);
    }

    private void ReadSuperblockSlot(int slot, Span<byte> destination)
    {
        lock (this.ioGate)
        {
            var offset = slot * (long)this.SuperblockSlotSize;
            this.stream.Seek(offset, SeekOrigin.Begin);
            var totalRead = 0;

            while (totalRead < destination.Length)
            {
                var read = this.stream.Read(destination[totalRead..]);

                if (read == 0)
                {
                    destination[totalRead..].Clear();

                    break;
                }

                totalRead += read;
            }

            if (totalRead < destination.Length)
                destination[totalRead..].Clear();
        }
    }
}
