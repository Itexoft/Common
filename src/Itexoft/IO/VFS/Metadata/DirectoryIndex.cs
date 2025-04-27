// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Diagnostics.CodeAnalysis;
using Itexoft.IO.VFS.Metadata.BTree;
using Itexoft.IO.VFS.Metadata.Models;

namespace Itexoft.IO.VFS.Metadata;

internal sealed class DirectoryIndex(FileTable fileTable)
{
    private readonly BPlusTree<DirectoryKey, DirectoryEntry> entries = new();
    private readonly ReaderWriterLockSlim gate = new(LockRecursionPolicy.NoRecursion);

    public void Upsert(FileId parentId, string name, DirectoryEntry entry)
    {
        var key = DirectoryKey.Create(parentId, name);
        var now = DateTime.UtcNow;

        this.gate.EnterWriteLock();

        try
        {
            if (this.entries.TryGetValue(key, out var existing))
            {
                var updated = existing with
                {
                    Name = name,
                    TargetId = entry.TargetId,
                    Kind = entry.Kind,
                    Attributes = entry.Attributes,
                    ModifiedUtc = now,
                    AccessedUtc = now,
                    Generation = existing.Generation + 1,
                };

                this.entries.Upsert(key, updated);
            }
            else
            {
                var created = entry with
                {
                    Name = name,
                    CreatedUtc = now,
                    ModifiedUtc = now,
                    AccessedUtc = now,
                    Generation = 0,
                };

                this.entries.Upsert(key, created);
            }
        }
        finally
        {
            this.gate.ExitWriteLock();
        }
    }

    public bool TryGet(FileId parentId, string name, [MaybeNullWhen(false)] out DirectoryEntry entry)
    {
        var key = DirectoryKey.Create(parentId, name);
        this.gate.EnterReadLock();

        try
        {
            return this.entries.TryGetValue(key, out entry);
        }
        finally
        {
            this.gate.ExitReadLock();
        }
    }

    public bool Remove(FileId parentId, string name)
    {
        var key = DirectoryKey.Create(parentId, name);
        this.gate.EnterWriteLock();

        try
        {
            return this.entries.Remove(key);
        }
        finally
        {
            this.gate.ExitWriteLock();
        }
    }

    public IEnumerable<DirectoryEntry> Enumerate(FileId parentId)
    {
        this.gate.EnterReadLock();

        try
        {
            var snapshot = new List<DirectoryEntry>();

            foreach (var kvp in this.entries.Enumerate(k => k.ParentId == parentId))
                snapshot.Add(kvp.Value);

            return snapshot;
        }
        finally
        {
            this.gate.ExitReadLock();
        }
    }

    public IEnumerable<KeyValuePair<DirectoryKey, DirectoryEntry>> EnumerateAll()
    {
        this.gate.EnterReadLock();

        try
        {
            return new List<KeyValuePair<DirectoryKey, DirectoryEntry>>(this.entries.EnumerateAll());
        }
        finally
        {
            this.gate.ExitReadLock();
        }
    }

    public void EnsureDirectoryExists(FileId directoryId)
    {
        if (!fileTable.TryGet(directoryId, out var metadata) || metadata.Kind != FileKind.Directory)
            throw new DirectoryNotFoundException($"Directory {directoryId.Value} not found.");
    }

    public void LoadFrom(IEnumerable<KeyValuePair<DirectoryKey, DirectoryEntry>> entries)
    {
        this.gate.EnterWriteLock();

        try
        {
            this.entries.Reset(entries);
        }
        finally
        {
            this.gate.ExitWriteLock();
        }
    }

    public bool TryFindByTarget(FileId targetId, out FileId parentId, out DirectoryEntry entry)
    {
        this.gate.EnterReadLock();

        try
        {
            foreach (var kvp in this.entries.EnumerateAll())
            {
                if (kvp.Value.TargetId == targetId)
                {
                    parentId = kvp.Key.ParentId;
                    entry = kvp.Value;

                    return true;
                }
            }
        }
        finally
        {
            this.gate.ExitReadLock();
        }

        parentId = FileId.Invalid;
        entry = default!;

        return false;
    }
}
