// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Collections;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Itexoft.DotNET;
using Itexoft.Extensions;

namespace Itexoft.Collections;

[DebuggerTypeProxy(typeof(DictionaryDebugView<,>)), DebuggerDisplay("Count = {Count}")]
public class AtomicDictionary<TKey, TValue> : IDictionary<TKey, TValue>, IDictionary, IReadOnlyDictionary<TKey, TValue> where TKey : notnull
{
    private const int defaultCapacity = 31;

    private const int maxLockNumber = 1024;

    private readonly bool comparerIsDefaultForClasses;

    private readonly bool growLockArray;

    private readonly int initialCapacity;

    private int budget;

    private volatile Tables tables;

    public AtomicDictionary() : this(DefaultConcurrencyLevel, defaultCapacity, true, null) { }

    public AtomicDictionary(int concurrencyLevel, int capacity) : this(concurrencyLevel, capacity, false, null) { }

    public AtomicDictionary(IEnumerable<KeyValuePair<TKey, TValue>> collection) : this(DefaultConcurrencyLevel, collection, null) { }

    public AtomicDictionary(IEqualityComparer<TKey>? comparer) : this(DefaultConcurrencyLevel, defaultCapacity, true, comparer) { }

    public AtomicDictionary(IEnumerable<KeyValuePair<TKey, TValue>> collection, IEqualityComparer<TKey>? comparer) : this(
        DefaultConcurrencyLevel,
        GetCapacityFromCollection(collection),
        comparer)
    {
        collection.Required();

        this.InitializeFromCollection(collection);
    }

    public AtomicDictionary(int concurrencyLevel, IEnumerable<KeyValuePair<TKey, TValue>> collection, IEqualityComparer<TKey>? comparer) : this(
        concurrencyLevel,
        GetCapacityFromCollection(collection),
        false,
        comparer)
    {
        collection.Required();

        this.InitializeFromCollection(collection);
    }

    public AtomicDictionary(int concurrencyLevel, int capacity, IEqualityComparer<TKey>? comparer) :
        this(concurrencyLevel, capacity, false, comparer) { }

    internal AtomicDictionary(int concurrencyLevel, int capacity, bool growLockArray, IEqualityComparer<TKey>? comparer)
    {
        if (concurrencyLevel <= 0)
        {
            if (concurrencyLevel != -1)
                throw new ArgumentOutOfRangeException(nameof(concurrencyLevel), Sr.concurrentDictionaryConcurrencyLevelMustBePositiveOrNegativeOne);

            concurrencyLevel = DefaultConcurrencyLevel;
        }

        ArgumentOutOfRangeException.ThrowIfNegative(capacity);

        if (capacity < concurrencyLevel)
            capacity = concurrencyLevel;

        capacity = HashHelpers.GetPrime(capacity);

        var locks = new object[concurrencyLevel];
        locks[0] = locks;

        for (var i = 1; i < locks.Length; i++)
            locks[i] = new();

        var countPerLock = new int[locks.Length];
        var buckets = new VolatileNode[capacity];

        if (typeof(TKey).IsValueType)
        {
            if (comparer is not null && ReferenceEquals(comparer, EqualityComparer<TKey>.Default))
                comparer = null;
        }
        else
        {
            comparer ??= EqualityComparer<TKey>.Default;

            if (ReferenceEquals(comparer, EqualityComparer<TKey>.Default))
                this.comparerIsDefaultForClasses = true;
        }

        this.tables = new(buckets, locks, countPerLock, comparer);
        this.growLockArray = growLockArray;
        this.initialCapacity = capacity;
        this.budget = buckets.Length / locks.Length;
    }

    public IEqualityComparer<TKey> Comparer
    {
        get
        {
            var comparer = this.tables.comparer;

            return comparer ?? EqualityComparer<TKey>.Default;
        }
    }

    public bool IsEmpty
    {
        get
        {
            if (!this.AreAllBucketsEmpty())
                return false;

            var locksAcquired = 0;

            try
            {
                this.AcquireAllLocks(ref locksAcquired);

                return this.AreAllBucketsEmpty();
            }
            finally
            {
                this.ReleaseLocks(locksAcquired);
            }
        }
    }

    private static int DefaultConcurrencyLevel => Environment.ProcessorCount;

    public TValue this[TKey key]
    {
        get
        {
            if (!this.TryGetValue(key, out var value))
                ThrowKeyNotFoundException(key);

            return value;
        }
        set
        {
            if (key is null)
                ThrowHelper.ThrowKeyNullException();

            this.TryAddInternal(this.tables, key!, null, value, true, true, out _);
        }
    }

    public int Count
    {
        get
        {
            var locksAcquired = 0;

            try
            {
                this.AcquireAllLocks(ref locksAcquired);

                return this.GetCountNoLocks();
            }
            finally
            {
                this.ReleaseLocks(locksAcquired);
            }
        }
    }

    #region IEnumerable Members

    IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();

    #endregion

    public bool ContainsKey(TKey key) => this.TryGetValue(key, out _);

