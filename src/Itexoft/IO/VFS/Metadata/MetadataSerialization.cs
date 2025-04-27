// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Collections.Immutable;
using Itexoft.IO.VFS.Core;
using Itexoft.IO.VFS.Metadata.Attributes;
using Itexoft.IO.VFS.Metadata.Models;

namespace Itexoft.IO.VFS.Metadata;

internal static class MetadataSerialization
{
    public static byte[] SerializeFileTable(IEnumerable<KeyValuePair<FileId, FileMetadata>> entries)
    {
        var list = entries.Where(static kvp => kvp.Value is not null).ToList();

        if (list.Count == 0)
            return [];

        var total = sizeof(int);
        foreach (var (_, metadata) in list)
        {
            total += sizeof(long); // file id
            total += sizeof(byte); // kind
            total += sizeof(long); // length
            total += sizeof(int); // attributes
            total += sizeof(long) * 3; // timestamps
            total += sizeof(int); // generation
            total += sizeof(int); // extent count
            total += metadata.Extents.Length * (sizeof(long) + sizeof(int)); // extent entries
        }

        var buffer = new byte[total];
        var writer = new SpanBinary.Writer(buffer);

        writer.WriteInt32(list.Count);
        foreach (var (fileId, metadata) in list)
        {
            writer.WriteInt64(fileId.Value);
            writer.WriteByte((byte)metadata.Kind);
            writer.WriteInt64(metadata.Length);
            writer.WriteInt32((int)metadata.Attributes);
            writer.WriteInt64(metadata.CreatedUtc.Ticks);
            writer.WriteInt64(metadata.ModifiedUtc.Ticks);
            writer.WriteInt64(metadata.AccessedUtc.Ticks);
            writer.WriteInt32(metadata.Generation);

            writer.WriteInt32(metadata.Extents.Length);
            foreach (var extent in metadata.Extents)
            {
                writer.WriteInt64(extent.Start.Value);
                writer.WriteInt32(extent.Length);
            }
        }

        return buffer;
    }

    public static IEnumerable<KeyValuePair<FileId, FileMetadata>> DeserializeFileTable(byte[] payload)
    {
        var reader = new SpanBinary.Reader(payload);
        var count = reader.ReadInt32();
        var result = new List<KeyValuePair<FileId, FileMetadata>>(count);

        for (var i = 0; i < count; i++)
        {
            var id = new FileId(reader.ReadInt64());
            var kind = (FileKind)reader.ReadByte();
            var length = reader.ReadInt64();
            var attributes = (FileAttributes)reader.ReadInt32();
            var created = new DateTime(reader.ReadInt64(), DateTimeKind.Utc);
            var modified = new DateTime(reader.ReadInt64(), DateTimeKind.Utc);
            var accessed = new DateTime(reader.ReadInt64(), DateTimeKind.Utc);
            var generation = reader.ReadInt32();

            var extentCount = reader.ReadInt32();
            var extentsBuilder = ImmutableArray.CreateBuilder<PageSpan>(extentCount);
            for (var j = 0; j < extentCount; j++)
            {
                var start = reader.ReadInt64();
                var len = reader.ReadInt32();
                extentsBuilder.Add(new(new(start), len));
            }

            var metadata = new FileMetadata
            {
                Kind = kind,
                Length = length,
                Attributes = attributes,
                CreatedUtc = created,
                ModifiedUtc = modified,
                AccessedUtc = accessed,
                Generation = generation,
                Extents = extentsBuilder.ToImmutable()
            };

            result.Add(new(id, metadata));
        }

        return result;
    }

    public static byte[] SerializeDirectoryIndex(IEnumerable<KeyValuePair<DirectoryKey, DirectoryEntry>> entries)
    {
        var list = entries.Where(static kvp => kvp.Value is not null).ToList();

        if (list.Count == 0)
            return [];

        var total = sizeof(int);
        foreach (var kvp in list)
        {
            var entry = kvp.Value;
            var name = entry.Name ?? string.Empty;
            var nameSize = SpanBinary.GetStringSize(name);

            total += sizeof(long); // parent id (from key)
            total += sizeof(long); // target id
            total += sizeof(byte); // kind
            total += sizeof(int); // attributes
            total += sizeof(long) * 3; // timestamps
            total += sizeof(int); // generation
            total += sizeof(int); // string length prefix
            total += nameSize; // string bytes
        }

        var buffer = new byte[total];
        var writer = new SpanBinary.Writer(buffer);

        writer.WriteInt32(list.Count);
        foreach (var kvp in list)
        {
            var key = kvp.Key;
            var entry = kvp.Value;
            writer.WriteInt64(key.ParentId.Value);
            writer.WriteInt64(entry.TargetId.Value);
            writer.WriteByte((byte)entry.Kind);
            writer.WriteInt32((int)entry.Attributes);
            writer.WriteInt64(entry.CreatedUtc.Ticks);
            writer.WriteInt64(entry.ModifiedUtc.Ticks);
            writer.WriteInt64(entry.AccessedUtc.Ticks);
            writer.WriteInt32(entry.Generation);
            writer.WriteString(entry.Name);
        }

        return buffer;
    }

