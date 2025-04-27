// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Itexoft.IO.VFS.Allocation;
using Itexoft.IO.VFS.Core;
using Itexoft.IO.VFS.Metadata.BTree;
using Itexoft.IO.VFS.Metadata.Models;

namespace Itexoft.IO.VFS.Metadata;

internal sealed class FileTable
{
    private readonly ExtentAllocator allocator;
    private readonly ReaderWriterLockSlim gate = new(LockRecursionPolicy.NoRecursion);
    private readonly BPlusTree<FileId, FileMetadata> index;
    private long lastIdentifier;

    public FileTable(ExtentAllocator allocator)
    {
        this.allocator = allocator;
        this.index = new(Comparer<FileId>.Create((a, b) => a.Value.CompareTo(b.Value)));
        var now = DateTime.UtcNow;

        var rootMetadata = new FileMetadata
        {
            Kind = FileKind.Directory,
            Length = 0,
            Extents = ImmutableArray<PageSpan>.Empty,
            Attributes = FileAttributes.Directory,
            CreatedUtc = now,
            ModifiedUtc = now,
            AccessedUtc = now,
            Generation = 0,
        };

        this.index.Upsert(FileId.Root, rootMetadata);
        this.lastIdentifier = FileId.Root.Value;
    }

    public FileMetadata Get(FileId fileId)
    {
        this.gate.EnterReadLock();

        try
        {
            if (!this.index.TryGetValue(fileId, out var metadata))
                throw new KeyNotFoundException($"File {fileId.Value} not found.");

            return metadata;
        }
        finally
        {
            this.gate.ExitReadLock();
        }
    }

    public bool TryGet(FileId fileId, [MaybeNullWhen(false)] out FileMetadata metadata)
    {
        this.gate.EnterReadLock();

        try
        {
            return this.index.TryGetValue(fileId, out metadata);
        }
        finally
        {
            this.gate.ExitReadLock();
        }
    }

    public FileId Allocate(FileKind kind, FileAttributes attributes)
    {
        var idValue = Interlocked.Increment(ref this.lastIdentifier);
        var fileId = new FileId(idValue);
        var now = DateTime.UtcNow;
        var normalizedAttributes = NormalizeAttributes(kind, attributes);

        var metadata = new FileMetadata
        {
            Kind = kind,
            Length = 0,
            Extents = ImmutableArray<PageSpan>.Empty,
            Attributes = normalizedAttributes,
            CreatedUtc = now,
            ModifiedUtc = now,
            AccessedUtc = now,
            Generation = 0,
        };

        this.gate.EnterWriteLock();

        try
        {
            this.index.Upsert(fileId, metadata);
        }
        finally
        {
            this.gate.ExitWriteLock();
        }

        return fileId;
    }

    public FileMetadata Update(FileId fileId, Func<FileMetadata, FileMetadata> mutator)
    {
        this.gate.EnterWriteLock();

        try
        {
            if (!this.index.TryGetValue(fileId, out var current))
                throw new KeyNotFoundException($"File {fileId.Value} not found.");

            var updated = mutator(current) with
            {
                Generation = current.Generation + 1,
                ModifiedUtc = DateTime.UtcNow,
            };

            this.index.Upsert(fileId, updated);

            return updated;
        }
        finally
        {
            this.gate.ExitWriteLock();
        }
    }

    public bool Remove(FileId fileId)
    {
        this.gate.EnterWriteLock();

        try
        {
            var removed = this.index.Remove(fileId);

            return removed;
        }
        finally
        {
            this.gate.ExitWriteLock();
        }
    }

    public IEnumerable<KeyValuePair<FileId, FileMetadata>> Enumerate()
    {
        this.gate.EnterReadLock();

        try
        {
            return new List<KeyValuePair<FileId, FileMetadata>>(this.index.EnumerateAll());
        }
        finally
        {
            this.gate.ExitReadLock();
        }
    }

    public ExtentAllocator.ExtentReservation ReserveExtent(int pageCount) => this.allocator.Reserve(pageCount);

    public void LoadFrom(IEnumerable<KeyValuePair<FileId, FileMetadata>> entries)
    {
        this.gate.EnterWriteLock();

        try
        {
            this.index.Reset(entries);
            this.lastIdentifier = FileId.Root.Value;

            foreach (var kvp in this.index.EnumerateAll())
            {
                if (kvp.Key.Value > this.lastIdentifier)
                    this.lastIdentifier = kvp.Key.Value;
            }
        }
        finally
        {
            this.gate.ExitWriteLock();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static FileAttributes NormalizeAttributes(FileKind kind, FileAttributes attributes) => kind switch
    {
        FileKind.Directory => attributes | FileAttributes.Directory,
        _ => attributes & ~FileAttributes.Directory,
    };
}