    public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value)
    {
        if (key is null)
            ThrowHelper.ThrowKeyNullException();

        var tables = this.tables;

        var comparer = tables.comparer;

        if (typeof(TKey).IsValueType && comparer is null)
        {
            var hashcode = key!.GetHashCode();

            for (var n = GetBucket(tables, hashcode); n is not null; n = n.next)
            {
                if (hashcode == n.hashcode && EqualityComparer<TKey>.Default.Equals(n.key, key))
                {
                    value = n.value;

                    return true;
                }
            }
        }
        else
        {
            Debug.Assert(comparer is not null);
            var hashcode = this.GetHashCode(comparer, key!);

            for (var n = GetBucket(tables, hashcode); n is not null; n = n.next)
            {
                if (hashcode == n.hashcode && comparer.Equals(n.key, key!))
                {
                    value = n.value;

                    return true;
                }
            }
        }

        value = default;

        return false;
    }

    public void Clear()
    {
        var locksAcquired = 0;

        try
        {
            this.AcquireAllLocks(ref locksAcquired);

            if (this.AreAllBucketsEmpty())
                return;

            var tables = this.tables;

            var newTables = new Tables(
                new VolatileNode[HashHelpers.GetPrime(this.initialCapacity)],
                tables.locks,
                new int[tables.countPerLock.Length],
                tables.comparer);

            this.tables = newTables;
            this.budget = Math.Max(1, newTables.buckets.Length / newTables.locks.Length);
        }
        finally
        {
            this.ReleaseLocks(locksAcquired);
        }
    }

    void ICollection<KeyValuePair<TKey, TValue>>.CopyTo(KeyValuePair<TKey, TValue>[] array, int index)
    {
        array.Required();

        ArgumentOutOfRangeException.ThrowIfNegative(index);

        var locksAcquired = 0;

        try
        {
            this.AcquireAllLocks(ref locksAcquired);

            var count = this.GetCountNoLocks();

            if (array.Length - count < index)
                throw new ArgumentException(Sr.concurrentDictionaryArrayNotLargeEnough);

            this.CopyToPairs(array, index);
        }
        finally
        {
            this.ReleaseLocks(locksAcquired);
        }
    }

    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => new Enumerator(this);

    public AlternateLookup<TAlternateKey> GetAlternateLookup<TAlternateKey>() where TAlternateKey : notnull, allows ref struct
    {
        if (!IsCompatibleKey<TAlternateKey>(this.tables))
            ThrowHelper.ThrowIncompatibleComparer();

        return new(this);
    }

    public bool TryGetAlternateLookup<TAlternateKey>(out AlternateLookup<TAlternateKey> lookup) where TAlternateKey : notnull, allows ref struct
    {
        if (IsCompatibleKey<TAlternateKey>(this.tables))
        {
            lookup = new(this);

            return true;
        }

        lookup = default;

        return false;
    }

    private static int GetCapacityFromCollection(IEnumerable<KeyValuePair<TKey, TValue>> collection) => collection switch
    {
        ICollection<KeyValuePair<TKey, TValue>> c => Math.Max(defaultCapacity, c.Count),
        IReadOnlyCollection<KeyValuePair<TKey, TValue>> rc => Math.Max(defaultCapacity, rc.Count),
        _ => defaultCapacity,
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int GetHashCode(IEqualityComparer<TKey>? comparer, TKey key)
    {
        if (typeof(TKey).IsValueType)
            return comparer is null ? key.GetHashCode() : comparer.GetHashCode(key);

        Debug.Assert(comparer is not null);

        return this.comparerIsDefaultForClasses ? key.GetHashCode() : comparer.GetHashCode(key);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool NodeEqualsKey(IEqualityComparer<TKey>? comparer, Node node, TKey key)
    {
        if (typeof(TKey).IsValueType)
            return comparer?.Equals(node.key, key) ?? EqualityComparer<TKey>.Default.Equals(node.key, key);

        Debug.Assert(comparer is not null);

        return comparer.Equals(node.key, key);
    }

    private void InitializeFromCollection(IEnumerable<KeyValuePair<TKey, TValue>> collection)
    {
        foreach (var pair in collection)
        {
            if (pair.Key is null)
                ThrowHelper.ThrowKeyNullException();

            if (!this.TryAddInternal(this.tables, pair.Key!, null, pair.Value, false, false, out _))
                throw new ArgumentException(Sr.concurrentDictionarySourceContainsDuplicateKeys);
        }

        if (this.budget == 0)
        {
            var tables = this.tables;
            this.budget = tables.buckets.Length / tables.locks.Length;
        }
    }

    public bool TryAdd(TKey key, TValue value)
    {
        if (key is null)
            ThrowHelper.ThrowKeyNullException();

        return this.TryAddInternal(this.tables, key!, null, value, false, true, out _);
    }

    public bool TryAdd(TKey key, Func<TKey, TValue> valueFactory)
    {
        if (key is null)
            ThrowHelper.ThrowKeyNullException();

        return this.TryAddInternal(this.tables, key!, null, valueFactory, false, true, out _);
    }

    public bool TryRemove(TKey key, [MaybeNullWhen(false)] out TValue value)
    {
        if (key is null)
            ThrowHelper.ThrowKeyNullException();

        return this.TryRemoveInternal(key!, null, out value, false, default);
    }

    public bool TryRemove(TKey key, Func<TKey, TValue, bool>? predicate, [MaybeNullWhen(false)] out TValue value)
    {
        if (key is null)
            ThrowHelper.ThrowKeyNullException();

        return this.TryRemoveInternal(key!, predicate, out value, false, default);
    }

    public bool TryRemove(KeyValuePair<TKey, TValue> item) => this.TryRemove(item, null);

    public bool TryRemove(KeyValuePair<TKey, TValue> item, Func<TKey, TValue, bool>? predicate)
    {
        if (item.Key is null)
            ThrowHelper.ThrowArgumentNullException(nameof(item), Sr.concurrentDictionaryItemKeyIsNull);

        return this.TryRemoveInternal(item.Key!, predicate, out _, true, item.Value);
    }

    private bool TryRemoveInternal(
        TKey key,
        Func<TKey, TValue, bool>? predicate,
        [MaybeNullWhen(false)] out TValue value,
        bool matchValue,
        TValue? oldValue)
    {
        var tables = this.tables;

        var comparer = tables.comparer;
        var hashcode = this.GetHashCode(comparer, key);

        while (true)
        {
            var locks = tables.locks;
            ref var bucket = ref GetBucketAndLock(tables, hashcode, out var lockNo);

            lock (locks[lockNo])
            {
                if (tables != this.tables)
                {
                    tables = this.tables;

                    if (!ReferenceEquals(comparer, tables.comparer))
                    {
                        comparer = tables.comparer;
                        hashcode = this.GetHashCode(comparer, key);
                    }

                    continue;
                }

                Node? prev = null;

                for (var curr = bucket; curr is not null; curr = curr.next)
                {
                    Debug.Assert((prev is null && curr == bucket) || prev!.next == curr);

                    if (hashcode == curr.hashcode && NodeEqualsKey(comparer, curr, key))
                    {
                        if (matchValue)
                        {
                            var valuesMatch = EqualityComparer<TValue>.Default.Equals(oldValue, curr.value);

                            if (!valuesMatch)
                            {
                                value = default;

                                return false;
                            }
                        }

                        if (predicate != null && !predicate(key, curr.value))
                        {
                            value = default;

                            return false;
                        }

                        if (prev is null)
                            Volatile.Write(ref bucket, curr.next);
                        else
                            prev.next = curr.next;

                        value = curr.value;
                        tables.countPerLock[lockNo]--;

                        return true;
                    }

                    prev = curr;
                }
            }

            value = default;

            return false;
        }
    }

    private static bool TryGetValueInternal(Tables tables, TKey key, int hashcode, [MaybeNullWhen(false)] out TValue value)
    {
        var comparer = tables.comparer;

        if (typeof(TKey).IsValueType && comparer is null)
        {
            for (var n = GetBucket(tables, hashcode); n is not null; n = n.next)
            {
                if (hashcode == n.hashcode && EqualityComparer<TKey>.Default.Equals(n.key, key))
                {
                    value = n.value;

                    return true;
                }
            }
        }
        else
        {
            Debug.Assert(comparer is not null);

            for (var n = GetBucket(tables, hashcode); n is not null; n = n.next)
            {
                if (hashcode == n.hashcode && comparer.Equals(n.key, key))
                {
                    value = n.value;

                    return true;
                }
            }
        }

        value = default;

        return false;
    }

    public bool TryUpdate(TKey key, TValue newValue, TValue comparisonValue)
    {
        if (key is null)
            ThrowHelper.ThrowKeyNullException();

        return this.TryUpdateInternal(this.tables, key!, null, newValue, comparisonValue);
    }

    private bool TryUpdateInternal(Tables tables, TKey key, int? nullableHashcode, TValue newValue, TValue comparisonValue)
    {
        var comparer = tables.comparer;

        var hashcode = nullableHashcode ?? this.GetHashCode(comparer, key);
        Debug.Assert(nullableHashcode is null || nullableHashcode == hashcode);

        var valueComparer = EqualityComparer<TValue>.Default;

        while (true)
        {
            var locks = tables.locks;
            ref var bucket = ref GetBucketAndLock(tables, hashcode, out var lockNo);

            lock (locks[lockNo])
            {
                if (tables != this.tables)
                {
                    tables = this.tables;

                    if (!ReferenceEquals(comparer, tables.comparer))
                    {
                        comparer = tables.comparer;
                        hashcode = this.GetHashCode(comparer, key);
                    }

                    continue;
                }

                Node? prev = null;

                for (var node = bucket; node is not null; node = node.next)
                {
                    Debug.Assert((prev is null && node == bucket) || prev!.next == node);

                    if (hashcode == node.hashcode && NodeEqualsKey(comparer, node, key))
                    {
                        if (valueComparer.Equals(node.value, comparisonValue))
                        {
                            if (!typeof(TValue).IsValueType || ConcurrentDictionaryTypeProps<TValue>.isWriteAtomic)
                                node.value = newValue;
                            else
                            {
                                var newNode = new Node(node.key, newValue, hashcode, node.next);

                                if (prev is null)
                                    Volatile.Write(ref bucket, newNode);
                                else
                                    prev.next = newNode;
                            }

                            return true;
                        }

                        return false;
                    }

                    prev = node;
                }

                return false;
            }
        }
    }

    public KeyValuePair<TKey, TValue>[] ToArray()
    {
        var locksAcquired = 0;

        try
        {
            this.AcquireAllLocks(ref locksAcquired);

            var count = this.GetCountNoLocks();

            if (count == 0)
                return [];

            var array = new KeyValuePair<TKey, TValue>[count];
            this.CopyToPairs(array, 0);

            return array;
        }
        finally
        {
            this.ReleaseLocks(locksAcquired);
        }
    }

    private void CopyToPairs(KeyValuePair<TKey, TValue>[] array, int index)
    {
        foreach (var bucket in this.tables.buckets)
        {
            for (var current = bucket.node; current is not null; current = current.next)
            {
                array[index] = new(current.key, current.value);
                Debug.Assert(index < int.MaxValue, "This method should only be called when there's no overflow risk");
                index++;
            }
        }
    }

    private void CopyToEntries(DictionaryEntry[] array, int index)
    {
        foreach (var bucket in this.tables.buckets)
        {
            for (var current = bucket.node; current is not null; current = current.next)
            {
                array[index] = new(current.key, current.value);
                Debug.Assert(index < int.MaxValue, "This method should only be called when there's no overflow risk");
                index++;
            }
        }
    }

    private void CopyToObjects(object[] array, int index)
    {
        foreach (var bucket in this.tables.buckets)
        {
            for (var current = bucket.node; current is not null; current = current.next)
            {
                array[index] = new KeyValuePair<TKey, TValue>(current.key, current.value);
                Debug.Assert(index < int.MaxValue, "This method should only be called when there's no overflow risk");
                index++;
            }
        }
    }

    private bool TryAddInternal(
        Tables tables,
        TKey key,
        int? nullableHashcode,
        TValue value,
        bool updateIfExists,
        bool acquireLock,
        out TValue resultingValue) => this.TryAddInternal<object>(
        tables,
        key,
        nullableHashcode,
        (_, _) => value,
        updateIfExists,
        acquireLock,
        null!,
        out resultingValue);

    private bool TryAddInternal(
        Tables tables,
        TKey key,
        int? nullableHashcode,
        Func<TKey, TValue> getValue,
        bool updateIfExists,
        bool acquireLock,
        out TValue resultingValue) => this.TryAddInternal<object>(
        tables,
        key,
        nullableHashcode,
        (k, _) => getValue(k),
        updateIfExists,
        acquireLock,
        null!,
        out resultingValue);

    private bool TryAddInternal<TArg>(
        Tables tables,
        TKey key,
        int? nullableHashcode,
        Func<TKey, TArg, TValue> getValue,
        bool updateIfExists,
        bool acquireLock,
        TArg factoryArg,
        out TValue resultingValue) where TArg : allows ref struct
    {
        var comparer = tables.comparer;

        var hashcode = nullableHashcode ?? this.GetHashCode(comparer, key);
        Debug.Assert(nullableHashcode is null || nullableHashcode == hashcode);

        while (true)
        {
            var locks = tables.locks;
            ref var bucket = ref GetBucketAndLock(tables, hashcode, out var lockNo);

            var resizeDesired = false;
            var lockTaken = false;

            try
            {
                if (acquireLock)
                    Monitor.Enter(locks[lockNo], ref lockTaken);

                if (tables != this.tables)
                {
                    tables = this.tables;

                    if (!ReferenceEquals(comparer, tables.comparer))
                    {
                        comparer = tables.comparer;
                        hashcode = this.GetHashCode(comparer, key);
                    }

                    continue;
                }

                Node? prev = null;

                for (var node = bucket; node is not null; node = node.next)
                {
                    Debug.Assert((prev is null && node == bucket) || prev!.next == node);

                    if (hashcode == node.hashcode && NodeEqualsKey(comparer, node, key))
                    {
                        if (updateIfExists)
                        {
                            resultingValue = getValue(key, factoryArg);

                            if (!typeof(TValue).IsValueType || ConcurrentDictionaryTypeProps<TValue>.isWriteAtomic)
                                node.value = resultingValue;
                            else
                            {
                                var newNode = new Node(node.key, resultingValue, hashcode, node.next);

                                if (prev is null)
                                    Volatile.Write(ref bucket, newNode);
                                else
                                    prev.next = newNode;
                            }
                        }
                        else
                            resultingValue = node.value;

                        return false;
                    }

                    prev = node;
                }

                resultingValue = getValue(key, factoryArg);

                var resultNode = new Node(key, resultingValue, hashcode, bucket);
                Volatile.Write(ref bucket, resultNode);

                checked
                {
                    tables.countPerLock[lockNo]++;
                }

                if (tables.countPerLock[lockNo] > this.budget)
                    resizeDesired = true;
            }
            finally
            {
                if (lockTaken)
                    Monitor.Exit(locks[lockNo]);
            }

            if (resizeDesired)
                this.GrowTable(tables, resizeDesired);

            return true;
        }
    }

    [DoesNotReturn]
    private static void ThrowKeyNotFoundException(TKey key) =>
        throw new KeyNotFoundException(Sr.Format(Sr.argKeyNotFoundWithKey, key.ToString()));

    private int GetCountNoLocks()
    {
        var count = 0;

        foreach (var value in this.tables.countPerLock)
        {
            checked
            {
                count += value;
            }
        }

        return count;
    }

    public TValue GetOrAdd(TKey key, Func<TKey, TValue> valueFactory)
    {
        if (key is null)
            ThrowHelper.ThrowKeyNullException();

        if (valueFactory is null)
            ThrowHelper.ThrowArgumentNullException(nameof(valueFactory));

        var tables = this.tables;

        var comparer = tables.comparer;
        var hashcode = this.GetHashCode(comparer, key!);

        if (!TryGetValueInternal(tables, key!, hashcode, out var resultingValue))
            this.TryAddInternal(tables, key!, hashcode, valueFactory!(key!), false, true, out resultingValue);

        return resultingValue;
    }

    public TValue GetOrAdd<TArg>(TKey key, Func<TKey, TArg, TValue> valueFactory, TArg factoryArgument) where TArg : allows ref struct
    {
        if (key is null)
            ThrowHelper.ThrowKeyNullException();

        valueFactory.Required();

        var tables = this.tables;

        var comparer = tables.comparer;
        var hashcode = this.GetHashCode(comparer, key!);

        if (!TryGetValueInternal(tables, key!, hashcode, out var resultingValue))
            this.TryAddInternal(tables, key!, hashcode, valueFactory, false, true, factoryArgument, out resultingValue);

        return resultingValue;
    }

    public TValue GetOrAdd(TKey key, TValue value)
    {
        if (key is null)
            ThrowHelper.ThrowKeyNullException();

        var tables = this.tables;

        var comparer = tables.comparer;
        var hashcode = this.GetHashCode(comparer, key!);

        if (!TryGetValueInternal(tables, key!, hashcode, out var resultingValue))
            this.TryAddInternal(tables, key!, hashcode, value, false, true, out resultingValue);

        return resultingValue;
    }

    public TValue AddOrUpdate(TKey key, TValue value, Func<TKey, TValue, TValue, TValue> updateValueFactory)
    {
        if (key is null)
            ThrowHelper.ThrowKeyNullException();

        if (updateValueFactory is null)
            ThrowHelper.ThrowArgumentNullException(nameof(updateValueFactory));

        var tables = this.tables;

        var comparer = tables.comparer;
        var hashcode = this.GetHashCode(comparer, key!);

        while (true)
        {
            if (TryGetValueInternal(tables, key!, hashcode, out var oldValue))
            {
                var newValue = updateValueFactory!(key!, oldValue, value);

                if (this.TryUpdateInternal(tables, key!, hashcode, newValue, oldValue))
                    return newValue;
            }
            else
            {
                if (this.TryAddInternal(tables, key!, hashcode, value, false, true, out var resultingValue))
                    return resultingValue;
            }

            if (tables != this.tables)
            {
                tables = this.tables;

                if (!ReferenceEquals(comparer, tables.comparer))
                {
                    comparer = tables.comparer;
                    hashcode = this.GetHashCode(comparer, key!);
                }
            }
        }
    }

    public TValue AddOrUpdate<TArg>(
        TKey key,
        Func<TKey, TArg, TValue> addValueFactory,
        Func<TKey, TValue, TArg, TValue> updateValueFactory,
        TArg factoryArgument) where TArg : allows ref struct
    {
        if (key is null)
            ThrowHelper.ThrowKeyNullException();

        if (addValueFactory is null)
            ThrowHelper.ThrowArgumentNullException(nameof(addValueFactory));

        if (updateValueFactory is null)
            ThrowHelper.ThrowArgumentNullException(nameof(updateValueFactory));

        var tables = this.tables;

        var comparer = tables.comparer;
        var hashcode = this.GetHashCode(comparer, key!);

        while (true)
        {
            if (TryGetValueInternal(tables, key!, hashcode, out var oldValue))
            {
                var newValue = updateValueFactory!(key!, oldValue, factoryArgument);

                if (this.TryUpdateInternal(tables, key!, hashcode, newValue, oldValue))
                    return newValue;
            }
            else
            {
                if (this.TryAddInternal(tables, key!, hashcode, addValueFactory!(key!, factoryArgument), false, true, out var resultingValue))
                    return resultingValue;
            }

            if (tables != this.tables)
            {
                tables = this.tables;

                if (!ReferenceEquals(comparer, tables.comparer))
                {
                    comparer = tables.comparer;
                    hashcode = this.GetHashCode(comparer, key!);
                }
            }
        }
    }

    public TValue AddOrUpdate(TKey key, Func<TKey, TValue> addValueFactory, Func<TKey, TValue, TValue> updateValueFactory)
    {
        if (key is null)
            ThrowHelper.ThrowKeyNullException();

        if (addValueFactory is null)
            ThrowHelper.ThrowArgumentNullException(nameof(addValueFactory));

        if (updateValueFactory is null)
            ThrowHelper.ThrowArgumentNullException(nameof(updateValueFactory));

        var tables = this.tables;

        var comparer = tables.comparer;
        var hashcode = this.GetHashCode(comparer, key!);

        while (true)
        {
            if (TryGetValueInternal(tables, key!, hashcode, out var oldValue))
            {
                var newValue = updateValueFactory!(key!, oldValue);

                if (this.TryUpdateInternal(tables, key!, hashcode, newValue, oldValue))
                    return newValue;
            }
            else
            {
                if (this.TryAddInternal(tables, key!, hashcode, addValueFactory!(key!), false, true, out var resultingValue))
                    return resultingValue;
            }

            if (tables != this.tables)
            {
                tables = this.tables;

                if (!ReferenceEquals(comparer, tables.comparer))
                {
                    comparer = tables.comparer;
                    hashcode = this.GetHashCode(comparer, key!);
                }
            }
        }
    }

    public TValue AddOrUpdate(TKey key, TValue addValue, Func<TKey, TValue, TValue> updateValueFactory)
    {
        if (key is null)
            ThrowHelper.ThrowKeyNullException();

        if (updateValueFactory is null)
            ThrowHelper.ThrowArgumentNullException(nameof(updateValueFactory));

        var tables = this.tables;

        var comparer = tables.comparer;
        var hashcode = this.GetHashCode(comparer, key!);

        while (true)
        {
            if (TryGetValueInternal(tables, key!, hashcode, out var oldValue))
            {
                var newValue = updateValueFactory!(key!, oldValue);

                if (this.TryUpdateInternal(tables, key!, hashcode, newValue, oldValue))
                    return newValue;
            }
            else
            {
                if (this.TryAddInternal(tables, key!, hashcode, addValue, false, true, out var resultingValue))
                    return resultingValue;
            }

            if (tables != this.tables)
            {
                tables = this.tables;

                if (!ReferenceEquals(comparer, tables.comparer))
                {
                    comparer = tables.comparer;
                    hashcode = this.GetHashCode(comparer, key!);
                }
            }
        }
    }

    private bool AreAllBucketsEmpty() => !this.tables.countPerLock.AsSpan().ContainsAnyExcept(0);

    private void GrowTable(Tables tables, bool resizeDesired)
    {
        var locksAcquired = 0;

        try
        {
            this.AcquireFirstLock(ref locksAcquired);

            if (tables != this.tables)
                return;

            var newLength = tables.buckets.Length;

            if (resizeDesired)
            {
                if (this.GetCountNoLocks() < tables.buckets.Length / 4)
                {
                    this.budget = 2 * this.budget;

                    if (this.budget < 0)
                        this.budget = int.MaxValue;

                    return;
                }

                if ((newLength = tables.buckets.Length * 2) < 0 || (newLength = HashHelpers.GetPrime(newLength)) > Array.MaxLength)
                {
                    newLength = Array.MaxLength;

                    this.budget = int.MaxValue;
                }
            }

            var newLocks = tables.locks;

            if (this.growLockArray && tables.locks.Length < maxLockNumber)
            {
                newLocks = new object[tables.locks.Length * 2];
                Array.Copy(tables.locks, newLocks, tables.locks.Length);

                for (var i = tables.locks.Length; i < newLocks.Length; i++)
                    newLocks[i] = new();
            }

            var newBuckets = new VolatileNode[newLength];
            var newCountPerLock = new int[newLocks.Length];
            var newTables = new Tables(newBuckets, newLocks, newCountPerLock, tables.comparer);

            AcquirePostFirstLock(tables, ref locksAcquired);

            foreach (var bucket in tables.buckets)
            {
                var current = bucket.node;

                while (current is not null)
                {
                    var hashCode = current.hashcode;

                    var next = current.next;
                    ref var newBucket = ref GetBucketAndLock(newTables, hashCode, out var newLockNo);

                    newBucket = new(current.key, current.value, hashCode, newBucket);

                    checked
                    {
                        newCountPerLock[newLockNo]++;
                    }

                    current = next;
                }
            }

            this.budget = Math.Max(1, newBuckets.Length / newLocks.Length);

            this.tables = newTables;
        }
        finally
        {
            this.ReleaseLocks(locksAcquired);
        }
    }

    private void AcquireAllLocks(ref int locksAcquired)
    {
        this.AcquireFirstLock(ref locksAcquired);
        AcquirePostFirstLock(this.tables, ref locksAcquired);
        Debug.Assert(locksAcquired == this.tables.locks.Length);
    }

    private void AcquireFirstLock(ref int locksAcquired)
    {
        var locks = this.tables.locks;
        Debug.Assert(locksAcquired == 0);
        Debug.Assert(!Monitor.IsEntered(locks[0]));

        Monitor.Enter(locks[0]);
        locksAcquired = 1;
    }

    private static void AcquirePostFirstLock(Tables tables, ref int locksAcquired)
    {
        var locks = tables.locks;
        Debug.Assert(Monitor.IsEntered(locks[0]));
        Debug.Assert(locksAcquired == 1);

        for (var i = 1; i < locks.Length; i++)
        {
            Monitor.Enter(locks[i]);
            locksAcquired++;
        }

        Debug.Assert(locksAcquired == locks.Length);
    }

    private void ReleaseLocks(int locksAcquired)
    {
        Debug.Assert(locksAcquired >= 0);

        var locks = this.tables.locks;

        for (var i = 0; i < locksAcquired; i++)
            Monitor.Exit(locks[i]);
    }

    private ReadOnlyCollection<TKey> GetKeys()
    {
        var locksAcquired = 0;

        try
        {
            this.AcquireAllLocks(ref locksAcquired);

            var count = this.GetCountNoLocks();

            if (count == 0)
                return ReadOnlyCollection<TKey>.Empty;

            var keys = new TKey[count];
            var i = 0;

            foreach (var bucket in this.tables.buckets)
            {
                for (var node = bucket.node; node is not null; node = node.next)
                {
                    keys[i] = node.key;
                    i++;
                }
            }

            Debug.Assert(i == count);

            return new(keys);
        }
        finally
        {
            this.ReleaseLocks(locksAcquired);
        }
    }

    private ReadOnlyCollection<TValue> GetValues()
    {
        var locksAcquired = 0;

        try
        {
            this.AcquireAllLocks(ref locksAcquired);

            var count = this.GetCountNoLocks();

            if (count == 0)
                return ReadOnlyCollection<TValue>.Empty;

            var keys = new TValue[count];
            var i = 0;

            foreach (var bucket in this.tables.buckets)
            {
                for (var node = bucket.node; node is not null; node = node.next)
                {
                    keys[i] = node.value;
                    i++;
                }
            }

            Debug.Assert(i == count);

            return new(keys);
        }
        finally
        {
            this.ReleaseLocks(locksAcquired);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Node? GetBucket(Tables tables, int hashcode)
    {
        var buckets = tables.buckets;

        if (nint.Size == 8)
            return buckets[HashHelpers.FastMod((uint)hashcode, (uint)buckets.Length, tables.fastModBucketsMultiplier)].node;

        return buckets[(uint)hashcode % (uint)buckets.Length].node;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ref Node? GetBucketAndLock(Tables tables, int hashcode, out uint lockNo)
    {
        var buckets = tables.buckets;
        uint bucketNo;

        if (nint.Size == 8)
            bucketNo = HashHelpers.FastMod((uint)hashcode, (uint)buckets.Length, tables.fastModBucketsMultiplier);
        else
            bucketNo = (uint)hashcode % (uint)buckets.Length;

        lockNo = bucketNo % (uint)tables.locks.Length;

        return ref buckets[bucketNo].node;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsCompatibleKey<TAlternateKey>(Tables tables) where TAlternateKey : notnull, allows ref struct
    {
        Debug.Assert(tables is not null);

        return tables.comparer is IAlternateEqualityComparer<TAlternateKey, TKey>;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static IAlternateEqualityComparer<TAlternateKey, TKey> GetAlternateComparer<TAlternateKey>(Tables tables)
        where TAlternateKey : notnull, allows ref struct
    {
        Debug.Assert(IsCompatibleKey<TAlternateKey>(tables));

        return Unsafe.As<IAlternateEqualityComparer<TAlternateKey, TKey>>(tables.comparer!);
    }

    private sealed class Enumerator(AtomicDictionary<TKey, TValue> dictionary) : IEnumerator<KeyValuePair<TKey, TValue>>
    {
        private const int stateUninitialized = 0;
        private const int stateOuterloop = 1;
        private const int stateInnerLoop = 2;
        private const int stateDone = 3;

        private VolatileNode[]? buckets;
        private int i = -1;
        private Node? node;
        private int state;

        object IEnumerator.Current => this.Current;

        public KeyValuePair<TKey, TValue> Current { get; private set; }

        public void Reset()
        {
            this.buckets = null;
            this.node = null;
            this.Current = default;
            this.i = -1;
            this.state = stateUninitialized;
        }

        public void Dispose() { }

        public bool MoveNext()
        {
            switch (this.state)
            {
                case stateUninitialized:
                    this.buckets = dictionary.tables.buckets;
                    this.i = -1;
                    goto case stateOuterloop;

                case stateOuterloop:
                    var buckets = this.buckets;
                    Debug.Assert(buckets is not null);

                    var i = ++this.i;

                    if ((uint)i < (uint)buckets.Length)
                    {
                        this.node = buckets[i].node;
                        this.state = stateInnerLoop;
                        goto case stateInnerLoop;
                    }

                    goto default;

                case stateInnerLoop:
                    if (this.node is Node node)
                    {
                        this.Current = new(node.key, node.value);
                        this.node = node.next;

                        return true;
                    }

                    goto case stateOuterloop;

                default:
                    this.state = stateDone;

                    return false;
            }
        }
    }

    private struct VolatileNode
    {
        internal volatile Node? node;
    }

    private sealed class Node
    {
        internal readonly int hashcode;
        internal readonly TKey key;
        internal volatile Node? next;
        internal TValue value;

        internal Node(TKey key, TValue value, int hashcode, Node? next)
        {
            this.key = key;
            this.value = value;
            this.next = next;
            this.hashcode = hashcode;
        }
    }

    private sealed class Tables
    {
        internal readonly VolatileNode[] buckets;

        internal readonly IEqualityComparer<TKey>? comparer;

        internal readonly int[] countPerLock;

        internal readonly ulong fastModBucketsMultiplier;

        internal readonly object[] locks;

        internal Tables(VolatileNode[] buckets, object[] locks, int[] countPerLock, IEqualityComparer<TKey>? comparer)
        {
            Debug.Assert(typeof(TKey).IsValueType || comparer is not null);

            this.buckets = buckets;
            this.locks = locks;
            this.countPerLock = countPerLock;
            this.comparer = comparer;

            if (nint.Size == 8)
                this.fastModBucketsMultiplier = HashHelpers.GetFastModMultiplier((uint)buckets.Length);
        }
    }

    private sealed class DictionaryEnumerator : IDictionaryEnumerator
    {
        private readonly IEnumerator<KeyValuePair<TKey, TValue>> enumerator;

        internal DictionaryEnumerator(AtomicDictionary<TKey, TValue> dictionary) => this.enumerator = dictionary.GetEnumerator();

        public DictionaryEntry Entry => new(this.enumerator.Current.Key, this.enumerator.Current.Value);

        public object Key => this.enumerator.Current.Key;

        public object? Value => this.enumerator.Current.Value;

        public object Current => this.Entry;

        public bool MoveNext() => this.enumerator.MoveNext();

        public void Reset() => this.enumerator.Reset();
    }

    public readonly struct AlternateLookup<TAlternateKey> where TAlternateKey : notnull, allows ref struct
    {
        internal AlternateLookup(AtomicDictionary<TKey, TValue> dictionary)
        {
            Debug.Assert(dictionary is not null);
            Debug.Assert(IsCompatibleKey<TAlternateKey>(dictionary.tables));
            this.Dictionary = dictionary;
        }

        public AtomicDictionary<TKey, TValue> Dictionary { get; }

        public TValue this[TAlternateKey key]
        {
            get => this.TryGetValue(key, out var value) ? value : throw new KeyNotFoundException();
            set => this.TryAdd(key, value, true, out _);
        }

        public bool ContainsKey(TAlternateKey key) => this.TryGetValue(key, out _);

        public bool TryAdd(TAlternateKey key, TValue value) => this.TryAdd(key, value, false, out _);

        private bool TryAdd(TAlternateKey key, TValue value, bool updateIfExists, out TValue resultingValue)
        {
            if (key is null)
                ThrowHelper.ThrowKeyNullException();

            var tables = this.Dictionary.tables;
            var comparer = GetAlternateComparer<TAlternateKey>(tables);

            var hashcode = comparer.GetHashCode(key!);

            while (true)
            {
                var locks = tables.locks;
                ref var bucket = ref GetBucketAndLock(tables, hashcode, out var lockNo);

                var resizeDesired = false;
                var lockTaken = false;

                try
                {
                    Monitor.Enter(locks[lockNo], ref lockTaken);

                    if (tables != this.Dictionary.tables)
                    {
                        tables = this.Dictionary.tables;

                        if (!ReferenceEquals(comparer, tables.comparer))
                        {
                            comparer = GetAlternateComparer<TAlternateKey>(tables);
                            hashcode = comparer.GetHashCode(key!);
                        }

                        continue;
                    }

                    Node? prev = null;

                    for (var node = bucket; node is not null; node = node.next)
                    {
                        Debug.Assert((prev is null && node == bucket) || prev!.next == node);

                        if (hashcode == node.hashcode && comparer.Equals(key!, node.key))
                        {
                            if (updateIfExists)
                            {
                                if (!typeof(TValue).IsValueType || ConcurrentDictionaryTypeProps<TValue>.isWriteAtomic)
                                    node.value = value;
                                else
                                {
                                    var newNode = new Node(node.key, value, hashcode, node.next);

                                    if (prev is null)
                                        Volatile.Write(ref bucket, newNode);
                                    else
                                        prev.next = newNode;
                                }

                                resultingValue = value;
                            }
                            else
                                resultingValue = node.value;

                            return false;
                        }

                        prev = node;

                        if (!typeof(TKey).IsValueType) { }
                    }

                    var actualKey = comparer.Create(key!);

                    if (actualKey is null)
                        ThrowHelper.ThrowKeyNullException();

                    var resultNode = new Node(actualKey!, value, hashcode, bucket);
                    Volatile.Write(ref bucket, resultNode);

                    checked
                    {
                        tables.countPerLock[lockNo]++;
                    }

                    if (tables.countPerLock[lockNo] > this.Dictionary.budget)
                        resizeDesired = true;
                }
                finally
                {
                    if (lockTaken)
                        Monitor.Exit(locks[lockNo]);
                }

                if (resizeDesired)
                    this.Dictionary.GrowTable(tables, resizeDesired);

                resultingValue = value;

                return true;
            }
        }

        public bool TryGetValue(TAlternateKey key, [MaybeNullWhen(false)] out TValue value) => this.TryGetValue(key, out _, out value);

        public bool TryGetValue(TAlternateKey key, [MaybeNullWhen(false)] out TKey actualKey, [MaybeNullWhen(false)] out TValue value)
        {
            if (key is null)
                ThrowHelper.ThrowKeyNullException();

            var tables = this.Dictionary.tables;
            var comparer = GetAlternateComparer<TAlternateKey>(tables);

            var hashcode = comparer.GetHashCode(key!);

            for (var n = GetBucket(tables, hashcode); n is not null; n = n.next)
            {
                if (hashcode == n.hashcode && comparer.Equals(key!, n.key))
                {
                    actualKey = n.key;
                    value = n.value;

                    return true;
                }
            }

            actualKey = default;
            value = default;

            return false;
        }

        public bool TryRemove(TAlternateKey key, Func<TKey, TValue, bool>? predicate, [MaybeNullWhen(false)] out TValue value) =>
            this.TryRemove(key, predicate, out _, out value);

        public bool TryRemove(
            TAlternateKey key,
            Func<TKey, TValue, bool>? predicate,
            [MaybeNullWhen(false)] out TKey actualKey,
            [MaybeNullWhen(false)] out TValue value)
        {
            if (key is null)
                ThrowHelper.ThrowKeyNullException();

            var tables = this.Dictionary.tables;
            var comparer = GetAlternateComparer<TAlternateKey>(tables);
            var hashcode = comparer.GetHashCode(key!);

            while (true)
            {
                var locks = tables.locks;
                ref var bucket = ref GetBucketAndLock(tables, hashcode, out var lockNo);

                if (tables.countPerLock[lockNo] != 0)
                {
                    lock (locks[lockNo])
                    {
                        if (tables != this.Dictionary.tables)
                        {
                            tables = this.Dictionary.tables;

                            if (!ReferenceEquals(comparer, tables.comparer))
                            {
                                comparer = GetAlternateComparer<TAlternateKey>(tables);
                                hashcode = comparer.GetHashCode(key!);
                            }

                            continue;
                        }

                        Node? prev = null;

                        for (var curr = bucket; curr is not null; curr = curr.next)
                        {
                            Debug.Assert((prev is null && curr == bucket) || prev!.next == curr);

                            if (hashcode == curr.hashcode && comparer.Equals(key!, curr.key))
                            {
                                if (predicate != null && !predicate(curr.key, curr.value))
                                {
                                    actualKey = default;
                                    value = default;

                                    return false;
                                }

                                if (prev is null)
                                    Volatile.Write(ref bucket, curr.next);
                                else
                                    prev.next = curr.next;

                                actualKey = curr.key;
                                value = curr.value;
                                tables.countPerLock[lockNo]--;

                                return true;
                            }

                            prev = curr;
                        }
                    }
                }

                actualKey = default;
                value = default;

                return false;
            }
        }
    }

    #region IDictionary<TKey,TValue> members

    void IDictionary<TKey, TValue>.Add(TKey key, TValue value)
    {
        if (!this.TryAdd(key, value))
            throw new ArgumentException(Sr.concurrentDictionaryKeyAlreadyExisted);
    }

    bool IDictionary<TKey, TValue>.Remove(TKey key) => this.TryRemove(key, out _);

    public ICollection<TKey> Keys => this.GetKeys();

    IEnumerable<TKey> IReadOnlyDictionary<TKey, TValue>.Keys => this.GetKeys();

    public ICollection<TValue> Values => this.GetValues();

    IEnumerable<TValue> IReadOnlyDictionary<TKey, TValue>.Values => this.GetValues();

    #endregion

    #region ICollection<KeyValuePair<TKey,TValue>> Members

    void ICollection<KeyValuePair<TKey, TValue>>.Add(KeyValuePair<TKey, TValue> keyValuePair) =>
        ((IDictionary<TKey, TValue>)this).Add(keyValuePair.Key, keyValuePair.Value);

    bool ICollection<KeyValuePair<TKey, TValue>>.Contains(KeyValuePair<TKey, TValue> keyValuePair) =>
        this.TryGetValue(keyValuePair.Key, out var value) && EqualityComparer<TValue>.Default.Equals(value, keyValuePair.Value);

    bool ICollection<KeyValuePair<TKey, TValue>>.IsReadOnly => false;

    bool ICollection<KeyValuePair<TKey, TValue>>.Remove(KeyValuePair<TKey, TValue> keyValuePair) => this.TryRemove(keyValuePair);

    #endregion

    #region IDictionary Members

    void IDictionary.Add(object key, object? value)
    {
        if (key is null)
            ThrowHelper.ThrowKeyNullException();

        if (!(key is TKey))
            throw new ArgumentException(Sr.concurrentDictionaryTypeOfKeyIncorrect);

        ThrowIfInvalidObjectValue(value);

        ((IDictionary<TKey, TValue>)this).Add((TKey)key, (TValue)value!);
    }

    bool IDictionary.Contains(object key)
    {
        if (key is null)
            ThrowHelper.ThrowKeyNullException();

        return key is TKey tkey && this.ContainsKey(tkey);
    }

    IDictionaryEnumerator IDictionary.GetEnumerator() => new DictionaryEnumerator(this);

    bool IDictionary.IsFixedSize => false;

    bool IDictionary.IsReadOnly => false;

    ICollection IDictionary.Keys => this.GetKeys();

    void IDictionary.Remove(object key)
    {
        if (key is null)
            ThrowHelper.ThrowKeyNullException();

        if (key is TKey tkey)
            this.TryRemove(tkey, out _);
    }

    ICollection IDictionary.Values => this.GetValues();

    object? IDictionary.this[object key]
    {
        get
        {
            if (key is null)
                ThrowHelper.ThrowKeyNullException();

            if (key is TKey tkey && this.TryGetValue(tkey, out var value))
                return value;

            return null;
        }
        set
        {
            if (key is null)
                ThrowHelper.ThrowKeyNullException();

            if (!(key is TKey))
                throw new ArgumentException(Sr.concurrentDictionaryTypeOfKeyIncorrect);

            ThrowIfInvalidObjectValue(value);

            this[(TKey)key] = (TValue)value!;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ThrowIfInvalidObjectValue(object? value)
    {
        if (value is not null)
        {
            if (!(value is TValue))
                ThrowHelper.ThrowValueNullException();
        }
        else if (default(TValue) is not null)
            ThrowHelper.ThrowValueNullException();
    }

    #endregion

    #region ICollection Members

    void ICollection.CopyTo(Array array, int index)
    {
        array.Required();
        ArgumentOutOfRangeException.ThrowIfNegative(index);

        var locksAcquired = 0;

        try
        {
            this.AcquireAllLocks(ref locksAcquired);

            var count = this.GetCountNoLocks();

            if (array.Length - count < index)
                throw new ArgumentException(Sr.concurrentDictionaryArrayNotLargeEnough);

            if (array is KeyValuePair<TKey, TValue>[] pairs)
            {
                this.CopyToPairs(pairs, index);

                return;
            }

            if (array is DictionaryEntry[] entries)
            {
                this.CopyToEntries(entries, index);

                return;
            }

            if (array is object[] objects)
            {
                this.CopyToObjects(objects, index);

                return;
            }

            throw new ArgumentException(Sr.concurrentDictionaryArrayIncorrectType, nameof(array));
        }
        finally
        {
            this.ReleaseLocks(locksAcquired);
        }
    }

    bool ICollection.IsSynchronized => false;

    object ICollection.SyncRoot => throw new NotSupportedException(Sr.concurrentCollectionSyncRootNotSupported);

    #endregion
}

internal static class ConcurrentDictionaryTypeProps<T>
{
    internal static readonly bool isWriteAtomic = IsWriteAtomicPrivate();

    private static bool IsWriteAtomicPrivate()
    {
        if (!typeof(T).IsValueType || typeof(T) == typeof(nint) || typeof(T) == typeof(nuint))
            return true;

        switch (Type.GetTypeCode(typeof(T)))
        {
            case TypeCode.Boolean:
            case TypeCode.Byte:
            case TypeCode.Char:
            case TypeCode.Int16:
            case TypeCode.Int32:
            case TypeCode.SByte:
            case TypeCode.Single:
            case TypeCode.UInt16:
            case TypeCode.UInt32:
                return true;

            case TypeCode.Double:
            case TypeCode.Int64:
            case TypeCode.UInt64:
                return nint.Size == 8;

            default:
                return false;
        }
    }
}

internal sealed class DictionaryDebugView<TKey, TValue> where TKey : notnull
{
    private readonly IDictionary<TKey, TValue> dictionary;

    public DictionaryDebugView(IDictionary<TKey, TValue> dictionary)
    {
        if (dictionary is null)
            ThrowHelper.ThrowArgumentNullException(nameof(dictionary));

        this.dictionary = dictionary!;
    }

    [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
    public DebugViewDictionaryItem<TKey, TValue>[] Items
    {
        get
        {
            var keyValuePairs = new KeyValuePair<TKey, TValue>[this.dictionary.Count];
            this.dictionary.CopyTo(keyValuePairs, 0);
            var items = new DebugViewDictionaryItem<TKey, TValue>[keyValuePairs.Length];

            for (var i = 0; i < items.Length; i++)
                items[i] = new(keyValuePairs[i]);

            return items;
        }
    }
}
