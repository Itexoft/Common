// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Diagnostics.CodeAnalysis;

namespace Itexoft.IO.VFS.Metadata.BTree;

/// <summary>
/// Temporary in-memory B+ tree facade backed by <see cref="SortedDictionary{TKey, TValue}" />.
/// This keeps the call surface stable while on-disk pages are implemented.
/// </summary>
internal sealed class BPlusTree<TKey, TValue> where TKey : notnull
{
    private readonly SortedDictionary<TKey, TValue> store;

    public BPlusTree(IComparer<TKey>? comparer = null)
    {
        IComparer<TKey> effectiveComparer;

        if (comparer is null)
            effectiveComparer = Comparer<TKey>.Default;
        else
            effectiveComparer = comparer;

        this.store = new(effectiveComparer);
    }

    public int Count => this.store.Count;

    public bool TryGetValue(TKey key, [MaybeNullWhen(false), NotNullWhen(true)] out TValue value)
    {
        if (this.store.TryGetValue(key, out var temp))
        {
            value = temp!;

            return true;
        }

        value = default!;

        return false;
    }

    public void Upsert(TKey key, TValue value) => this.store[key] = value;

    public bool Remove(TKey key) => this.store.Remove(key);

    public bool ContainsKey(TKey key) => this.store.ContainsKey(key);

    public IEnumerable<KeyValuePair<TKey, TValue>> EnumerateAll() => this.store;

    public IEnumerable<KeyValuePair<TKey, TValue>> Enumerate(Func<TKey, bool> predicate)
    {
        foreach (var kvp in this.store)
        {
            if (predicate(kvp.Key))
                yield return kvp;
        }
    }

    public void Reset(IEnumerable<KeyValuePair<TKey, TValue>> items)
    {
        this.store.Clear();

        foreach (var kvp in items)
            this.store[kvp.Key] = kvp.Value;
    }
}
