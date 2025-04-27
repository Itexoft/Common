// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Collections;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Itexoft.DotNET;

namespace Itexoft.Collections;

[DebuggerTypeProxy(typeof(IDictionaryDebugView<,>))]
[DebuggerDisplay("Count = {Count}")]
public class ConcurrentDictionaryEx<TKey, TValue> : IDictionary<TKey, TValue>, IDictionary, IReadOnlyDictionary<TKey, TValue>
    where TKey : notnull
{
    private const int DefaultCapacity = 31;


    private const int MaxLockNumber = 1024;

    private readonly bool _comparerIsDefaultForClasses;

    private readonly bool _growLockArray;

    private readonly int _initialCapacity;

    private int _budget;

    private volatile Tables _tables;


    public ConcurrentDictionaryEx()
        : this(DefaultConcurrencyLevel, DefaultCapacity, true, null) { }


    public ConcurrentDictionaryEx(int concurrencyLevel, int capacity)
        : this(concurrencyLevel, capacity, false, null) { }


    public ConcurrentDictionaryEx(IEnumerable<KeyValuePair<TKey, TValue>> collection)
        : this(DefaultConcurrencyLevel, collection, null) { }


    public ConcurrentDictionaryEx(IEqualityComparer<TKey>? comparer)
        : this(DefaultConcurrencyLevel, DefaultCapacity, true, comparer) { }


    public ConcurrentDictionaryEx(IEnumerable<KeyValuePair<TKey, TValue>> collection, IEqualityComparer<TKey>? comparer)
        : this(DefaultConcurrencyLevel, GetCapacityFromCollection(collection), comparer)
    {
        ArgumentNullException.ThrowIfNull(collection);

        this.InitializeFromCollection(collection);
    }


    public ConcurrentDictionaryEx(
        int concurrencyLevel,
        IEnumerable<KeyValuePair<TKey, TValue>> collection,
        IEqualityComparer<TKey>? comparer)
        : this(concurrencyLevel, GetCapacityFromCollection(collection), false, comparer)
    {
        ArgumentNullException.ThrowIfNull(collection);

        this.InitializeFromCollection(collection);
    }


    public ConcurrentDictionaryEx(int concurrencyLevel, int capacity, IEqualityComparer<TKey>? comparer)
        : this(concurrencyLevel, capacity, false, comparer) { }

    internal ConcurrentDictionaryEx(int concurrencyLevel, int capacity, bool growLockArray, IEqualityComparer<TKey>? comparer)
    {
        if (concurrencyLevel <= 0)
        {
            if (concurrencyLevel != -1)
                throw new ArgumentOutOfRangeException(
                    nameof(concurrencyLevel),
                    SR.ConcurrentDictionary_ConcurrencyLevelMustBePositiveOrNegativeOne);

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
                this._comparerIsDefaultForClasses = true;
        }

        this._tables = new(buckets, locks, countPerLock, comparer);
        this._growLockArray = growLockArray;
        this._initialCapacity = capacity;
        this._budget = buckets.Length / locks.Length;
    }


    public IEqualityComparer<TKey> Comparer
    {
        get
        {
            var comparer = this._tables._comparer;

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

            this.TryAddInternal(this._tables, key!, null, value, true, true, out _);
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

        var tables = this._tables;

        var comparer = tables._comparer;
        if (typeof(TKey).IsValueType && comparer is null)
        {
            var hashcode = key!.GetHashCode();
            for (var n = GetBucket(tables, hashcode); n is not null; n = n._next)
                if (hashcode == n._hashcode && EqualityComparer<TKey>.Default.Equals(n._key, key))
                {
                    value = n._value;

                    return true;
                }
        }
        else
        {
            Debug.Assert(comparer is not null);
            var hashcode = this.GetHashCode(comparer, key!);
            for (var n = GetBucket(tables, hashcode); n is not null; n = n._next)
                if (hashcode == n._hashcode && comparer.Equals(n._key, key!))
                {
                    value = n._value;

                    return true;
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

            var tables = this._tables;
            var newTables = new Tables(
                new VolatileNode[HashHelpers.GetPrime(this._initialCapacity)],
                tables._locks,
                new int[tables._countPerLock.Length],
                tables._comparer);
            this._tables = newTables;
            this._budget = Math.Max(1, newTables._buckets.Length / newTables._locks.Length);
        }
        finally
        {
            this.ReleaseLocks(locksAcquired);
        }
    }


    void ICollection<KeyValuePair<TKey, TValue>>.CopyTo(KeyValuePair<TKey, TValue>[] array, int index)
    {
        ArgumentNullException.ThrowIfNull(array);

        ArgumentOutOfRangeException.ThrowIfNegative(index);

        var locksAcquired = 0;
        try
        {
            this.AcquireAllLocks(ref locksAcquired);

            var count = this.GetCountNoLocks();

            if (array.Length - count < index)
                throw new ArgumentException(SR.ConcurrentDictionary_ArrayNotLargeEnough);

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
        if (!IsCompatibleKey<TAlternateKey>(this._tables))
            ThrowHelper.ThrowIncompatibleComparer();

        return new(this);
    }


    public bool TryGetAlternateLookup<TAlternateKey>(out AlternateLookup<TAlternateKey> lookup)
        where TAlternateKey : notnull, allows ref struct
    {
        if (IsCompatibleKey<TAlternateKey>(this._tables))
        {
            lookup = new(this);

            return true;
        }

        lookup = default;

        return false;
    }


    private static int GetCapacityFromCollection(IEnumerable<KeyValuePair<TKey, TValue>> collection)
    {
        return collection switch
        {
            ICollection<KeyValuePair<TKey, TValue>> c => Math.Max(DefaultCapacity, c.Count),
            IReadOnlyCollection<KeyValuePair<TKey, TValue>> rc => Math.Max(DefaultCapacity, rc.Count),
            _ => DefaultCapacity
        };
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int GetHashCode(IEqualityComparer<TKey>? comparer, TKey key)
    {
        if (typeof(TKey).IsValueType)
            return comparer is null ? key.GetHashCode() : comparer.GetHashCode(key);

        Debug.Assert(comparer is not null);

        return this._comparerIsDefaultForClasses ? key.GetHashCode() : comparer.GetHashCode(key);
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool NodeEqualsKey(IEqualityComparer<TKey>? comparer, Node node, TKey key)
    {
        if (typeof(TKey).IsValueType)
            return comparer is null ? EqualityComparer<TKey>.Default.Equals(node._key, key) : comparer.Equals(node._key, key);

        Debug.Assert(comparer is not null);

        return comparer.Equals(node._key, key);
    }

    private void InitializeFromCollection(IEnumerable<KeyValuePair<TKey, TValue>> collection)
    {
        foreach (var pair in collection)
        {
            if (pair.Key is null)
                ThrowHelper.ThrowKeyNullException();

            if (!this.TryAddInternal(this._tables, pair.Key!, null, pair.Value, false, false, out _))
                throw new ArgumentException(SR.ConcurrentDictionary_SourceContainsDuplicateKeys);
        }

        if (this._budget == 0)
        {
            var tables = this._tables;
            this._budget = tables._buckets.Length / tables._locks.Length;
        }
    }


    public bool TryAdd(TKey key, TValue value)
    {
        if (key is null)
            ThrowHelper.ThrowKeyNullException();

        return this.TryAddInternal(this._tables, key!, null, value, false, true, out _);
    }

    public bool TryAdd(TKey key, Func<TKey, TValue> valueFactory)
    {
        if (key is null)
            ThrowHelper.ThrowKeyNullException();

        return this.TryAddInternal(this._tables, key!, null, valueFactory, false, true, out _);
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
            ThrowHelper.ThrowArgumentNullException(nameof(item), SR.ConcurrentDictionary_ItemKeyIsNull);

        return this.TryRemoveInternal(item.Key!, predicate, out _, true, item.Value);
    }


    private bool TryRemoveInternal(
        TKey key,
        Func<TKey, TValue, bool>? predicate,
        [MaybeNullWhen(false)] out TValue value,
        bool matchValue,
        TValue? oldValue)
    {
        var tables = this._tables;

        var comparer = tables._comparer;
        var hashcode = this.GetHashCode(comparer, key);

        while (true)
        {
            var locks = tables._locks;
            ref var bucket = ref GetBucketAndLock(tables, hashcode, out var lockNo);

            lock (locks[lockNo])
            {
                if (tables != this._tables)
                {
                    tables = this._tables;
                    if (!ReferenceEquals(comparer, tables._comparer))
                    {
                        comparer = tables._comparer;
                        hashcode = this.GetHashCode(comparer, key);
                    }

                    continue;
                }

                Node? prev = null;
                for (var curr = bucket; curr is not null; curr = curr._next)
                {
                    Debug.Assert((prev is null && curr == bucket) || prev!._next == curr);

                    if (hashcode == curr._hashcode && NodeEqualsKey(comparer, curr, key))
                    {
                        if (matchValue)
                        {
                            var valuesMatch = EqualityComparer<TValue>.Default.Equals(oldValue, curr._value);
                            if (!valuesMatch)
                            {
                                value = default;

                                return false;
                            }
                        }

                        if (predicate != null && !predicate(key, curr._value))
                        {
                            value = default;

                            return false;
                        }

                        if (prev is null)
                            Volatile.Write(ref bucket, curr._next);
                        else
                            prev._next = curr._next;

                        value = curr._value;
                        tables._countPerLock[lockNo]--;

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
        var comparer = tables._comparer;

        if (typeof(TKey).IsValueType && comparer is null)
        {
            for (var n = GetBucket(tables, hashcode); n is not null; n = n._next)
                if (hashcode == n._hashcode && EqualityComparer<TKey>.Default.Equals(n._key, key))
                {
                    value = n._value;

                    return true;
                }
        }
        else
        {
            Debug.Assert(comparer is not null);
            for (var n = GetBucket(tables, hashcode); n is not null; n = n._next)
                if (hashcode == n._hashcode && comparer.Equals(n._key, key))
                {
                    value = n._value;

                    return true;
                }
        }

        value = default;

        return false;
    }


    public bool TryUpdate(TKey key, TValue newValue, TValue comparisonValue)
    {
        if (key is null)
            ThrowHelper.ThrowKeyNullException();

        return this.TryUpdateInternal(this._tables, key!, null, newValue, comparisonValue);
    }


    private bool TryUpdateInternal(Tables tables, TKey key, int? nullableHashcode, TValue newValue, TValue comparisonValue)
    {
        var comparer = tables._comparer;

        var hashcode = nullableHashcode ?? this.GetHashCode(comparer, key);
        Debug.Assert(nullableHashcode is null || nullableHashcode == hashcode);

        var valueComparer = EqualityComparer<TValue>.Default;

        while (true)
        {
            var locks = tables._locks;
            ref var bucket = ref GetBucketAndLock(tables, hashcode, out var lockNo);

            lock (locks[lockNo])
            {
                if (tables != this._tables)
                {
                    tables = this._tables;
                    if (!ReferenceEquals(comparer, tables._comparer))
                    {
                        comparer = tables._comparer;
                        hashcode = this.GetHashCode(comparer, key);
                    }

                    continue;
                }


                Node? prev = null;
                for (var node = bucket; node is not null; node = node._next)
                {
                    Debug.Assert((prev is null && node == bucket) || prev!._next == node);
                    if (hashcode == node._hashcode && NodeEqualsKey(comparer, node, key))
                    {
                        if (valueComparer.Equals(node._value, comparisonValue))
                        {
                            if (!typeof(TValue).IsValueType || ConcurrentDictionaryTypeProps<TValue>.IsWriteAtomic)
                            {
                                node._value = newValue;
                            }
                            else
                            {
                                var newNode = new Node(node._key, newValue, hashcode, node._next);

                                if (prev is null)
                                    Volatile.Write(ref bucket, newNode);
                                else
                                    prev._next = newNode;
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
        foreach (var bucket in this._tables._buckets)
            for (var current = bucket._node; current is not null; current = current._next)
            {
                array[index] = new(current._key, current._value);
                Debug.Assert(index < int.MaxValue, "This method should only be called when there's no overflow risk");
                index++;
            }
    }


    private void CopyToEntries(DictionaryEntry[] array, int index)
    {
        foreach (var bucket in this._tables._buckets)
            for (var current = bucket._node; current is not null; current = current._next)
            {
                array[index] = new(current._key, current._value);
                Debug.Assert(index < int.MaxValue, "This method should only be called when there's no overflow risk");
                index++;
            }
    }


    private void CopyToObjects(object[] array, int index)
    {
        foreach (var bucket in this._tables._buckets)
            for (var current = bucket._node; current is not null; current = current._next)
            {
                array[index] = new KeyValuePair<TKey, TValue>(current._key, current._value);
                Debug.Assert(index < int.MaxValue, "This method should only be called when there's no overflow risk");
                index++;
            }
    }


    private bool TryAddInternal(
        Tables tables,
        TKey key,
        int? nullableHashcode,
        TValue value,
        bool updateIfExists,
        bool acquireLock,
        out TValue resultingValue)
    {
        return this.TryAddInternal<object>(
            tables,
            key,
            nullableHashcode,
            (_, _) => value,
            updateIfExists,
            acquireLock,
            null!,
            out resultingValue);
    }

    private bool TryAddInternal(
        Tables tables,
        TKey key,
        int? nullableHashcode,
        Func<TKey, TValue> getValue,
        bool updateIfExists,
        bool acquireLock,
        out TValue resultingValue)
    {
        return this.TryAddInternal<object>(
            tables,
            key,
            nullableHashcode,
            (k, _) => getValue(k),
            updateIfExists,
            acquireLock,
            null!,
            out resultingValue);
    }

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
        var comparer = tables._comparer;

        var hashcode = nullableHashcode ?? this.GetHashCode(comparer, key);
        Debug.Assert(nullableHashcode is null || nullableHashcode == hashcode);

        while (true)
        {
            var locks = tables._locks;
            ref var bucket = ref GetBucketAndLock(tables, hashcode, out var lockNo);

            var resizeDesired = false;
            var lockTaken = false;
            try
            {
                if (acquireLock)
                    Monitor.Enter(locks[lockNo], ref lockTaken);


                if (tables != this._tables)
                {
                    tables = this._tables;
                    if (!ReferenceEquals(comparer, tables._comparer))
                    {
                        comparer = tables._comparer;
                        hashcode = this.GetHashCode(comparer, key);
                    }

                    continue;
                }


                Node? prev = null;
                for (var node = bucket; node is not null; node = node._next)
                {
                    Debug.Assert((prev is null && node == bucket) || prev!._next == node);
                    if (hashcode == node._hashcode && NodeEqualsKey(comparer, node, key))
                    {
                        if (updateIfExists)
                        {
                            resultingValue = getValue(key, factoryArg);

                            if (!typeof(TValue).IsValueType || ConcurrentDictionaryTypeProps<TValue>.IsWriteAtomic)
                            {
                                node._value = resultingValue;
                            }
                            else
                            {
                                var newNode = new Node(node._key, resultingValue, hashcode, node._next);
                                if (prev is null)
                                    Volatile.Write(ref bucket, newNode);
                                else
                                    prev._next = newNode;
                            }
                        }
                        else
                        {
                            resultingValue = node._value;
                        }

                        return false;
                    }

                    prev = node;
                }


                resultingValue = getValue(key, factoryArg);

                var resultNode = new Node(key, resultingValue, hashcode, bucket);
                Volatile.Write(ref bucket, resultNode);
                checked
                {
                    tables._countPerLock[lockNo]++;
                }


                if (tables._countPerLock[lockNo] > this._budget)
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
        throw new KeyNotFoundException(SR.Format(SR.Arg_KeyNotFoundWithKey, key.ToString()));


    private int GetCountNoLocks()
    {
        var count = 0;
        foreach (var value in this._tables._countPerLock)
            checked
            {
                count += value;
            }

        return count;
    }


    public TValue GetOrAdd(TKey key, Func<TKey, TValue> valueFactory)
    {
        if (key is null)
            ThrowHelper.ThrowKeyNullException();

        if (valueFactory is null)
            ThrowHelper.ThrowArgumentNullException(nameof(valueFactory));

        var tables = this._tables;

        var comparer = tables._comparer;
        var hashcode = this.GetHashCode(comparer, key!);

        if (!TryGetValueInternal(tables, key!, hashcode, out var resultingValue))
            this.TryAddInternal(tables, key!, hashcode, valueFactory!(key!), false, true, out resultingValue);

        return resultingValue;
    }


    public TValue GetOrAdd<TArg>(TKey key, Func<TKey, TArg, TValue> valueFactory, TArg factoryArgument)
        where TArg : allows ref struct
    {
        if (key is null)
            ThrowHelper.ThrowKeyNullException();

        ArgumentNullException.ThrowIfNull(valueFactory);

        var tables = this._tables;

        var comparer = tables._comparer;
        var hashcode = this.GetHashCode(comparer, key!);

        if (!TryGetValueInternal(tables, key!, hashcode, out var resultingValue))
            this.TryAddInternal(tables, key!, hashcode, valueFactory, false, true, factoryArgument, out resultingValue);

        return resultingValue;
    }


    public TValue GetOrAdd(TKey key, TValue value)
    {
        if (key is null)
            ThrowHelper.ThrowKeyNullException();

        var tables = this._tables;

        var comparer = tables._comparer;
        var hashcode = this.GetHashCode(comparer, key!);

        if (!TryGetValueInternal(tables, key!, hashcode, out var resultingValue))
            this.TryAddInternal(tables, key!, hashcode, value, false, true, out resultingValue);

        return resultingValue;
    }


    public TValue AddOrUpdate<TArg>(
        TKey key,
        Func<TKey, TArg, TValue> addValueFactory,
        Func<TKey, TValue, TArg, TValue> updateValueFactory,
        TArg factoryArgument)
        where TArg : allows ref struct
    {
        if (key is null)
            ThrowHelper.ThrowKeyNullException();

        if (addValueFactory is null)
            ThrowHelper.ThrowArgumentNullException(nameof(addValueFactory));

        if (updateValueFactory is null)
            ThrowHelper.ThrowArgumentNullException(nameof(updateValueFactory));

        var tables = this._tables;

        var comparer = tables._comparer;
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
                if (this.TryAddInternal(
                        tables,
                        key!,
                        hashcode,
                        addValueFactory!(key!, factoryArgument),
                        false,
                        true,
                        out var resultingValue))
                    return resultingValue;
            }

            if (tables != this._tables)
            {
                tables = this._tables;
                if (!ReferenceEquals(comparer, tables._comparer))
                {
                    comparer = tables._comparer;
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

        var tables = this._tables;

        var comparer = tables._comparer;
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

            if (tables != this._tables)
            {
                tables = this._tables;
                if (!ReferenceEquals(comparer, tables._comparer))
                {
                    comparer = tables._comparer;
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

        var tables = this._tables;

        var comparer = tables._comparer;
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

            if (tables != this._tables)
            {
                tables = this._tables;
                if (!ReferenceEquals(comparer, tables._comparer))
                {
                    comparer = tables._comparer;
                    hashcode = this.GetHashCode(comparer, key!);
                }
            }
        }
    }

    private bool AreAllBucketsEmpty() => !this._tables._countPerLock.AsSpan().ContainsAnyExcept(0);


    private void GrowTable(Tables tables, bool resizeDesired)
    {
        var locksAcquired = 0;
        try
        {
            this.AcquireFirstLock(ref locksAcquired);


            if (tables != this._tables)
                return;

            var newLength = tables._buckets.Length;

            if (resizeDesired)
            {
                if (this.GetCountNoLocks() < tables._buckets.Length / 4)
                {
                    this._budget = 2 * this._budget;
                    if (this._budget < 0)
                        this._budget = int.MaxValue;

                    return;
                }


                if ((newLength = tables._buckets.Length * 2) < 0 || (newLength = HashHelpers.GetPrime(newLength)) > Array.MaxLength)
                {
                    newLength = Array.MaxLength;


                    this._budget = int.MaxValue;
                }
            }

            var newLocks = tables._locks;


            if (this._growLockArray && tables._locks.Length < MaxLockNumber)
            {
                newLocks = new object[tables._locks.Length * 2];
                Array.Copy(tables._locks, newLocks, tables._locks.Length);
                for (var i = tables._locks.Length; i < newLocks.Length; i++)
                    newLocks[i] = new();
            }

            var newBuckets = new VolatileNode[newLength];
            var newCountPerLock = new int[newLocks.Length];
            var newTables = new Tables(newBuckets, newLocks, newCountPerLock, tables._comparer);


            AcquirePostFirstLock(tables, ref locksAcquired);


            foreach (var bucket in tables._buckets)
            {
                var current = bucket._node;
                while (current is not null)
                {
                    var hashCode = current._hashcode;

                    var next = current._next;
                    ref var newBucket = ref GetBucketAndLock(newTables, hashCode, out var newLockNo);

                    newBucket = new(current._key, current._value, hashCode, newBucket);

                    checked
                    {
                        newCountPerLock[newLockNo]++;
                    }

                    current = next;
                }
            }


            this._budget = Math.Max(1, newBuckets.Length / newLocks.Length);


            this._tables = newTables;
        }
        finally
        {
            this.ReleaseLocks(locksAcquired);
        }
    }


    private void AcquireAllLocks(ref int locksAcquired)
    {
        this.AcquireFirstLock(ref locksAcquired);
        AcquirePostFirstLock(this._tables, ref locksAcquired);
        Debug.Assert(locksAcquired == this._tables._locks.Length);
    }


    private void AcquireFirstLock(ref int locksAcquired)
    {
        var locks = this._tables._locks;
        Debug.Assert(locksAcquired == 0);
        Debug.Assert(!Monitor.IsEntered(locks[0]));

        Monitor.Enter(locks[0]);
        locksAcquired = 1;
    }


    private static void AcquirePostFirstLock(Tables tables, ref int locksAcquired)
    {
        var locks = tables._locks;
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

        var locks = this._tables._locks;
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
            foreach (var bucket in this._tables._buckets)
                for (var node = bucket._node; node is not null; node = node._next)
                {
                    keys[i] = node._key;
                    i++;
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
            foreach (var bucket in this._tables._buckets)
                for (var node = bucket._node; node is not null; node = node._next)
                {
                    keys[i] = node._value;
                    i++;
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
        var buckets = tables._buckets;

        if (nint.Size == 8)
            return buckets[HashHelpers.FastMod((uint)hashcode, (uint)buckets.Length, tables._fastModBucketsMultiplier)]._node;

        return buckets[(uint)hashcode % (uint)buckets.Length]._node;
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ref Node? GetBucketAndLock(Tables tables, int hashcode, out uint lockNo)
    {
        var buckets = tables._buckets;
        uint bucketNo;
        if (nint.Size == 8)
            bucketNo = HashHelpers.FastMod((uint)hashcode, (uint)buckets.Length, tables._fastModBucketsMultiplier);
        else
            bucketNo = (uint)hashcode % (uint)buckets.Length;
        lockNo = bucketNo % (uint)tables._locks.Length;

        return ref buckets[bucketNo]._node;
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsCompatibleKey<TAlternateKey>(Tables tables)
        where TAlternateKey : notnull, allows ref struct
    {
        Debug.Assert(tables is not null);

        return tables._comparer is IAlternateEqualityComparer<TAlternateKey, TKey>;
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static IAlternateEqualityComparer<TAlternateKey, TKey> GetAlternateComparer<TAlternateKey>(Tables tables)
        where TAlternateKey : notnull, allows ref struct
    {
        Debug.Assert(IsCompatibleKey<TAlternateKey>(tables));

        return Unsafe.As<IAlternateEqualityComparer<TAlternateKey, TKey>>(tables._comparer!);
    }


    private sealed class Enumerator : IEnumerator<KeyValuePair<TKey, TValue>>
    {
        private const int StateUninitialized = 0;
        private const int StateOuterloop = 1;
        private const int StateInnerLoop = 2;
        private const int StateDone = 3;


        private readonly ConcurrentDictionaryEx<TKey, TValue> _dictionary;

        private VolatileNode[]? _buckets;
        private int _i;
        private Node? _node;
        private int _state;

        public Enumerator(ConcurrentDictionaryEx<TKey, TValue> dictionary)
        {
            this._dictionary = dictionary;
            this._i = -1;
        }

        object IEnumerator.Current => this.Current;

        public KeyValuePair<TKey, TValue> Current { get; private set; }

        public void Reset()
        {
            this._buckets = null;
            this._node = null;
            this.Current = default;
            this._i = -1;
            this._state = StateUninitialized;
        }

        public void Dispose() { }

        public bool MoveNext()
        {
            switch (this._state)
            {
                case StateUninitialized:
                    this._buckets = this._dictionary._tables._buckets;
                    this._i = -1;
                    goto case StateOuterloop;

                case StateOuterloop:
                    var buckets = this._buckets;
                    Debug.Assert(buckets is not null);

                    var i = ++this._i;
                    if ((uint)i < (uint)buckets.Length)
                    {
                        this._node = buckets[i]._node;
                        this._state = StateInnerLoop;
                        goto case StateInnerLoop;
                    }

                    goto default;

                case StateInnerLoop:
                    if (this._node is Node node)
                    {
                        this.Current = new(node._key, node._value);
                        this._node = node._next;

                        return true;
                    }

                    goto case StateOuterloop;

                default:
                    this._state = StateDone;

                    return false;
            }
        }
    }

    private struct VolatileNode
    {
        internal volatile Node? _node;
    }


    private sealed class Node
    {
        internal readonly int _hashcode;
        internal readonly TKey _key;
        internal volatile Node? _next;
        internal TValue _value;

        internal Node(TKey key, TValue value, int hashcode, Node? next)
        {
            this._key = key;
            this._value = value;
            this._next = next;
            this._hashcode = hashcode;
        }
    }


    private sealed class Tables
    {
        internal readonly VolatileNode[] _buckets;

        internal readonly IEqualityComparer<TKey>? _comparer;

        internal readonly int[] _countPerLock;

        internal readonly ulong _fastModBucketsMultiplier;

        internal readonly object[] _locks;

        internal Tables(VolatileNode[] buckets, object[] locks, int[] countPerLock, IEqualityComparer<TKey>? comparer)
        {
            Debug.Assert(typeof(TKey).IsValueType || comparer is not null);

            this._buckets = buckets;
            this._locks = locks;
            this._countPerLock = countPerLock;
            this._comparer = comparer;
            if (nint.Size == 8)
                this._fastModBucketsMultiplier = HashHelpers.GetFastModMultiplier((uint)buckets.Length);
        }
    }


    private sealed class DictionaryEnumerator : IDictionaryEnumerator
    {
        private readonly IEnumerator<KeyValuePair<TKey, TValue>> _enumerator;

        internal DictionaryEnumerator(ConcurrentDictionaryEx<TKey, TValue> dictionary) => this._enumerator = dictionary.GetEnumerator();

        public DictionaryEntry Entry => new(this._enumerator.Current.Key, this._enumerator.Current.Value);

        public object Key => this._enumerator.Current.Key;

        public object? Value => this._enumerator.Current.Value;

        public object Current => this.Entry;

        public bool MoveNext() => this._enumerator.MoveNext();

        public void Reset() => this._enumerator.Reset();
    }


    public readonly struct AlternateLookup<TAlternateKey> where TAlternateKey : notnull, allows ref struct
    {
        internal AlternateLookup(ConcurrentDictionaryEx<TKey, TValue> dictionary)
        {
            Debug.Assert(dictionary is not null);
            Debug.Assert(IsCompatibleKey<TAlternateKey>(dictionary._tables));
            this.Dictionary = dictionary;
        }


        public ConcurrentDictionaryEx<TKey, TValue> Dictionary { get; }


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

            var tables = this.Dictionary._tables;
            var comparer = GetAlternateComparer<TAlternateKey>(tables);

            var hashcode = comparer.GetHashCode(key!);

            while (true)
            {
                var locks = tables._locks;
                ref var bucket = ref GetBucketAndLock(tables, hashcode, out var lockNo);

                var resizeDesired = false;
                var lockTaken = false;
                try
                {
                    Monitor.Enter(locks[lockNo], ref lockTaken);


                    if (tables != this.Dictionary._tables)
                    {
                        tables = this.Dictionary._tables;
                        if (!ReferenceEquals(comparer, tables._comparer))
                        {
                            comparer = GetAlternateComparer<TAlternateKey>(tables);
                            hashcode = comparer.GetHashCode(key!);
                        }

                        continue;
                    }


                    Node? prev = null;
                    for (var node = bucket; node is not null; node = node._next)
                    {
                        Debug.Assert((prev is null && node == bucket) || prev!._next == node);
                        if (hashcode == node._hashcode && comparer.Equals(key!, node._key))
                        {
                            if (updateIfExists)
                            {
                                if (!typeof(TValue).IsValueType || ConcurrentDictionaryTypeProps<TValue>.IsWriteAtomic)
                                {
                                    node._value = value;
                                }
                                else
                                {
                                    var newNode = new Node(node._key, value, hashcode, node._next);
                                    if (prev is null)
                                        Volatile.Write(ref bucket, newNode);
                                    else
                                        prev._next = newNode;
                                }

                                resultingValue = value;
                            }
                            else
                            {
                                resultingValue = node._value;
                            }

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
                        tables._countPerLock[lockNo]++;
                    }


                    if (tables._countPerLock[lockNo] > this.Dictionary._budget)
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

            var tables = this.Dictionary._tables;
            var comparer = GetAlternateComparer<TAlternateKey>(tables);

            var hashcode = comparer.GetHashCode(key!);
            for (var n = GetBucket(tables, hashcode); n is not null; n = n._next)
                if (hashcode == n._hashcode && comparer.Equals(key!, n._key))
                {
                    actualKey = n._key;
                    value = n._value;

                    return true;
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

            var tables = this.Dictionary._tables;
            var comparer = GetAlternateComparer<TAlternateKey>(tables);
            var hashcode = comparer.GetHashCode(key!);

            while (true)
            {
                var locks = tables._locks;
                ref var bucket = ref GetBucketAndLock(tables, hashcode, out var lockNo);


                if (tables._countPerLock[lockNo] != 0)
                    lock (locks[lockNo])
                    {
                        if (tables != this.Dictionary._tables)
                        {
                            tables = this.Dictionary._tables;
                            if (!ReferenceEquals(comparer, tables._comparer))
                            {
                                comparer = GetAlternateComparer<TAlternateKey>(tables);
                                hashcode = comparer.GetHashCode(key!);
                            }

                            continue;
                        }


                        Node? prev = null;
                        for (var curr = bucket; curr is not null; curr = curr._next)
                        {
                            Debug.Assert((prev is null && curr == bucket) || prev!._next == curr);

                            if (hashcode == curr._hashcode && comparer.Equals(key!, curr._key))
                            {
                                if (predicate != null && !predicate(curr._key, curr._value))
                                {
                                    actualKey = default;
                                    value = default;

                                    return false;
                                }

                                if (prev is null)
                                    Volatile.Write(ref bucket, curr._next);
                                else
                                    prev._next = curr._next;

                                actualKey = curr._key;
                                value = curr._value;
                                tables._countPerLock[lockNo]--;

                                return true;
                            }

                            prev = curr;
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
            throw new ArgumentException(SR.ConcurrentDictionary_KeyAlreadyExisted);
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
        this.TryGetValue(keyValuePair.Key, out var value)
        && EqualityComparer<TValue>.Default.Equals(value, keyValuePair.Value);


    bool ICollection<KeyValuePair<TKey, TValue>>.IsReadOnly => false;


    bool ICollection<KeyValuePair<TKey, TValue>>.Remove(KeyValuePair<TKey, TValue> keyValuePair) => this.TryRemove(keyValuePair);

    #endregion

    #region IDictionary Members

    void IDictionary.Add(object key, object? value)
    {
        if (key is null)
            ThrowHelper.ThrowKeyNullException();

        if (!(key is TKey))
            throw new ArgumentException(SR.ConcurrentDictionary_TypeOfKeyIncorrect);

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
                throw new ArgumentException(SR.ConcurrentDictionary_TypeOfKeyIncorrect);

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
        {
            ThrowHelper.ThrowValueNullException();
        }
    }

    #endregion

    #region ICollection Members

    void ICollection.CopyTo(Array array, int index)
    {
        ArgumentNullException.ThrowIfNull(array);
        ArgumentOutOfRangeException.ThrowIfNegative(index);

        var locksAcquired = 0;
        try
        {
            this.AcquireAllLocks(ref locksAcquired);

            var count = this.GetCountNoLocks();

            if (array.Length - count < index)
                throw new ArgumentException(SR.ConcurrentDictionary_ArrayNotLargeEnough);


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

            throw new ArgumentException(SR.ConcurrentDictionary_ArrayIncorrectType, nameof(array));
        }
        finally
        {
            this.ReleaseLocks(locksAcquired);
        }
    }


    bool ICollection.IsSynchronized => false;


    object ICollection.SyncRoot => throw new NotSupportedException(SR.ConcurrentCollection_SyncRoot_NotSupported);

    #endregion
}

internal static class ConcurrentDictionaryTypeProps<T>
{
    internal static readonly bool IsWriteAtomic = IsWriteAtomicPrivate();

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

internal sealed class IDictionaryDebugView<TKey, TValue> where TKey : notnull
{
    private readonly IDictionary<TKey, TValue> _dictionary;

    public IDictionaryDebugView(IDictionary<TKey, TValue> dictionary)
    {
        if (dictionary is null)
            ThrowHelper.ThrowArgumentNullException(nameof(dictionary));

        this._dictionary = dictionary!;
    }

    [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
    public DebugViewDictionaryItem<TKey, TValue>[] Items
    {
        get
        {
            var keyValuePairs = new KeyValuePair<TKey, TValue>[this._dictionary.Count];
            this._dictionary.CopyTo(keyValuePairs, 0);
            var items = new DebugViewDictionaryItem<TKey, TValue>[keyValuePairs.Length];
            for (var i = 0; i < items.Length; i++)
                items[i] = new(keyValuePairs[i]);

            return items;
        }
    }
}