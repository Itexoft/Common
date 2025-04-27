// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Buffers;
using System.Collections.Concurrent;
using System.Text;
using Itexoft.IO.VFS.Core;
using Itexoft.IO.VFS.Metadata.Models;
using Itexoft.IO.VFS.Storage;

namespace Itexoft.IO.VFS.Diagnostics;

internal static class DebugPageTracker
{
    internal readonly struct PageInfo
    {
        public PageInfo(long fileId, string owner, string context, byte sample, long sequence)
        {
            this.FileId = fileId;
            this.Owner = owner;
            this.Context = context;
            this.SampleByte = sample;
            this.Sequence = sequence;
        }

        public long FileId { get; }
        public string Owner { get; }
        public string Context { get; }
        public byte SampleByte { get; }
        public long Sequence { get; }
    }

#if DEBUG
    private readonly struct Entry
    {
        public Entry(long fileId, string owner, string context, byte sample, long sequence)
        {
            this.FileId = fileId;
            this.Owner = owner;
            this.Context = context;
            this.SampleByte = sample;
            this.Sequence = sequence;
        }

        public long FileId { get; }
        public string Owner { get; }
        public string Context { get; }
        public byte SampleByte { get; }
        public long Sequence { get; }
    }

    private sealed class StorageEntry
    {
        public StorageEntry(string name) => this.Name = name;

        public string Name { get; }
        public ConcurrentDictionary<long, Entry> Pages { get; } = new();
    }

    private static readonly ConcurrentDictionary<long, StorageEntry> Storages = new();
    private static long sequence;

    public static void RegisterStorage(StorageEngine storage, string name)
    {
        Storages[storage.DebugId] = new(name);
    }

    public static void UnregisterStorage(StorageEngine storage) => Storages.TryRemove(storage.DebugId, out _);

    private static StorageEntry GetOrCreate(StorageEngine storage)
    {
        return Storages.GetOrAdd(storage.DebugId, _ => new(storage.DebugName));
    }

    private static string NormalizeOwner(string owner, long fileId)
    {
        if (string.IsNullOrWhiteSpace(owner))
            return $"file:{fileId}";

        return owner;
    }

    public static void RecordWrite(
        StorageEngine storage,
        FileId fileId,
        PageId pageId,
        ReadOnlySpan<byte> buffer,
        string context,
        string owner)
    {
        if (!pageId.IsValid)
            return;

        var store = GetOrCreate(storage);
        var sample = buffer.Length > 0 ? buffer[0] : (byte)0;
        var entry = new Entry(
            fileId.Value,
            NormalizeOwner(owner, fileId.Value),
            context,
            sample,
            Interlocked.Increment(ref sequence));

        store.Pages.AddOrUpdate(
            pageId.Value,
            entry,
            (_, previous) =>
            {
                if (previous.FileId != entry.FileId || !string.Equals(previous.Owner, entry.Owner, StringComparison.Ordinal))
                {
                    var message =
                        $"[DebugPageTracker] storage={store.Name} page={pageId.Value} reassigned {previous.Owner}({previous.FileId}) -> {entry.Owner}({entry.FileId}) via {context}, bytes {previous.SampleByte:X2}->{entry.SampleByte:X2}, seq {previous.Sequence}->{entry.Sequence}";
                    DebugUtility.Log(message);
                }

                return entry;
            });
    }

    public static bool TryGetPage(StorageEngine storage, long pageId, out PageInfo info)
    {
        if (Storages.TryGetValue(storage.DebugId, out var store) && store.Pages.TryGetValue(pageId, out var entry))
        {
            info = new(entry.FileId, entry.Owner, entry.Context, entry.SampleByte, entry.Sequence);

            return true;
        }

        info = default;

        return false;
    }

    public static string Describe(StorageEngine storage, PageSpan span)
    {
        if (!Storages.TryGetValue(storage.DebugId, out var store))
            return "<tracker-missing>";

        var builder = new StringBuilder();
        var end = span.Start.Value + span.Length;
        for (var page = span.Start.Value; page < end; page++)
            if (store.Pages.TryGetValue(page, out var entry))
                builder.Append(
                    $"[{page}] owner={entry.Owner}({entry.FileId}) ctx={entry.Context} seq={entry.Sequence} sample=0x{entry.SampleByte:X2}; ");
            else
                builder.Append($"[{page}] <free>; ");

        return builder.ToString();
    }

    public static string DescribePage(StorageEngine storage, long pageId) => TryGetPage(storage, pageId, out var info)
        ? $"{info.Owner}({info.FileId}) ctx={info.Context} seq={info.Sequence} sample=0x{info.SampleByte:X2}"
        : "<untracked>";

    public static void Audit(StorageEngine storage, string scope)
    {
        if (!Storages.TryGetValue(storage.DebugId, out var store))
            return;

        var bufferLength = Math.Max(storage.PageSize, storage.SuperblockSlotSize);
        var buffer = ArrayPool<byte>.Shared.Rent(bufferLength);
        try
        {
            foreach (var kvp in store.Pages)
            {
                var pageId = new PageId(kvp.Key);
                var sliceLength = pageId.Value < 2 ? storage.SuperblockSlotSize : storage.PageSize;
                try
                {
                    storage.ReadPage(pageId, buffer.AsSpan(0, sliceLength));
                }
                catch (ObjectDisposedException ex)
                {
                    DebugUtility.Log($"[DebugPageTracker][AUDIT:{scope}] storage={store.Name} skipped ({ex.Message})");

                    return;
                }

                var value = buffer[0];
                if (value != kvp.Value.SampleByte)
                {
                    var message =
                        $"[DebugPageTracker][AUDIT:{scope}] storage={store.Name} page={pageId.Value} expected=0x{kvp.Value.SampleByte:X2} actual=0x{value:X2} owner={kvp.Value.Owner}({kvp.Value.FileId}) seq={kvp.Value.Sequence} ctx={kvp.Value.Context}";
                    DebugUtility.Log(message);
                    DebugBreakHelper.Trigger(message);
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, true);
        }
    }
#else
    public static void RegisterStorage(StorageEngine storage, string name) { }
    public static void UnregisterStorage(StorageEngine storage) { }

    public static void RecordWrite(
        StorageEngine storage,
        FileId fileId,
        PageId pageId,
        ReadOnlySpan<byte> buffer,
        string context,
        string owner) { }

    public static bool TryGetPage(StorageEngine storage, long pageId, out PageInfo info)
    {
        info = default;

        return false;
    }

    public static string Describe(StorageEngine storage, PageSpan span) => string.Empty;
    public static string DescribePage(StorageEngine storage, long pageId) => string.Empty;
    public static void Audit(StorageEngine storage, string scope) { }
#endif
}