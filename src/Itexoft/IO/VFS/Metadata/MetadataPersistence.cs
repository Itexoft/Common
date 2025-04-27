// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Buffers;
using Itexoft.IO.VFS.Allocation;
using Itexoft.IO.VFS.Core;
using Itexoft.IO.VFS.Metadata.Attributes;
using Itexoft.IO.VFS.Metadata.Models;
using Itexoft.IO.VFS.Storage;

namespace Itexoft.IO.VFS.Metadata;

internal sealed class MetadataPersistence(
    StorageEngine storage,
    ExtentAllocator allocator,
    FileTable fileTable,
    DirectoryIndex directoryIndex,
    AttributeTable attributeTable)
{
    private static readonly byte[] headerMagic = "META"u8.ToArray();

    private readonly object syncRoot = new();
    private MetadataRoot currentRoot = MetadataRoot.Empty;

    public void Load()
    {
        lock (this.syncRoot)
        {
            var payload = ArrayPool<byte>.Shared.Rent(storage.SuperblockSlotSize);

            var usedSpans = new List<PageSpan>
            {
                new(new(0), 1),
                new(new(1), 1),
            };

            try
            {
                var span = payload.AsSpan(0, storage.SuperblockPayloadLength);
                storage.ReadSuperblockPayload(span);

                var loaded = this.TryLoadFrom(span, usedSpans);

                if (!loaded && storage.TryReadFallbackSuperblock(payload.AsSpan(0, storage.SuperblockSlotSize)))
                {
                    usedSpans.Clear();
                    usedSpans.Add(new(new(0), 1));
                    usedSpans.Add(new(new(1), 1));
                    loaded = this.TryLoadFrom(payload.AsSpan(0, storage.SuperblockPayloadLength), usedSpans);
                }

                if (!loaded)
                    this.ResetMetadata();
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(payload, true);
            }

            allocator.InitializeFromUsedPages(usedSpans);
            allocator.MarkMetadataRange(new(new(0), 2));
        }
    }

    private bool TryLoadFrom(ReadOnlySpan<byte> superblock, List<PageSpan> usedSpans)
    {
        if (!MetadataRoot.TryParse(superblock, out var root))
            return false;

        var fileEntries = Array.Empty<KeyValuePair<FileId, FileMetadata>>();
        var directoryEntries = Array.Empty<KeyValuePair<DirectoryKey, DirectoryEntry>>();
        var attributeEntries = Array.Empty<KeyValuePair<AttributeKey, AttributeRecord>>();

        if (root.FileTable.IsValid)
        {
            var data = this.ReadExtent(root.FileTable);

            if (data is null)
                return false;

            fileEntries = MetadataSerialization.DeserializeFileTable(data).ToArray();
        }

        if (root.DirectoryIndex.IsValid)
        {
            var data = this.ReadExtent(root.DirectoryIndex);

            if (data is null)
                return false;

            directoryEntries = MetadataSerialization.DeserializeDirectoryIndex(data).ToArray();
        }

        if (root.AttributeTable.IsValid)
        {
            var data = this.ReadExtent(root.AttributeTable);

            if (data is null)
                return false;

            attributeEntries = MetadataSerialization.DeserializeAttributeTable(data).ToArray();
        }

        fileTable.LoadFrom(fileEntries);
        directoryIndex.LoadFrom(directoryEntries);
        attributeTable.LoadFrom(attributeEntries);
        this.currentRoot = root;

        if (root.FileTable.IsValid)
        {
            allocator.MarkMetadataRange(root.FileTable.ToPageSpan());
            usedSpans.Add(root.FileTable.ToPageSpan());
        }

        if (root.DirectoryIndex.IsValid)
        {
            allocator.MarkMetadataRange(root.DirectoryIndex.ToPageSpan());
            usedSpans.Add(root.DirectoryIndex.ToPageSpan());
        }

        if (root.AttributeTable.IsValid)
        {
            allocator.MarkMetadataRange(root.AttributeTable.ToPageSpan());
            usedSpans.Add(root.AttributeTable.ToPageSpan());
        }

        foreach (var kvp in fileTable.Enumerate())
            usedSpans.AddRange(kvp.Value.Extents);

        return true;
    }

    public void Flush()
    {
        lock (this.syncRoot)
        {
            var fileData = MetadataSerialization.SerializeFileTable(fileTable.Enumerate());
            var directoryData = MetadataSerialization.SerializeDirectoryIndex(directoryIndex.EnumerateAll());
            var attributeData = MetadataSerialization.SerializeAttributeTable(attributeTable.EnumerateAll());

            var newRoot = new MetadataRoot
            {
                FileTable = this.WriteBuffer(fileData, "FileTable"),
                DirectoryIndex = this.WriteBuffer(directoryData, "DirectoryIndex"),
                AttributeTable = this.WriteBuffer(attributeData, "AttributeTable"),
            };

            this.WriteRoot(newRoot);

            this.ReleaseSpan(this.currentRoot.FileTable, newRoot.FileTable);
            this.ReleaseSpan(this.currentRoot.DirectoryIndex, newRoot.DirectoryIndex);
            this.ReleaseSpan(this.currentRoot.AttributeTable, newRoot.AttributeTable);

            this.currentRoot = newRoot;
            allocator.ReleaseStagedData();
        }
    }

    internal (PageSpan FileTable, PageSpan DirectoryIndex, PageSpan AttributeTable) CaptureDebugMetadata()
    {
        lock (this.syncRoot)
        {
            return (this.currentRoot.FileTable.ToPageSpan(), this.currentRoot.DirectoryIndex.ToPageSpan(),
                this.currentRoot.AttributeTable.ToPageSpan());
        }
    }

    private void ResetMetadata()
    {
        fileTable.LoadFrom([]);
        directoryIndex.LoadFrom([]);
        attributeTable.LoadFrom([]);
        this.currentRoot = MetadataRoot.Empty;
    }

    private MetadataSpan WriteBuffer(byte[] buffer, string ownerTag)
    {
        if (buffer.Length == 0)
            return MetadataSpan.Empty;

        var pageSize = storage.PageSize;
        var pageCount = (buffer.Length + pageSize - 1) / pageSize;

        using var reservation = allocator.Reserve(pageCount, ExtentAllocator.AllocationOwner.Metadata);
        var span = reservation.Span;
        allocator.MarkMetadataRange(span);

        var pooled = ArrayPool<byte>.Shared.Rent(pageSize);

        try
        {
            for (var i = 0; i < pageCount; i++)
            {
                var offset = i * pageSize;
                var slice = buffer.AsSpan(offset);
                var bytesToCopy = Math.Min(pageSize, buffer.Length - i * pageSize);
                Array.Clear(pooled, 0, pageSize);
                slice[..bytesToCopy].CopyTo(pooled);
                var pageId = new PageId(span.Start.Value + i);
                var pageSpan = pooled.AsSpan(0, pageSize);
                storage.WritePage(pageId, pageSpan);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(pooled, true);
        }

        reservation.Commit();
        var checksum = Hashing.HashToUInt32(buffer);

        return new(span.Start.Value, span.Length, buffer.Length, checksum);
    }

    private byte[]? ReadExtent(MetadataSpan span)
    {
        var pageSize = storage.PageSize;
        var buffer = new byte[(int)span.ByteLength];
        var pooled = ArrayPool<byte>.Shared.Rent(pageSize);

        try
        {
            var offset = 0;

            for (var i = 0; i < span.Length; i++)
            {
                var pageId = new PageId(span.Start + i);
                storage.ReadPage(pageId, pooled.AsSpan(0, pageSize));
                var bytesToCopy = Math.Min(pageSize, buffer.Length - offset);
                Array.Copy(pooled, 0, buffer, offset, bytesToCopy);
                offset += bytesToCopy;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(pooled, true);
        }

        var checksum = span.Checksum;

        if (checksum != 0 && checksum != Hashing.HashToUInt32(buffer))
            return null;

        return buffer;
    }

    private void WriteRoot(MetadataRoot root)
    {
        var payload = ArrayPool<byte>.Shared.Rent(storage.SuperblockPayloadLength);

        try
        {
            root.Write(payload.AsSpan(0, storage.SuperblockPayloadLength));
            storage.WriteSuperblockPayload(payload.AsSpan(0, storage.SuperblockPayloadLength));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(payload, true);
        }
    }

    private void ReleaseSpan(MetadataSpan previous, MetadataSpan next)
    {
        if (!previous.IsValid || (next.IsValid && previous.Start == next.Start && previous.Length == next.Length))
            return;

        allocator.Free(previous.ToPageSpan(), ExtentAllocator.AllocationOwner.Metadata);
    }

    private readonly record struct MetadataRoot
    {
        public MetadataSpan FileTable { get; init; }
        public MetadataSpan DirectoryIndex { get; init; }
        public MetadataSpan AttributeTable { get; init; }

        public static MetadataRoot Empty => new()
        {
            FileTable = MetadataSpan.Empty,
            DirectoryIndex = MetadataSpan.Empty,
            AttributeTable = MetadataSpan.Empty,
        };

        public void Write(Span<byte> destination)
        {
            destination.Clear();
            var writer = new SpanBinary.Writer(destination);
            writer.WriteBytes(headerMagic);
            writer.WriteInt32(1);
            this.FileTable.Write(ref writer);
            this.DirectoryIndex.Write(ref writer);
            this.AttributeTable.Write(ref writer);
        }

        public static bool TryParse(ReadOnlySpan<byte> span, out MetadataRoot root)
        {
            root = Empty;

            if (span.Length < headerMagic.Length + sizeof(int))
                return false;

            try
            {
                var reader = new SpanBinary.Reader(span);
                var header = reader.ReadBytes(headerMagic.Length);

                if (!header.SequenceEqual(headerMagic))
                    return false;

                var version = reader.ReadInt32();

                if (version != 1)
                    return false;

                var fileTable = MetadataSpan.Read(ref reader);
                var directory = MetadataSpan.Read(ref reader);
                var attribute = MetadataSpan.Read(ref reader);

                root = new()
                {
                    FileTable = fileTable,
                    DirectoryIndex = directory,
                    AttributeTable = attribute,
                };

                return true;
            }
            catch (InvalidDataException)
            {
                return false;
            }
        }
    }

    private readonly record struct MetadataSpan(long Start, int Length, long ByteLength, uint Checksum)
    {
        public static readonly MetadataSpan Empty = new(0, 0, 0, 0);
        public bool IsValid => this.Length > 0;

        public PageSpan ToPageSpan() => this.IsValid ? new(new(this.Start), this.Length) : PageSpan.Invalid;

        public void Write(ref SpanBinary.Writer writer)
        {
            writer.WriteInt64(this.Start);
            writer.WriteInt32(this.Length);
            writer.WriteInt64(this.ByteLength);
            writer.WriteUInt32(this.Checksum);
        }

        public static MetadataSpan Read(ref SpanBinary.Reader reader)
        {
            var start = reader.ReadInt64();
            var length = reader.ReadInt32();
            var bytes = reader.ReadInt64();
            var checksum = reader.ReadUInt32();

            return length > 0 ? new(start, length, bytes, checksum) : Empty;
        }
    }
}