    public static IEnumerable<KeyValuePair<DirectoryKey, DirectoryEntry>> DeserializeDirectoryIndex(byte[] payload)
    {
        var reader = new SpanBinary.Reader(payload);
        var count = reader.ReadInt32();
        var result = new List<KeyValuePair<DirectoryKey, DirectoryEntry>>(count);

        for (var i = 0; i < count; i++)
        {
            var parentId = new FileId(reader.ReadInt64());
            var targetId = new FileId(reader.ReadInt64());
            var kind = (FileKind)reader.ReadByte();
            var attributes = (FileAttributes)reader.ReadInt32();
            var created = new DateTime(reader.ReadInt64(), DateTimeKind.Utc);
            var modified = new DateTime(reader.ReadInt64(), DateTimeKind.Utc);
            var accessed = new DateTime(reader.ReadInt64(), DateTimeKind.Utc);
            var generation = reader.ReadInt32();
            var name = reader.ReadString();

            var key = DirectoryKey.Create(parentId, name);
            var entry = new DirectoryEntry
            {
                Name = name,
                TargetId = targetId,
                Kind = kind,
                Attributes = attributes,
                CreatedUtc = created,
                ModifiedUtc = modified,
                AccessedUtc = accessed,
                Generation = generation
            };

            result.Add(new(key, entry));
        }

        return result;
    }

    public static byte[] SerializeAttributeTable(IEnumerable<KeyValuePair<AttributeKey, AttributeRecord>> entries)
    {
        var list = entries.ToList();

        if (list.Count == 0)
            return [];

        var total = sizeof(int);
        foreach (var kvp in list)
        {
            var record = kvp.Value;
            var name = record.Name ?? string.Empty;
            var nameSize = SpanBinary.GetStringSize(name);
            var dataLength = record.Data.Length;

            total += sizeof(long); // file id
            total += sizeof(int); // string length prefix
            total += nameSize; // name bytes
            total += sizeof(int); // data length
            total += dataLength; // data bytes
            total += sizeof(long) * 2; // timestamps
        }

        var buffer = new byte[total];
        var writer = new SpanBinary.Writer(buffer);

        writer.WriteInt32(list.Count);
        foreach (var kvp in list)
        {
            var record = kvp.Value;
            writer.WriteInt64(kvp.Key.FileId.Value);
            writer.WriteString(record.Name);
            writer.WriteInt32(record.Data.Length);
            writer.WriteBytes(record.Data.AsSpan());
            writer.WriteInt64(record.CreatedUtc.Ticks);
            writer.WriteInt64(record.ModifiedUtc.Ticks);
        }

        return buffer;
    }

    public static IEnumerable<KeyValuePair<AttributeKey, AttributeRecord>> DeserializeAttributeTable(byte[] payload)
    {
        var reader = new SpanBinary.Reader(payload);
        var count = reader.ReadInt32();
        var result = new List<KeyValuePair<AttributeKey, AttributeRecord>>(count);

        for (var i = 0; i < count; i++)
        {
            var fileId = new FileId(reader.ReadInt64());
            var name = reader.ReadString();
            var dataLength = reader.ReadInt32();
            var dataBytes = reader.ReadBytes(dataLength);
            var created = new DateTime(reader.ReadInt64(), DateTimeKind.Utc);
            var modified = new DateTime(reader.ReadInt64(), DateTimeKind.Utc);

            var key = AttributeKey.Create(fileId, name);
            var record = new AttributeRecord
            {
                FileId = fileId,
                Name = name,
                Data = [..dataBytes.ToArray()],
                CreatedUtc = created,
                ModifiedUtc = modified
            };

            result.Add(new(key, record));
        }

        return result;
    }
}