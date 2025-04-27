// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Itexoft.IO.VFS.Metadata.BTree;
using Itexoft.IO.VFS.Metadata.Models;

namespace Itexoft.IO.VFS.Metadata.Attributes;

internal sealed class AttributeTable
{
    private readonly FileTable fileTable;
    private readonly ReaderWriterLockSlim gate = new(LockRecursionPolicy.NoRecursion);
    private readonly BPlusTree<AttributeKey, AttributeRecord> tree;

    public AttributeTable(FileTable fileTable)
    {
        this.fileTable = fileTable;
        this.tree = new();
    }

    public void Upsert(FileId fileId, string name, ReadOnlySpan<byte> value)
    {
        if (!this.fileTable.TryGet(fileId, out _))
            throw new FileNotFoundException($"Cannot attach attribute to missing file {fileId.Value}.");

        var key = AttributeKey.Create(fileId, name);
        var now = DateTime.UtcNow;
        var buffer = ImmutableArray.CreateRange(value.ToArray());

        this.gate.EnterWriteLock();
        try
        {
            if (this.tree.TryGetValue(key, out var existing))
            {
                if (existing is null)
                    throw new InvalidOperationException("Attribute record cannot be null.");
                var updated = existing with
                {
                    Data = buffer,
                    ModifiedUtc = now
                };
                this.tree.Upsert(key, updated);
            }
            else
            {
                var record = new AttributeRecord
                {
                    FileId = fileId,
                    Name = name,
                    Data = buffer,
                    CreatedUtc = now,
                    ModifiedUtc = now
                };
                this.tree.Upsert(key, record);
            }
        }
        finally
        {
            this.gate.ExitWriteLock();
        }
    }

    public bool TryGet(FileId fileId, string name, [MaybeNullWhen(false)] out AttributeRecord record)
    {
        var key = AttributeKey.Create(fileId, name);
        this.gate.EnterReadLock();
        try
        {
            return this.tree.TryGetValue(key, out record);
        }
        finally
        {
            this.gate.ExitReadLock();
        }
    }

    public bool Remove(FileId fileId, string name)
    {
        var key = AttributeKey.Create(fileId, name);
        this.gate.EnterWriteLock();
        try
        {
            return this.tree.Remove(key);
        }
        finally
        {
            this.gate.ExitWriteLock();
        }
    }

    public IEnumerable<AttributeRecord> Enumerate(FileId fileId)
    {
        this.gate.EnterReadLock();
        try
        {
            var snapshot = new List<AttributeRecord>();
            foreach (var kvp in this.tree.Enumerate(k => k.FileId == fileId))
                snapshot.Add(kvp.Value);

            return snapshot;
        }
        finally
        {
            this.gate.ExitReadLock();
        }
    }

    public IEnumerable<KeyValuePair<AttributeKey, AttributeRecord>> EnumerateAll()
    {
        this.gate.EnterReadLock();
        try
        {
            return new List<KeyValuePair<AttributeKey, AttributeRecord>>(this.tree.EnumerateAll());
        }
        finally
        {
            this.gate.ExitReadLock();
        }
    }

    public void RemoveAll(FileId fileId)
    {
        var toRemove = new List<AttributeKey>();
        this.gate.EnterWriteLock();
        try
        {
            foreach (var kvp in this.tree.Enumerate(k => k.FileId == fileId))
                toRemove.Add(kvp.Key);

            foreach (var key in toRemove)
                this.tree.Remove(key);
        }
        finally
        {
            this.gate.ExitWriteLock();
        }
    }

    public void LoadFrom(IEnumerable<KeyValuePair<AttributeKey, AttributeRecord>> entries)
    {
        this.gate.EnterWriteLock();
        try
        {
            this.tree.Reset(entries);
        }
        finally
        {
            this.gate.ExitWriteLock();
        }
    }
}