// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Collections;
using System.Collections.Concurrent;

namespace Itexoft.Collections;

public class ConcurrentSet<T> : ISet<T> where T : notnull
{
    private readonly ConcurrentDictionary<T, bool> items;

    public ConcurrentSet() => this.items = [];
    public ConcurrentSet(IEnumerable<T> collection) => this.items = new(collection.Select(x => new KeyValuePair<T, bool>(x, true)));
    public ConcurrentSet(IEqualityComparer<T>? comparer) => this.items = new(comparer);

    public ConcurrentSet(IEnumerable<T> collection, IEqualityComparer<T>? comparer) => this.items = new(
        collection.Select(x => new KeyValuePair<T, bool>(x, true)),
        comparer);

    public ConcurrentSet(int concurrencyLevel, int capacity) => this.items = new(concurrencyLevel, capacity);

    public int Count => this.items.Count;

    public bool IsReadOnly => false;

    public bool Add(T item) => this.items.TryAdd(item, true);

    public void Clear() => this.items.Clear();

    public bool Contains(T item) => this.items.ContainsKey(item);

    public void CopyTo(T[] array, int arrayIndex) => this.items.Keys.CopyTo(array, arrayIndex);

    public void ExceptWith(IEnumerable<T> other)
    {
        ArgumentNullException.ThrowIfNull(other, nameof(other));

        foreach (var element in other)
            this.items.TryRemove(element, out _);
    }

    public IEnumerator<T> GetEnumerator() => this.items.Keys.GetEnumerator();

    public void IntersectWith(IEnumerable<T> other)
    {
        ArgumentNullException.ThrowIfNull(other);

        var keep = new HashSet<T>(other);
        foreach (var key in this.items.Keys)
            if (!keep.Contains(key))
                this.items.TryRemove(key, out _);
    }

    public bool IsProperSubsetOf(IEnumerable<T> other)
    {
        ArgumentNullException.ThrowIfNull(other, nameof(other));

        var otherSet = other as ICollection<T> ?? new HashSet<T>(other);

        if (this.Count >= otherSet.Count)
            return false;

        foreach (var key in this.items.Keys)
            if (!otherSet.Contains(key))
                return false;

        return true;
    }

    public bool IsProperSupersetOf(IEnumerable<T> other)
    {
        ArgumentNullException.ThrowIfNull(other, nameof(other));

        var otherSet = other as ICollection<T> ?? new HashSet<T>(other);

        if (otherSet.Count >= this.Count)
            return false;

        foreach (var element in otherSet)
            if (!this.items.ContainsKey(element))
                return false;

        return true;
    }

    public bool IsSubsetOf(IEnumerable<T> other)
    {
        ArgumentNullException.ThrowIfNull(other, nameof(other));

        var otherSet = other as ICollection<T> ?? new HashSet<T>(other);

        foreach (var key in this.items.Keys)
            if (!otherSet.Contains(key))
                return false;

        return true;
    }

    public bool IsSupersetOf(IEnumerable<T> other)
    {
        ArgumentNullException.ThrowIfNull(other, nameof(other));

        foreach (var element in other)
            if (!this.items.ContainsKey(element))
                return false;

        return true;
    }

    public bool Overlaps(IEnumerable<T> other)
    {
        ArgumentNullException.ThrowIfNull(other, nameof(other));

        foreach (var element in other)
            if (this.items.ContainsKey(element))
                return true;

        return false;
    }

    public bool Remove(T item) => this.items.TryRemove(item, out _);

    public bool SetEquals(IEnumerable<T> other)
    {
        ArgumentNullException.ThrowIfNull(other, nameof(other));

        var otherSet = other as ICollection<T> ?? new HashSet<T>(other);

        if (otherSet.Count != this.Count)
            return false;

        foreach (var key in this.items.Keys)
            if (!otherSet.Contains(key))
                return false;

        return true;
    }

    public void SymmetricExceptWith(IEnumerable<T> other)
    {
        ArgumentNullException.ThrowIfNull(other, nameof(other));

        foreach (var element in other)
            if (!this.items.TryAdd(element, true))
                this.items.TryRemove(element, out _);
    }

    public void UnionWith(IEnumerable<T> other)
    {
        ArgumentNullException.ThrowIfNull(other, nameof(other));

        foreach (var element in other)
            this.items.TryAdd(element, true);
    }

    void ICollection<T>.Add(T item) => this.Add(item);

    IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
}