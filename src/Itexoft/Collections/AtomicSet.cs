// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Collections;
using Itexoft.Extensions;

namespace Itexoft.Collections;

public class AtomicSet<T> : ISet<T> where T : notnull
{
    private readonly AtomicDictionary<T, bool> items;

    public AtomicSet() => this.items = [];
    public AtomicSet(IEnumerable<T> collection) => this.items = new(collection.Required().Select(x => new KeyValuePair<T, bool>(x, true)));
    public AtomicSet(IEqualityComparer<T>? comparer) => this.items = new(comparer);

    public AtomicSet(IEnumerable<T> collection, IEqualityComparer<T>? comparer) => this.items = new(
        collection.Select(x => new KeyValuePair<T, bool>(x, true)),
        comparer);

    public AtomicSet(int concurrencyLevel, int capacity) => this.items = new(concurrencyLevel, capacity);

    public int Count => this.items.Count;

    public bool IsReadOnly => false;

    public bool Add(T item) => this.items.TryAdd(item, true);

    public void Clear() => this.items.Clear();

    public bool Contains(T item) => this.items.ContainsKey(item);

    public void CopyTo(T[] array, int arrayIndex) => this.items.Keys.CopyTo(array, arrayIndex);

    public void ExceptWith(IEnumerable<T> other)
    {
        foreach (var element in other.Required())
            this.items.TryRemove(element, out _);
    }

    public IEnumerator<T> GetEnumerator() => this.items.Keys.GetEnumerator();

    public void IntersectWith(IEnumerable<T> other)
    {
        var keep = new HashSet<T>(other.Required());

        foreach (var key in this.items.Keys)
        {
            if (!keep.Contains(key))
                this.items.TryRemove(key, out _);
        }
    }

    public bool IsProperSubsetOf(IEnumerable<T> other)
    {
        var otherSet = other.Required() as ICollection<T> ?? new HashSet<T>(other);

        if (this.Count >= otherSet.Count)
            return false;

        foreach (var key in this.items.Keys)
        {
            if (!otherSet.Contains(key))
                return false;
        }

        return true;
    }

    public bool IsProperSupersetOf(IEnumerable<T> other)
    {
        var otherSet = other.Required() as ICollection<T> ?? new HashSet<T>(other);

        if (otherSet.Count >= this.Count)
            return false;

        foreach (var element in otherSet)
        {
            if (!this.items.ContainsKey(element))
                return false;
        }

        return true;
    }

    public bool IsSubsetOf(IEnumerable<T> other)
    {
        var otherSet = other.Required() as ICollection<T> ?? new HashSet<T>(other);

        foreach (var key in this.items.Keys)
        {
            if (!otherSet.Contains(key))
                return false;
        }

        return true;
    }

    public bool IsSupersetOf(IEnumerable<T> other)
    {
        foreach (var element in other.Required())
        {
            if (!this.items.ContainsKey(element))
                return false;
        }

        return true;
    }

    public bool Overlaps(IEnumerable<T> other)
    {
        foreach (var element in other.Required())
        {
            if (this.items.ContainsKey(element))
                return true;
        }

        return false;
    }

    public bool Remove(T item) => this.items.TryRemove(item, out _);

    public bool SetEquals(IEnumerable<T> other)
    {
        var otherSet = other.Required() as ICollection<T> ?? new HashSet<T>(other);

        if (otherSet.Count != this.Count)
            return false;

        foreach (var key in this.items.Keys)
        {
            if (!otherSet.Contains(key))
                return false;
        }

        return true;
    }

    public void SymmetricExceptWith(IEnumerable<T> other)
    {
        foreach (var element in other.Required())
        {
            if (!this.items.TryAdd(element, true))
                this.items.TryRemove(element, out _);
        }
    }

    public void UnionWith(IEnumerable<T> other)
    {
        foreach (var element in other.Required())
            this.items.TryAdd(element, true);
    }

    void ICollection<T>.Add(T item) => this.Add(item);

    IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
}
