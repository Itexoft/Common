namespace Itexoft.Common.Collections;

public class ConcurrentDictionaryLazy<TKey, TValue> where TKey : notnull
{
    private readonly ConcurrentDictionaryEx<TKey, Lazy<TValue>> source;

    public ConcurrentDictionaryLazy() => this.source = [];

    public ConcurrentDictionaryLazy(int concurrencyLevel, int capacity) => this.source = new(concurrencyLevel, capacity);

    public ConcurrentDictionaryLazy(IEnumerable<KeyValuePair<TKey, Lazy<TValue>>> collection) => this.source = new(collection);

    public ConcurrentDictionaryLazy(IEqualityComparer<TKey>? comparer) => this.source = new(comparer);

    public ConcurrentDictionaryLazy(IEnumerable<KeyValuePair<TKey, Lazy<TValue>>> collection, IEqualityComparer<TKey>? comparer) =>
        this.source = new(collection, comparer);

    public ConcurrentDictionaryLazy(
        int concurrencyLevel,
        IEnumerable<KeyValuePair<TKey, Lazy<TValue>>> collection,
        IEqualityComparer<TKey>? comparer)
    {
        this.source = new(concurrencyLevel, collection, comparer);
    }

    public ConcurrentDictionaryLazy(int concurrencyLevel, int capacity, IEqualityComparer<TKey>? comparer)
    {
        this.source = new(concurrencyLevel, capacity, comparer);
    }

    public ConcurrentDictionaryLazy(int concurrencyLevel, int capacity, bool growLockArray, IEqualityComparer<TKey>? comparer) =>
        this.source = new(concurrencyLevel, capacity, growLockArray, comparer);

    public bool TryRemove(TKey item, Func<TKey, TValue, bool> predicate, out TValue value)
    {
        if (this.source.TryRemove(item, (k, v) => predicate(k, v.Value), out var lazy))
        {
            value = lazy.Value;

            return true;
        }

        value = default!;

        return false;
    }

    public TValue GetOrAdd(TKey key, Func<TKey, TValue> valueFactory)
    {
        return this.source.GetOrAdd(key, k => new(() => valueFactory(k))).Value;
    }
}