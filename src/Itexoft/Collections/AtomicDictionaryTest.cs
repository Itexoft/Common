// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using Itexoft.Threading.Core;

namespace Itexoft.Collections;

public unsafe sealed class AtomicDictionaryTest<TKey, TValue> where TKey : unmanaged where TValue : unmanaged
{
    public delegate TValue UpdateFactory(in TKey key, in TValue oldValue);

    public delegate TValue UpdateFactory<TArg>(in TKey key, in TValue oldValue, in TArg arg);

    public delegate TValue ValueFactory(in TKey key);

    public delegate TValue ValueFactory<TArg>(in TKey key, in TArg arg);

    private const byte ctrlEmpty = 0x80;
    private const byte ctrlDeleted = 0xFE;
    private const int combineFree = 0;
    private const int combinePublishing = 1;
    private const int combineReady = 2;
    private const int combineProcessing = 3;
    private const int combineDone = 4;
    private readonly Locks locks;

    private readonly FastConcurrentDictionaryOptions options;
    private readonly Lock retiredLock = new();
    private readonly int shardMask;
    private readonly Shard[] shards;
    private readonly int slotCacheMask;
    private readonly ThreadSlotCache[] slotCaches;
    private readonly int slotWordCount;
    private readonly long* slotWords;
    private Qsbr qsbr;
    private Retired[] retired;
    private int retiredCount;

    public AtomicDictionaryTest() : this(default) { }

    private AtomicDictionaryTest(FastConcurrentDictionaryOptions options = default)
    {
        this.options = NormalizeOptions(options);

        var shardCount = RoundUpToPowerOfTwo(this.options.ShardCount);
        this.shards = new Shard[shardCount];
        this.shardMask = shardCount - 1;

        this.locks = CreateLocks(shardCount, this.options);

        var slotCount = Math.Max(1, this.options.MaxSessions);
        this.slotWordCount = (slotCount + 63) / 64;
        this.slotWords = (long*)NativeMemory.Alloc((nuint)this.slotWordCount, (nuint)sizeof(long));
        NativeMemory.Clear(this.slotWords, (nuint)this.slotWordCount * (nuint)sizeof(long));

        const int slotStride = 8;
        var slots = (long*)NativeMemory.Alloc((nuint)slotCount * (nuint)slotStride, (nuint)sizeof(long));
        NativeMemory.Clear(slots, (nuint)slotCount * (nuint)slotStride * (nuint)sizeof(long));

        this.qsbr = new()
        {
            Slots = slots,
            SlotCount = slotCount,
            SlotStride = slotStride,
            GlobalEpoch = 1,
        };

        if (this.qsbr.SlotCount > 0)
        {
            var cacheSize = RoundUpToPowerOfTwo(Math.Max(1, this.qsbr.SlotCount * 2));
            this.slotCaches = new ThreadSlotCache[cacheSize];
            this.slotCacheMask = cacheSize - 1;
        }
        else
        {
            this.slotCaches = Array.Empty<ThreadSlotCache>();
            this.slotCacheMask = 0;
        }

        var combineSlotCount = this.options.EnableCombining ? this.options.CombiningSlots : 0;

        for (var i = 0; i < this.shards.Length; i++)
        {
            var table = CreateTable(this.options.InitialCapacityPerShard, this.options, out var owner);
            var lockIndex = i;

            if (this.locks.LockScheme == LockScheme.Bitset && this.locks.LockStrideWords > 0)
                lockIndex = (i * this.locks.LockStrideWords) << 6;

            this.shards[i] = new()
            {
                TablePtr = (nint)table,
                Owner = owner,
                Count = 0,
                Tombstones = 0,
                LockIndex = lockIndex,
                CombineSlots = combineSlotCount > 0 ? new CombineRequest[combineSlotCount] : null,
                CombineSlotMask = combineSlotCount > 0 ? combineSlotCount - 1 : 0,
            };
        }

        this.retired = new Retired[8];
    }

    private int ShardCount => this.shards.Length;

    private int Count
    {
        get
        {
            var total = 0;

            for (var i = 0; i < this.shards.Length; i++)
                total += Volatile.Read(ref this.shards[i].Count);

            return total;
        }
    }

    private int Capacity
    {
        get
        {
            var total = 0;

            for (var i = 0; i < this.shards.Length; i++)
            {
                var table = ReadTablePointer(ref this.shards[i].TablePtr);

                if (table != null)
                    total += table->Capacity;
            }

            return total;
        }
    }

    public bool TryGet(in TKey key, out TValue value)
    {
        var hash = HashKey(in key);
        var shardIndex = this.ShardIndex(hash);

        return this.TryGetFast(in key, hash, shardIndex, out value);
    }

    public bool TryAdd(in TKey key, in TValue value) =>
        this.TryAddValue(in key, in value);

    public bool TryAdd(in TKey key, ValueFactory valueFactory)
    {
        if (valueFactory is null)
            throw new ArgumentNullException(nameof(valueFactory));

        return this.TryAddFactory(in key, valueFactory);
    }

    public bool TryAdd<TArg>(in TKey key, in TArg arg, ValueFactory<TArg> valueFactory)
    {
        if (valueFactory is null)
            throw new ArgumentNullException(nameof(valueFactory));

        return this.TryAddFactory(in key, in arg, valueFactory);
    }

    public TValue GetOrAdd(in TKey key, in TValue value) =>
        this.GetOrAddValue(in key, in value);

    public TValue GetOrAdd(in TKey key, ValueFactory valueFactory)
    {
        if (valueFactory is null)
            throw new ArgumentNullException(nameof(valueFactory));

        return this.GetOrAddFactory(in key, valueFactory);
    }

    public TValue GetOrAdd<TArg>(in TKey key, in TArg arg, ValueFactory<TArg> valueFactory)
    {
        if (valueFactory is null)
            throw new ArgumentNullException(nameof(valueFactory));

        return this.GetOrAddFactory(in key, in arg, valueFactory);
    }

    public TValue AddOrUpdate(in TKey key, in TValue addValue, UpdateFactory updateFactory)
    {
        if (updateFactory is null)
            throw new ArgumentNullException(nameof(updateFactory));

        return this.AddOrUpdateValue(in key, in addValue, updateFactory);
    }

    public TValue AddOrUpdate(in TKey key, ValueFactory addFactory, UpdateFactory updateFactory)
    {
        if (addFactory is null)
            throw new ArgumentNullException(nameof(addFactory));

        if (updateFactory is null)
            throw new ArgumentNullException(nameof(updateFactory));

        return this.AddOrUpdateFactory(in key, addFactory, updateFactory);
    }

    public TValue AddOrUpdate<TArg>(in TKey key, in TArg arg, ValueFactory<TArg> addFactory, UpdateFactory<TArg> updateFactory)
    {
        if (addFactory is null)
            throw new ArgumentNullException(nameof(addFactory));

        if (updateFactory is null)
            throw new ArgumentNullException(nameof(updateFactory));

        return this.AddOrUpdateFactory(in key, in arg, addFactory, updateFactory);
    }

    public bool TryUpdate(in TKey key, in TValue newValue, in TValue comparisonValue)
    {
        var hash = HashKey(in key);
        var shardIndex = this.ShardIndex(hash);
        var h2 = H2(hash);

        if (this.options.EnableCombining && this.shards[shardIndex].CombineSlots != null)
        {
            if (this.TryAcquireLock(shardIndex, hash))
            {
                try
                {
                    ref var shard = ref this.shards[shardIndex];

                    return this.TryUpdateUnderLock(in key, in newValue, in comparisonValue, hash, h2, ref shard);
                }
                finally
                {
                    this.ReleaseLock(shardIndex, hash);
                }
            }

            ref var shardRef = ref this.shards[shardIndex];

            var request = new CombineRequest
            {
                Op = CombineOp.TryUpdate,
                Key = key,
                Value = newValue,
                Comparison = comparisonValue,
            };

            var slotIndex = TryPublishCombineRequest(ref shardRef, ref request, (int)hash);

            if (slotIndex >= 0)
            {
                ref var slot = ref shardRef.CombineSlots![slotIndex];

                return WaitForCombineBool(ref slot);
            }
        }

        return this.TryUpdateWithLock(in key, in newValue, in comparisonValue, hash, h2, shardIndex);
    }

    private bool TryUpdateWithLock(in TKey key, in TValue newValue, in TValue comparisonValue, ulong hash, byte h2, int shardIndex)
    {
        this.AcquireLock(shardIndex, hash);

        try
        {
            ref var shard = ref this.shards[shardIndex];

            return this.TryUpdateUnderLock(in key, in newValue, in comparisonValue, hash, h2, ref shard);
        }
        finally
        {
            this.ReleaseLock(shardIndex, hash);
        }
    }

    private bool TryUpdateUnderLock(in TKey key, in TValue newValue, in TValue comparisonValue, ulong hash, byte h2, ref Shard shard)
    {
        var result = this.TryUpdateLocked(in key, in newValue, in comparisonValue, hash, h2, ref shard);

        if (this.options.EnableCombining)
            this.ProcessCombineQueue(ref shard);

        return result;
    }

    public bool TryRemove(in TKey key, out TValue value)
    {
        var hash = HashKey(in key);
        var shardIndex = this.ShardIndex(hash);
        var h2 = H2(hash);

        if (this.options.EnableCombining && this.shards[shardIndex].CombineSlots != null)
        {
            if (this.TryAcquireLock(shardIndex, hash))
            {
                try
                {
                    ref var shard = ref this.shards[shardIndex];

                    return this.TryRemoveUnderLock(in key, hash, h2, ref shard, out value);
                }
                finally
                {
                    this.ReleaseLock(shardIndex, hash);
                }
            }

            ref var shardRef = ref this.shards[shardIndex];

            var request = new CombineRequest
            {
                Op = CombineOp.TryRemove,
                Key = key,
            };

            var slotIndex = TryPublishCombineRequest(ref shardRef, ref request, (int)hash);

            if (slotIndex >= 0)
            {
                ref var slot = ref shardRef.CombineSlots![slotIndex];

                return WaitForCombineBoolValue(ref slot, out value);
            }
        }

        return this.TryRemoveWithLock(in key, hash, h2, shardIndex, out value);
    }

    private bool TryRemoveWithLock(in TKey key, ulong hash, byte h2, int shardIndex, out TValue value)
    {
        this.AcquireLock(shardIndex, hash);

        try
        {
            ref var shard = ref this.shards[shardIndex];

            return this.TryRemoveUnderLock(in key, hash, h2, ref shard, out value);
        }
        finally
        {
            this.ReleaseLock(shardIndex, hash);
        }
    }

    private bool TryRemoveUnderLock(in TKey key, ulong hash, byte h2, ref Shard shard, out TValue value)
    {
        var result = this.TryRemoveLocked(in key, hash, h2, ref shard, out value);

        if (this.options.EnableCombining)
            this.ProcessCombineQueue(ref shard);

        return result;
    }

    ~AtomicDictionaryTest()
    {
        for (var i = 0; i < this.shards.Length; i++)
        {
            var table = ReadTablePointer(ref this.shards[i].TablePtr);
            var owner = this.shards[i].Owner;
            this.shards[i].TablePtr = 0;
            this.shards[i].Owner = null;

            if (table != null)
                DestroyTable(table, owner);
        }

        lock (this.retiredLock)
        {
            for (var i = 0; i < this.retiredCount; i++)
            {
                var item = this.retired[i];

                if (item.Table != null)
                    DestroyTable(item.Table, item.Owner);
            }

            this.retiredCount = 0;
        }

        if (this.locks.LockWords != null)
            NativeMemory.Free(this.locks.LockWords);

        if (this.qsbr.Slots != null)
            NativeMemory.Free(this.qsbr.Slots);

        if (this.slotWords != null)
            NativeMemory.Free(this.slotWords);
    }

    private static FastConcurrentDictionaryOptions NormalizeOptions(FastConcurrentDictionaryOptions options)
    {
        var useDefaults = options.ShardCount == 0
                          && options.InitialCapacityPerShard == 0
                          && options.MaxLoadFactor == 0
                          && options.TombstoneRatio == 0
                          && options.GroupWidth == 0
                          && options.MaxProbeGroups == 0
                          && options.SpinIters == 0
                          && options.SlowPathIters == 0
                          && options.MaxSessions == 0
                          && options.CombiningSlots == 0
                          && !options.EnableCombining;

        if (useDefaults)
            options = new FastConcurrentDictionaryOptions();

        var shardCount = options.ShardCount > 0 ? options.ShardCount : 256;
        var initialCapacityPerShard = options.InitialCapacityPerShard > 0 ? options.InitialCapacityPerShard : 1024;
        var maxLoadFactor = options.MaxLoadFactor > 0 && options.MaxLoadFactor < 1 ? options.MaxLoadFactor : 0.75f;
        var tombstoneRatio = options.TombstoneRatio > 0 && options.TombstoneRatio < 1 ? options.TombstoneRatio : 0.20f;
        var groupWidth = options.GroupWidth > 0 ? options.GroupWidth : 16;
        var maxProbeGroups = options.MaxProbeGroups > 0 ? options.MaxProbeGroups : 0;
        var spinIters = options.SpinIters > 0 ? options.SpinIters : 128;
        var slowPathIters = options.SlowPathIters > 0 ? options.SlowPathIters : 4096;
        var maxSessions = options.MaxSessions > 0 ? options.MaxSessions : 256;
        var contentionMode = useDefaults ? ContentionMode.SpinThenMonitor : options.ContentionMode;
        var lockScheme = useDefaults ? LockScheme.Bitset : options.LockScheme;
        var enableCombining = options.EnableCombining;
        var combiningSlots = options.CombiningSlots;

        if (enableCombining)
        {
            if (combiningSlots <= 0)
                combiningSlots = 64;

            if (!BitOperations.IsPow2((uint)combiningSlots))
                combiningSlots = (int)BitOperations.RoundUpToPowerOf2((uint)combiningSlots);
        }
        else
            combiningSlots = 0;

        return new(
            shardCount,
            initialCapacityPerShard,
            maxLoadFactor,
            tombstoneRatio,
            groupWidth,
            maxProbeGroups,
            spinIters,
            slowPathIters,
            contentionMode,
            lockScheme,
            maxSessions,
            enableCombining,
            combiningSlots);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong HashKey(in TKey key) => MemoryComparer<TKey>.Hash(in key);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int ShardIndex(ulong hash) => (int)((uint)(hash >> 32) & (uint)this.shardMask);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool KeysEqual(in TKey left, in TKey right) => MemoryComparer<TKey>.Equals(in left, in right);

    private static Locks CreateLocks(int shardCount, FastConcurrentDictionaryOptions options)
    {
        if (options.LockScheme == LockScheme.Matrix2D)
        {
            var words = (long*)NativeMemory.Alloc(2, (nuint)sizeof(long));
            NativeMemory.Clear(words, 2u * (nuint)sizeof(long));

            return new()
            {
                LockWords = words,
                SlowLocks = [new object(), new object()],
                SlowWaiters = new int[2],
                SpinIters = options.SpinIters,
                SlowPathIters = options.SlowPathIters,
                ContentionMode = options.ContentionMode,
                LockScheme = options.LockScheme,
                LockStrideWords = 0,
            };
        }
        else
        {
            const int lockStrideWords = 8;
            var wordCount = checked(shardCount * lockStrideWords);
            var words = (long*)NativeMemory.Alloc((nuint)wordCount, (nuint)sizeof(long));
            NativeMemory.Clear(words, (nuint)wordCount * (nuint)sizeof(long));
            var slowLocks = new object[wordCount];

            for (var i = 0; i < slowLocks.Length; i++)
                slowLocks[i] = new object();

            return new()
            {
                LockWords = words,
                SlowLocks = slowLocks,
                SlowWaiters = new int[wordCount],
                SpinIters = options.SpinIters,
                SlowPathIters = options.SlowPathIters,
                ContentionMode = options.ContentionMode,
                LockScheme = options.LockScheme,
                LockStrideWords = lockStrideWords,
            };
        }
    }

    private static Table* CreateTable(int capacity, FastConcurrentDictionaryOptions options, out object? owner)
    {
        var adjustedCapacity = RoundUpToPowerOfTwo(Math.Max(4, capacity));
        var groupWidth = options.GroupWidth > 0 ? options.GroupWidth : 16;
        groupWidth = Math.Min(groupWidth, adjustedCapacity);

        if (groupWidth > 1 && !BitOperations.IsPow2((uint)groupWidth))
            groupWidth = 1 << (int)BitOperations.Log2((uint)groupWidth);

        if (groupWidth < 1)
            groupWidth = 1;

        var totalGroups = adjustedCapacity / groupWidth;
        var maxProbeGroups = options.MaxProbeGroups > 0 ? Math.Min(options.MaxProbeGroups, totalGroups) : totalGroups;

        var table = (Table*)NativeMemory.Alloc((nuint)sizeof(Table));
        table->Capacity = adjustedCapacity;
        table->Mask = adjustedCapacity - 1;
        table->GroupWidth = groupWidth;
        table->MaxProbeGroups = maxProbeGroups;
        table->Ctrl = (byte*)NativeMemory.Alloc((nuint)adjustedCapacity, 1);
        table->Entries = (Entry*)NativeMemory.Alloc((nuint)adjustedCapacity, (nuint)sizeof(Entry));
        owner = null;

        InitializeCtrl(table->Ctrl, adjustedCapacity);

        return table;
    }

    private static void DestroyTable(Table* table, object? owner)
    {
        if (table == null)
            return;

        _ = owner;

        if (table->Ctrl != null)
            NativeMemory.Free(table->Ctrl);

        if (table->Entries != null)
            NativeMemory.Free(table->Entries);

        NativeMemory.Free(table);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void InitializeCtrl(byte* ctrl, int length) => Unsafe.InitBlockUnaligned(ctrl, ctrlEmpty, (uint)length);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int RoundUpToPowerOfTwo(int value)
    {
        if (value <= 1)
            return 1;

        var v = BitOperations.RoundUpToPowerOf2((uint)value);

        if (v > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(value));

        return (int)v;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte H2(ulong hash) => (byte)((hash >> 57) & 0x7F);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong MixLock(ulong value)
    {
        unchecked
        {
            value ^= value >> 33;
            value *= 0xff51afd7ed558ccdUL;
            value ^= value >> 33;
            value *= 0xc4ceb9fe1a85ec53UL;
            value ^= value >> 33;

            return value;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Table* ReadTablePointer(ref nint location) => (Table*)Volatile.Read(ref location);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteTablePointer(ref nint location, Table* value) =>
        Volatile.Write(ref location, (nint)value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ref byte CtrlAt(Table* table, int index) =>
        ref Unsafe.Add(ref Unsafe.AsRef<byte>(table->Ctrl), index);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong ReadCtrlWordUnaligned(Table* table, int index)
    {
        ref var ctrl = ref CtrlAt(table, index);

        return Unsafe.ReadUnaligned<ulong>(ref ctrl);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint MatchByteMaskVector16(ref byte start, byte target)
    {
        if (Vector128.IsHardwareAccelerated)
        {
            var vec = Unsafe.ReadUnaligned<Vector128<byte>>(ref start);
            var eq = Vector128.Equals(vec, Vector128.Create(target));

            return eq.ExtractMostSignificantBits();
        }

        var ctrlLo = Unsafe.ReadUnaligned<ulong>(ref start);
        var ctrlHi = Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref start, 8));

        return MatchByteMask(ctrlLo, target) | (MatchByteMask(ctrlHi, target) << 8);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint MatchByteMask(ulong value, byte target)
    {
        const ulong k1 = 0x0101010101010101UL;
        const ulong kMsb = 0x8080808080808080UL;
        const ulong kCompress = 0x0102040810204081UL;

        var x = value ^ (k1 * target);
        var tmp = (x - k1) & ~x & kMsb;

        return (uint)(((tmp >> 7) * kCompress) >> 56);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ref Entry EntryAt(Table* table, int index) =>
        ref Unsafe.Add(ref Unsafe.AsRef<Entry>(table->Entries), index);

    private bool TryGetLocked(in TKey key, ulong hash, int shardIndex, out TValue value)
    {
        this.AcquireLock(shardIndex, hash);

        try
        {
            return this.TryGetCore(in key, hash, shardIndex, out value);
        }
        finally
        {
            this.ReleaseLock(shardIndex, hash);
        }
    }

    private bool TryGetFast(in TKey key, ulong hash, int shardIndex, out TValue value)
    {
        if (this.qsbr.SlotCount == 0)
            return this.TryGetLocked(in key, hash, shardIndex, out value);

        var slotIndex = this.GetThreadSlotIndex();

        if (slotIndex < 0)
            return this.TryGetLocked(in key, hash, shardIndex, out value);

        this.EnterRead(slotIndex);

        return this.TryGetCore(in key, hash, shardIndex, out value);
    }

    private bool TryGetWithSlot(in TKey key, int slotIndex, out TValue value)
    {
        var hash = HashKey(in key);
        var shardIndex = this.ShardIndex(hash);

        if (slotIndex < 0 || this.qsbr.SlotCount == 0)
            return this.TryGetLocked(in key, hash, shardIndex, out value);

        return this.TryGetCore(in key, hash, shardIndex, out value);
    }

    private bool TryGetCore(in TKey key, ulong hash, int shardIndex, out TValue value)
    {
        ref var shard = ref this.shards[shardIndex];
        var table = ReadTablePointer(ref shard.TablePtr);

        if (table == null)
        {
            value = default;

            return false;
        }

        var h2 = H2(hash);
        var mask = table->Mask;
        var start = (int)(hash & (uint)mask);

        var groupWidth = table->GroupWidth;
        var capacity = table->Capacity;
        var baseIndex = start - (start & (groupWidth - 1));

        for (var group = 0; group < table->MaxProbeGroups; group++)
        {
            if ((groupWidth == 16 || groupWidth == 8) && baseIndex + groupWidth <= capacity)
            {
                uint matchMask;
                uint emptyMask;

                if (groupWidth == 16)
                {
                    ref var ctrl = ref CtrlAt(table, baseIndex);
                    matchMask = MatchByteMaskVector16(ref ctrl, h2);
                    emptyMask = MatchByteMaskVector16(ref ctrl, ctrlEmpty);
                }
                else
                {
                    var ctrlWord = ReadCtrlWordUnaligned(table, baseIndex);
                    matchMask = MatchByteMask(ctrlWord, h2);
                    emptyMask = MatchByteMask(ctrlWord, ctrlEmpty);
                }

                while (matchMask != 0)
                {
                    var bit = BitOperations.TrailingZeroCount(matchMask);
                    var index = baseIndex + bit;

                    if (Volatile.Read(ref CtrlAt(table, index)) == h2)
                    {
                        ref var entry = ref EntryAt(table, index);

                        if (KeysEqual(in key, in entry.Key))
                        {
                            value = entry.Value;

                            return true;
                        }
                    }

                    matchMask &= matchMask - 1;
                }

                while (emptyMask != 0)
                {
                    var bit = BitOperations.TrailingZeroCount(emptyMask);
                    var index = baseIndex + bit;

                    if (Volatile.Read(ref CtrlAt(table, index)) == ctrlEmpty)
                    {
                        value = default;

                        return false;
                    }

                    emptyMask &= emptyMask - 1;
                }

                baseIndex = (baseIndex + groupWidth) & mask;

                continue;
            }

            for (var offset = 0; offset < groupWidth; offset++)
            {
                var index = (baseIndex + offset) & mask;
                var ctrl = Volatile.Read(ref CtrlAt(table, index));

                if (ctrl == ctrlEmpty)
                {
                    value = default;

                    return false;
                }

                if (ctrl == h2)
                {
                    ref var entry = ref EntryAt(table, index);

                    if (KeysEqual(in key, in entry.Key))
                    {
                        value = entry.Value;

                        return true;
                    }
                }
            }

            baseIndex = (baseIndex + groupWidth) & mask;
        }

        value = default;

        return false;
    }

    private bool TryAddValue(in TKey key, in TValue value)
    {
        var hash = HashKey(in key);
        var shardIndex = this.ShardIndex(hash);
        var h2 = H2(hash);

        if (this.options.EnableCombining && this.shards[shardIndex].CombineSlots != null)
        {
            if (this.TryAcquireLock(shardIndex, hash))
            {
                try
                {
                    ref var shard = ref this.shards[shardIndex];

                    return this.TryAddUnderLock(in key, in value, hash, h2, ref shard);
                }
                finally
                {
                    this.ReleaseLock(shardIndex, hash);
                }
            }

            ref var shardRef = ref this.shards[shardIndex];

            var request = new CombineRequest
            {
                Op = CombineOp.TryAddValue,
                Key = key,
                Value = value,
            };

            var slotIndex = TryPublishCombineRequest(ref shardRef, ref request, (int)hash);

            if (slotIndex >= 0)
            {
                ref var slot = ref shardRef.CombineSlots![slotIndex];

                return WaitForCombineBool(ref slot);
            }
        }

        return this.TryAddWithLock(in key, in value, hash, h2, shardIndex);
    }

    private bool TryAddFactory(in TKey key, ValueFactory valueFactory)
    {
        var hash = HashKey(in key);
        var shardIndex = this.ShardIndex(hash);
        var h2 = H2(hash);

        if (this.options.EnableCombining && this.shards[shardIndex].CombineSlots != null)
        {
            if (this.TryAcquireLock(shardIndex, hash))
            {
                try
                {
                    ref var shard = ref this.shards[shardIndex];

                    return this.TryAddUnderLock(in key, valueFactory, hash, h2, ref shard);
                }
                finally
                {
                    this.ReleaseLock(shardIndex, hash);
                }
            }

            ref var shardRef = ref this.shards[shardIndex];

            var request = new CombineRequest
            {
                Op = CombineOp.TryAddFactory,
                Key = key,
                ValueFactory = valueFactory,
            };

            var slotIndex = TryPublishCombineRequest(ref shardRef, ref request, (int)hash);

            if (slotIndex >= 0)
            {
                ref var slot = ref shardRef.CombineSlots![slotIndex];

                return WaitForCombineBool(ref slot);
            }
        }

        return this.TryAddWithLock(in key, valueFactory, hash, h2, shardIndex);
    }

    private bool TryAddFactory<TArg>(in TKey key, in TArg arg, ValueFactory<TArg> valueFactory)
    {
        var hash = HashKey(in key);
        var shardIndex = this.ShardIndex(hash);
        var h2 = H2(hash);

        return this.TryAddWithLock(in key, in arg, valueFactory, hash, h2, shardIndex);
    }

    private bool TryAddWithLock(in TKey key, in TValue value, ulong hash, byte h2, int shardIndex)
    {
        this.AcquireLock(shardIndex, hash);

        try
        {
            ref var shard = ref this.shards[shardIndex];

            return this.TryAddUnderLock(in key, in value, hash, h2, ref shard);
        }
        finally
        {
            this.ReleaseLock(shardIndex, hash);
        }
    }

    private bool TryAddUnderLock(in TKey key, in TValue value, ulong hash, byte h2, ref Shard shard)
    {
        var result = this.TryAddLocked(in key, in value, hash, h2, ref shard);

        if (this.options.EnableCombining)
            this.ProcessCombineQueue(ref shard);

        return result;
    }

    private bool TryAddWithLock(in TKey key, ValueFactory valueFactory, ulong hash, byte h2, int shardIndex)
    {
        this.AcquireLock(shardIndex, hash);

        try
        {
            ref var shard = ref this.shards[shardIndex];

            return this.TryAddUnderLock(in key, valueFactory, hash, h2, ref shard);
        }
        finally
        {
            this.ReleaseLock(shardIndex, hash);
        }
    }

    private bool TryAddUnderLock(in TKey key, ValueFactory valueFactory, ulong hash, byte h2, ref Shard shard)
    {
        var result = this.TryAddLocked(in key, valueFactory, hash, h2, ref shard);

        if (this.options.EnableCombining)
            this.ProcessCombineQueue(ref shard);

        return result;
    }

    private bool TryAddWithLock<TArg>(in TKey key, in TArg arg, ValueFactory<TArg> valueFactory, ulong hash, byte h2, int shardIndex)
    {
        this.AcquireLock(shardIndex, hash);

        try
        {
            ref var shard = ref this.shards[shardIndex];

            return this.TryAddUnderLock(in key, in arg, valueFactory, hash, h2, ref shard);
        }
        finally
        {
            this.ReleaseLock(shardIndex, hash);
        }
    }

    private bool TryAddUnderLock<TArg>(in TKey key, in TArg arg, ValueFactory<TArg> valueFactory, ulong hash, byte h2, ref Shard shard)
    {
        var result = this.TryAddLocked(in key, in arg, valueFactory, hash, h2, ref shard);

        if (this.options.EnableCombining)
            this.ProcessCombineQueue(ref shard);

        return result;
    }

    private TValue GetOrAddValue(in TKey key, in TValue value) => this.GetOrAddValueWithSlot(in key, in value, -1);

    private TValue GetOrAddValueWithSlot(in TKey key, in TValue value, int slotIndex)
    {
        var hash = HashKey(in key);
        var shardIndex = this.ShardIndex(hash);

        if (this.qsbr.SlotCount > 0)
        {
            if (slotIndex >= 0
                    ? this.TryGetCore(in key, hash, shardIndex, out var existing)
                    : this.TryGetFast(in key, hash, shardIndex, out existing))
                return existing;
        }

        var h2 = H2(hash);

        if (this.options.EnableCombining && this.shards[shardIndex].CombineSlots != null)
        {
            if (this.TryAcquireLock(shardIndex, hash))
            {
                try
                {
                    ref var shard = ref this.shards[shardIndex];

                    return this.GetOrAddUnderLock(in key, in value, hash, h2, ref shard);
                }
                finally
                {
                    this.ReleaseLock(shardIndex, hash);
                }
            }

            ref var shardRef = ref this.shards[shardIndex];

            var request = new CombineRequest
            {
                Op = CombineOp.GetOrAddValue,
                Key = key,
                Value = value,
            };

            var combineSlotIndex = TryPublishCombineRequest(ref shardRef, ref request, (int)hash);

            if (combineSlotIndex >= 0)
            {
                ref var slot = ref shardRef.CombineSlots![combineSlotIndex];

                return WaitForCombineValue(ref slot);
            }
        }

        return this.GetOrAddWithLock(in key, in value, hash, h2, shardIndex);
    }

    private TValue GetOrAddFactory(in TKey key, ValueFactory valueFactory) => this.GetOrAddFactoryWithSlot(in key, valueFactory, -1);

    private TValue GetOrAddFactoryWithSlot(in TKey key, ValueFactory valueFactory, int slotIndex)
    {
        var hash = HashKey(in key);
        var shardIndex = this.ShardIndex(hash);

        if (this.qsbr.SlotCount > 0)
        {
            if (slotIndex >= 0
                    ? this.TryGetCore(in key, hash, shardIndex, out var existing)
                    : this.TryGetFast(in key, hash, shardIndex, out existing))
                return existing;
        }

        var h2 = H2(hash);

        if (this.options.EnableCombining && this.shards[shardIndex].CombineSlots != null)
        {
            if (this.TryAcquireLock(shardIndex, hash))
            {
                try
                {
                    ref var shard = ref this.shards[shardIndex];

                    return this.GetOrAddUnderLock(in key, valueFactory, hash, h2, ref shard);
                }
                finally
                {
                    this.ReleaseLock(shardIndex, hash);
                }
            }

            ref var shardRef = ref this.shards[shardIndex];

            var request = new CombineRequest
            {
                Op = CombineOp.GetOrAddFactory,
                Key = key,
                ValueFactory = valueFactory,
            };

            var combineSlotIndex = TryPublishCombineRequest(ref shardRef, ref request, (int)hash);

            if (combineSlotIndex >= 0)
            {
                ref var slot = ref shardRef.CombineSlots![combineSlotIndex];

                return WaitForCombineValue(ref slot);
            }
        }

        return this.GetOrAddWithLock(in key, valueFactory, hash, h2, shardIndex);
    }

    private TValue GetOrAddFactory<TArg>(in TKey key, in TArg arg, ValueFactory<TArg> valueFactory) =>
        this.GetOrAddFactoryWithSlot(in key, in arg, valueFactory, -1);

    private TValue GetOrAddFactoryWithSlot<TArg>(in TKey key, in TArg arg, ValueFactory<TArg> valueFactory, int slotIndex)
    {
        var hash = HashKey(in key);
        var shardIndex = this.ShardIndex(hash);

        if (this.qsbr.SlotCount > 0)
        {
            if (slotIndex >= 0
                    ? this.TryGetCore(in key, hash, shardIndex, out var existing)
                    : this.TryGetFast(in key, hash, shardIndex, out existing))
                return existing;
        }

        var h2 = H2(hash);

        return this.GetOrAddWithLock(in key, in arg, valueFactory, hash, h2, shardIndex);
    }

    private TValue GetOrAddWithLock(in TKey key, in TValue value, ulong hash, byte h2, int shardIndex)
    {
        this.AcquireLock(shardIndex, hash);

        try
        {
            ref var shard = ref this.shards[shardIndex];

            return this.GetOrAddUnderLock(in key, in value, hash, h2, ref shard);
        }
        finally
        {
            this.ReleaseLock(shardIndex, hash);
        }
    }

    private TValue GetOrAddUnderLock(in TKey key, in TValue value, ulong hash, byte h2, ref Shard shard)
    {
        var result = this.GetOrAddLocked(in key, in value, hash, h2, ref shard);

        if (this.options.EnableCombining)
            this.ProcessCombineQueue(ref shard);

        return result;
    }

    private TValue GetOrAddWithLock(in TKey key, ValueFactory valueFactory, ulong hash, byte h2, int shardIndex)
    {
        this.AcquireLock(shardIndex, hash);

        try
        {
            ref var shard = ref this.shards[shardIndex];

            return this.GetOrAddUnderLock(in key, valueFactory, hash, h2, ref shard);
        }
        finally
        {
            this.ReleaseLock(shardIndex, hash);
        }
    }

    private TValue GetOrAddUnderLock(in TKey key, ValueFactory valueFactory, ulong hash, byte h2, ref Shard shard)
    {
        var result = this.GetOrAddLocked(in key, valueFactory, hash, h2, ref shard);

        if (this.options.EnableCombining)
            this.ProcessCombineQueue(ref shard);

        return result;
    }

    private TValue GetOrAddWithLock<TArg>(in TKey key, in TArg arg, ValueFactory<TArg> valueFactory, ulong hash, byte h2, int shardIndex)
    {
        this.AcquireLock(shardIndex, hash);

        try
        {
            ref var shard = ref this.shards[shardIndex];

            return this.GetOrAddUnderLock(in key, in arg, valueFactory, hash, h2, ref shard);
        }
        finally
        {
            this.ReleaseLock(shardIndex, hash);
        }
    }

    private TValue GetOrAddUnderLock<TArg>(in TKey key, in TArg arg, ValueFactory<TArg> valueFactory, ulong hash, byte h2, ref Shard shard)
    {
        var result = this.GetOrAddLocked(in key, in arg, valueFactory, hash, h2, ref shard);

        if (this.options.EnableCombining)
            this.ProcessCombineQueue(ref shard);

        return result;
    }

    private TValue AddOrUpdateValue(in TKey key, in TValue addValue, UpdateFactory updateFactory)
    {
        var hash = HashKey(in key);
        var shardIndex = this.ShardIndex(hash);
        var h2 = H2(hash);

        if (this.options.EnableCombining && this.shards[shardIndex].CombineSlots != null)
        {
            if (this.TryAcquireLock(shardIndex, hash))
            {
                try
                {
                    ref var shard = ref this.shards[shardIndex];

                    return this.AddOrUpdateUnderLock(in key, in addValue, updateFactory, hash, h2, ref shard);
                }
                finally
                {
                    this.ReleaseLock(shardIndex, hash);
                }
            }

            ref var shardRef = ref this.shards[shardIndex];

            var request = new CombineRequest
            {
                Op = CombineOp.AddOrUpdateValue,
                Key = key,
                Value = addValue,
                UpdateFactory = updateFactory,
            };

            var slotIndex = TryPublishCombineRequest(ref shardRef, ref request, (int)hash);

            if (slotIndex >= 0)
            {
                ref var slot = ref shardRef.CombineSlots![slotIndex];

                return WaitForCombineValue(ref slot);
            }
        }

        return this.AddOrUpdateWithLock(in key, in addValue, updateFactory, hash, h2, shardIndex);
    }

    private TValue AddOrUpdateFactory(in TKey key, ValueFactory addFactory, UpdateFactory updateFactory)
    {
        var hash = HashKey(in key);
        var shardIndex = this.ShardIndex(hash);
        var h2 = H2(hash);

        if (this.options.EnableCombining && this.shards[shardIndex].CombineSlots != null)
        {
            if (this.TryAcquireLock(shardIndex, hash))
            {
                try
                {
                    ref var shard = ref this.shards[shardIndex];

                    return this.AddOrUpdateUnderLock(in key, addFactory, updateFactory, hash, h2, ref shard);
                }
                finally
                {
                    this.ReleaseLock(shardIndex, hash);
                }
            }

            ref var shardRef = ref this.shards[shardIndex];

            var request = new CombineRequest
            {
                Op = CombineOp.AddOrUpdateFactory,
                Key = key,
                ValueFactory = addFactory,
                UpdateFactory = updateFactory,
            };

            var slotIndex = TryPublishCombineRequest(ref shardRef, ref request, (int)hash);

            if (slotIndex >= 0)
            {
                ref var slot = ref shardRef.CombineSlots![slotIndex];

                return WaitForCombineValue(ref slot);
            }
        }

        return this.AddOrUpdateWithLock(in key, addFactory, updateFactory, hash, h2, shardIndex);
    }

    private TValue AddOrUpdateFactory<TArg>(in TKey key, in TArg arg, ValueFactory<TArg> addFactory, UpdateFactory<TArg> updateFactory)
    {
        var hash = HashKey(in key);
        var shardIndex = this.ShardIndex(hash);
        var h2 = H2(hash);

        return this.AddOrUpdateWithLock(in key, in arg, addFactory, updateFactory, hash, h2, shardIndex);
    }

    private TValue AddOrUpdateWithLock(in TKey key, in TValue addValue, UpdateFactory updateFactory, ulong hash, byte h2, int shardIndex)
    {
        this.AcquireLock(shardIndex, hash);

        try
        {
            ref var shard = ref this.shards[shardIndex];

            return this.AddOrUpdateUnderLock(in key, in addValue, updateFactory, hash, h2, ref shard);
        }
        finally
        {
            this.ReleaseLock(shardIndex, hash);
        }
    }

    private TValue AddOrUpdateUnderLock(in TKey key, in TValue addValue, UpdateFactory updateFactory, ulong hash, byte h2, ref Shard shard)
    {
        var result = this.AddOrUpdateLocked(in key, in addValue, updateFactory, hash, h2, ref shard);

        if (this.options.EnableCombining)
            this.ProcessCombineQueue(ref shard);

        return result;
    }

    private TValue AddOrUpdateWithLock(in TKey key, ValueFactory addFactory, UpdateFactory updateFactory, ulong hash, byte h2, int shardIndex)
    {
        this.AcquireLock(shardIndex, hash);

        try
        {
            ref var shard = ref this.shards[shardIndex];

            return this.AddOrUpdateUnderLock(in key, addFactory, updateFactory, hash, h2, ref shard);
        }
        finally
        {
            this.ReleaseLock(shardIndex, hash);
        }
    }

    private TValue AddOrUpdateUnderLock(in TKey key, ValueFactory addFactory, UpdateFactory updateFactory, ulong hash, byte h2, ref Shard shard)
    {
        var result = this.AddOrUpdateLocked(in key, addFactory, updateFactory, hash, h2, ref shard);

        if (this.options.EnableCombining)
            this.ProcessCombineQueue(ref shard);

        return result;
    }

    private TValue AddOrUpdateWithLock<TArg>(
        in TKey key,
        in TArg arg,
        ValueFactory<TArg> addFactory,
        UpdateFactory<TArg> updateFactory,
        ulong hash,
        byte h2,
        int shardIndex)
    {
        this.AcquireLock(shardIndex, hash);

        try
        {
            ref var shard = ref this.shards[shardIndex];

            return this.AddOrUpdateUnderLock(in key, in arg, addFactory, updateFactory, hash, h2, ref shard);
        }
        finally
        {
            this.ReleaseLock(shardIndex, hash);
        }
    }

    private TValue AddOrUpdateUnderLock<TArg>(
        in TKey key,
        in TArg arg,
        ValueFactory<TArg> addFactory,
        UpdateFactory<TArg> updateFactory,
        ulong hash,
        byte h2,
        ref Shard shard)
    {
        var result = this.AddOrUpdateLocked(in key, in arg, addFactory, updateFactory, hash, h2, ref shard);

        if (this.options.EnableCombining)
            this.ProcessCombineQueue(ref shard);

        return result;
    }

    private static void InsertAt(Table* table, int index, in TKey key, in TValue value, byte h2)
    {
        ref var entry = ref EntryAt(table, index);
        entry.Key = key;
        entry.Value = value;
        Volatile.Write(ref CtrlAt(table, index), h2);
    }

    private bool TryAddLocked(in TKey key, in TValue value, ulong hash, byte h2, ref Shard shard)
    {
        while (true)
        {
            var table = ReadTablePointer(ref shard.TablePtr);

            if (table == null)
                throw new InvalidOperationException(nameof(AtomicDictionaryTest<TKey, TValue>));

            var index = this.FindSlot(table, in key, hash, h2, out var found, out var usedTombstone);

            if (found)
                return false;

            if (index < 0)
            {
                this.ResizeShard(ref shard, table, table->Capacity * 2);

                continue;
            }

            InsertAt(table, index, in key, in value, h2);

            shard.Count++;

            if (usedTombstone)
                shard.Tombstones--;

            this.MaybeResize(ref shard, table);

            return true;
        }
    }

    private bool TryAddLocked(in TKey key, ValueFactory valueFactory, ulong hash, byte h2, ref Shard shard)
    {
        while (true)
        {
            var table = ReadTablePointer(ref shard.TablePtr);

            if (table == null)
                throw new InvalidOperationException(nameof(AtomicDictionaryTest<TKey, TValue>));

            var index = this.FindSlot(table, in key, hash, h2, out var found, out var usedTombstone);

            if (found)
                return false;

            if (index < 0)
            {
                this.ResizeShard(ref shard, table, table->Capacity * 2);

                continue;
            }

            var value = valueFactory(in key);

            InsertAt(table, index, in key, in value, h2);

            shard.Count++;

            if (usedTombstone)
                shard.Tombstones--;

            this.MaybeResize(ref shard, table);

            return true;
        }
    }

    private bool TryAddLocked<TArg>(in TKey key, in TArg arg, ValueFactory<TArg> valueFactory, ulong hash, byte h2, ref Shard shard)
    {
        while (true)
        {
            var table = ReadTablePointer(ref shard.TablePtr);

            if (table == null)
                throw new InvalidOperationException(nameof(AtomicDictionaryTest<TKey, TValue>));

            var index = this.FindSlot(table, in key, hash, h2, out var found, out var usedTombstone);

            if (found)
                return false;

            if (index < 0)
            {
                this.ResizeShard(ref shard, table, table->Capacity * 2);

                continue;
            }

            var value = valueFactory(in key, in arg);

            InsertAt(table, index, in key, in value, h2);

            shard.Count++;

            if (usedTombstone)
                shard.Tombstones--;

            this.MaybeResize(ref shard, table);

            return true;
        }
    }

    private TValue GetOrAddLocked(in TKey key, in TValue value, ulong hash, byte h2, ref Shard shard)
    {
        while (true)
        {
            var table = ReadTablePointer(ref shard.TablePtr);

            if (table == null)
                throw new InvalidOperationException(nameof(AtomicDictionaryTest<TKey, TValue>));

            var index = this.FindSlot(table, in key, hash, h2, out var found, out var usedTombstone);

            if (found)
                return EntryAt(table, index).Value;

            if (index < 0)
            {
                this.ResizeShard(ref shard, table, table->Capacity * 2);

                continue;
            }

            InsertAt(table, index, in key, in value, h2);

            shard.Count++;

            if (usedTombstone)
                shard.Tombstones--;

            this.MaybeResize(ref shard, table);

            return value;
        }
    }

    private TValue GetOrAddLocked(in TKey key, ValueFactory valueFactory, ulong hash, byte h2, ref Shard shard)
    {
        while (true)
        {
            var table = ReadTablePointer(ref shard.TablePtr);

            if (table == null)
                throw new InvalidOperationException(nameof(AtomicDictionaryTest<TKey, TValue>));

            var index = this.FindSlot(table, in key, hash, h2, out var found, out var usedTombstone);

            if (found)
                return EntryAt(table, index).Value;

            if (index < 0)
            {
                this.ResizeShard(ref shard, table, table->Capacity * 2);

                continue;
            }

            var value = valueFactory(in key);

            InsertAt(table, index, in key, in value, h2);

            shard.Count++;

            if (usedTombstone)
                shard.Tombstones--;

            this.MaybeResize(ref shard, table);

            return value;
        }
    }

    private TValue GetOrAddLocked<TArg>(in TKey key, in TArg arg, ValueFactory<TArg> valueFactory, ulong hash, byte h2, ref Shard shard)
    {
        while (true)
        {
            var table = ReadTablePointer(ref shard.TablePtr);

            if (table == null)
                throw new InvalidOperationException(nameof(AtomicDictionaryTest<TKey, TValue>));

            var index = this.FindSlot(table, in key, hash, h2, out var found, out var usedTombstone);

            if (found)
                return EntryAt(table, index).Value;

            if (index < 0)
            {
                this.ResizeShard(ref shard, table, table->Capacity * 2);

                continue;
            }

            var value = valueFactory(in key, in arg);

            InsertAt(table, index, in key, in value, h2);

            shard.Count++;

            if (usedTombstone)
                shard.Tombstones--;

            this.MaybeResize(ref shard, table);

            return value;
        }
    }

    private TValue AddOrUpdateLocked(in TKey key, in TValue addValue, UpdateFactory updateFactory, ulong hash, byte h2, ref Shard shard)
    {
        while (true)
        {
            var table = ReadTablePointer(ref shard.TablePtr);

            if (table == null)
                throw new InvalidOperationException(nameof(AtomicDictionaryTest<TKey, TValue>));

            var index = this.FindSlot(table, in key, hash, h2, out var found, out var usedTombstone);

            if (found)
            {
                ref var entry = ref EntryAt(table, index);
                var updated = updateFactory(in key, in entry.Value);
                entry.Value = updated;

                return updated;
            }

            if (index < 0)
            {
                this.ResizeShard(ref shard, table, table->Capacity * 2);

                continue;
            }

            InsertAt(table, index, in key, in addValue, h2);
            shard.Count++;

            if (usedTombstone)
                shard.Tombstones--;

            this.MaybeResize(ref shard, table);

            return addValue;
        }
    }

    private TValue AddOrUpdateLocked(in TKey key, ValueFactory addFactory, UpdateFactory updateFactory, ulong hash, byte h2, ref Shard shard)
    {
        while (true)
        {
            var table = ReadTablePointer(ref shard.TablePtr);

            if (table == null)
                throw new InvalidOperationException(nameof(AtomicDictionaryTest<TKey, TValue>));

            var index = this.FindSlot(table, in key, hash, h2, out var found, out var usedTombstone);

            if (found)
            {
                ref var entry = ref EntryAt(table, index);
                var updated = updateFactory(in key, in entry.Value);
                entry.Value = updated;

                return updated;
            }

            if (index < 0)
            {
                this.ResizeShard(ref shard, table, table->Capacity * 2);

                continue;
            }

            var value = addFactory(in key);
            InsertAt(table, index, in key, in value, h2);
            shard.Count++;

            if (usedTombstone)
                shard.Tombstones--;

            this.MaybeResize(ref shard, table);

            return value;
        }
    }

    private TValue AddOrUpdateLocked<TArg>(
        in TKey key,
        in TArg arg,
        ValueFactory<TArg> addFactory,
        UpdateFactory<TArg> updateFactory,
        ulong hash,
        byte h2,
        ref Shard shard)
    {
        while (true)
        {
            var table = ReadTablePointer(ref shard.TablePtr);

            if (table == null)
                throw new InvalidOperationException(nameof(AtomicDictionaryTest<TKey, TValue>));

            var index = this.FindSlot(table, in key, hash, h2, out var found, out var usedTombstone);

            if (found)
            {
                ref var entry = ref EntryAt(table, index);
                var updated = updateFactory(in key, in entry.Value, in arg);
                entry.Value = updated;

                return updated;
            }

            if (index < 0)
            {
                this.ResizeShard(ref shard, table, table->Capacity * 2);

                continue;
            }

            var value = addFactory(in key, in arg);
            InsertAt(table, index, in key, in value, h2);
            shard.Count++;

            if (usedTombstone)
                shard.Tombstones--;

            this.MaybeResize(ref shard, table);

            return value;
        }
    }

    private bool TryUpdateLocked(in TKey key, in TValue newValue, in TValue comparisonValue, ulong hash, byte h2, ref Shard shard)
    {
        var table = ReadTablePointer(ref shard.TablePtr);

        if (table == null)
            return false;

        var index = this.FindSlot(table, in key, hash, h2, out var found, out _);

        if (!found)
            return false;

        ref var entry = ref EntryAt(table, index);

        if (!MemoryComparer<TValue>.Equals(in entry.Value, in comparisonValue))
            return false;

        entry.Value = newValue;

        return true;
    }

    private bool TryRemoveLocked(in TKey key, ulong hash, byte h2, ref Shard shard, out TValue value)
    {
        var table = ReadTablePointer(ref shard.TablePtr);

        if (table == null)
        {
            value = default;

            return false;
        }

        var index = this.FindSlot(table, in key, hash, h2, out var found, out _);

        if (!found)
        {
            value = default;

            return false;
        }

        ref var entry = ref EntryAt(table, index);
        value = entry.Value;

        var nextIndex = (index + 1) & table->Mask;

        if (CtrlAt(table, nextIndex) == ctrlEmpty)
        {
            Volatile.Write(ref CtrlAt(table, index), ctrlEmpty);

            var current = index;

            while (true)
            {
                var prev = (current - 1) & table->Mask;

                if (CtrlAt(table, prev) != ctrlDeleted)
                    break;

                Volatile.Write(ref CtrlAt(table, prev), ctrlEmpty);
                shard.Tombstones--;
                current = prev;
            }
        }
        else
        {
            Volatile.Write(ref CtrlAt(table, index), ctrlDeleted);
            shard.Tombstones++;
        }

        shard.Count--;

        this.MaybeResize(ref shard, table);

        return true;
    }

    private int FindSlot(Table* table, in TKey key, ulong hash, byte h2, out bool found, out bool usedTombstone)
    {
        var firstDeleted = -1;
        var mask = table->Mask;
        var groupWidth = table->GroupWidth;
        var start = (int)(hash & (uint)mask);
        var baseIndex = start - (start & (groupWidth - 1));

        for (var group = 0; group < table->MaxProbeGroups; group++)
        {
            for (var offset = 0; offset < groupWidth; offset++)
            {
                var index = (baseIndex + offset) & mask;
                var ctrl = CtrlAt(table, index);

                if (ctrl == ctrlEmpty)
                {
                    found = false;

                    if (firstDeleted >= 0)
                    {
                        usedTombstone = true;

                        return firstDeleted;
                    }

                    usedTombstone = false;

                    return index;
                }

                if (ctrl == ctrlDeleted)
                {
                    if (firstDeleted < 0)
                        firstDeleted = index;

                    continue;
                }

                if (ctrl == h2)
                {
                    ref var entry = ref EntryAt(table, index);

                    if (KeysEqual(in key, in entry.Key))
                    {
                        found = true;
                        usedTombstone = false;

                        return index;
                    }
                }
            }

            baseIndex = (baseIndex + groupWidth) & mask;
        }

        if (firstDeleted >= 0)
        {
            found = false;
            usedTombstone = true;

            return firstDeleted;
        }

        found = false;
        usedTombstone = false;

        return -1;
    }

    private void MaybeResize(ref Shard shard, Table* table)
    {
        var capacity = table->Capacity;

        if (capacity <= 0)
            return;

        var load = (float)shard.Count / capacity;

        if (load > this.options.MaxLoadFactor)
        {
            this.ResizeShard(ref shard, table, capacity * 2);

            return;
        }

        var tombstoneLoad = (float)shard.Tombstones / capacity;

        if (tombstoneLoad > this.options.TombstoneRatio)
            this.ResizeShard(ref shard, table, capacity);
    }

    private void ResizeShard(ref Shard shard, Table* table, int newCapacity)
    {
        var newTable = CreateTable(newCapacity, this.options, out var newOwner);
        var newCount = 0;

        for (var i = 0; i < table->Capacity; i++)
        {
            var ctrl = CtrlAt(table, i);

            if (ctrl == ctrlEmpty || ctrl == ctrlDeleted)
                continue;

            ref var entry = ref EntryAt(table, i);
            var hash = HashKey(in entry.Key);
            var h2 = H2(hash);
            var insertIndex = this.FindSlot(newTable, in entry.Key, hash, h2, out _, out _);

            if (insertIndex < 0)
                throw new InvalidOperationException("Resize failed due to insufficient capacity.");

            InsertAt(newTable, insertIndex, in entry.Key, in entry.Value, h2);
            newCount++;
        }

        var oldTable = ReadTablePointer(ref shard.TablePtr);
        var oldOwner = shard.Owner;

        shard.Owner = newOwner;
        shard.Count = newCount;
        shard.Tombstones = 0;
        WriteTablePointer(ref shard.TablePtr, newTable);

        if (this.qsbr.SlotCount > 0)
            this.RetireTable(oldTable, oldOwner);
        else
            DestroyTable(oldTable, oldOwner);
    }

    private void RetireTable(Table* table, object? owner)
    {
        if (table == null)
            return;

        var retireEpoch = Interlocked.Increment(ref this.qsbr.GlobalEpoch) - 1;

        lock (this.retiredLock)
        {
            if (this.retiredCount == this.retired.Length)
                Array.Resize(ref this.retired, this.retired.Length * 2);

            this.retired[this.retiredCount++] = new Retired
            {
                Table = table,
                RetireEpoch = retireEpoch,
                Owner = owner,
            };
        }

        this.TryCollectRetired();
    }

    private void TryCollectRetired()
    {
        if (this.retiredCount == 0)
            return;

        var minEpoch = this.GetMinActiveEpoch();

        lock (this.retiredLock)
        {
            var write = 0;

            for (var i = 0; i < this.retiredCount; i++)
            {
                var item = this.retired[i];

                if (item.Table == null)
                    continue;

                if (minEpoch == long.MaxValue || item.RetireEpoch < minEpoch)
                    DestroyTable(item.Table, item.Owner);
                else
                    this.retired[write++] = item;
            }

            this.retiredCount = write;
        }
    }

    private long GetMinActiveEpoch()
    {
        if (this.qsbr.SlotCount == 0 || this.qsbr.Slots == null)
            return long.MaxValue;

        var min = long.MaxValue;

        for (var i = 0; i < this.qsbr.SlotCount; i++)
        {
            var epoch = Volatile.Read(ref this.SlotAt(i));

            if (epoch != 0 && epoch < min)
                min = epoch;
        }

        return min;
    }

    private int TryAcquireSlot()
    {
        if (this.slotWords == null || this.slotWordCount == 0)
            return -1;

        for (var wordIndex = 0; wordIndex < this.slotWordCount; wordIndex++)
        {
            var word = Volatile.Read(ref this.slotWords[wordIndex]);

            if (~word == 0)
                continue;

            var free = BitOperations.TrailingZeroCount(~(ulong)word);

            if (free >= 64)
                continue;

            var slotIndex = (wordIndex << 6) + free;

            if (slotIndex >= this.qsbr.SlotCount)
                continue;

            if (AtomicVm.TryAcquireBit(ref this.slotWords[wordIndex], free) < 64)
                return slotIndex;
        }

        return -1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int GetThreadSlotIndex()
    {
        if (this.slotCaches.Length == 0)
            return -1;

        var threadId = Environment.CurrentManagedThreadId;
        var mask = (uint)this.slotCacheMask;
        var start = (uint)threadId & mask;

        for (var probe = 0; probe <= mask; probe++)
        {
            ref var cache = ref this.slotCaches[(int)((start + (uint)probe) & mask)];
            var cachedThreadId = Volatile.Read(ref cache.ThreadId);

            if (cachedThreadId == threadId)
                return cache.SlotIndex;

            if (cachedThreadId == 0)
            {
                if (Interlocked.CompareExchange(ref cache.ThreadId, -threadId, 0) != 0)
                    continue;

                var slotIndex = this.TryAcquireSlot();

                if (slotIndex >= 0)
                {
                    cache.SlotIndex = slotIndex;
                    Volatile.Write(ref cache.ThreadId, threadId);

                    return slotIndex;
                }

                Volatile.Write(ref cache.ThreadId, 0);

                return -1;
            }
        }

        return -1;
    }

    private int AcquireSlot()
    {
        var sw = new SpinWait();

        while (true)
        {
            var slot = this.TryAcquireSlot();

            if (slot >= 0)
                return slot;

            sw.SpinOnce();
        }
    }

    private void ReleaseSlot(int slotIndex)
    {
        if (slotIndex < 0 || this.slotWords == null)
            return;

        if (this.qsbr.Slots != null)
            Volatile.Write(ref this.SlotAt(slotIndex), 0);

        var wordIndex = slotIndex >> 6;
        var bit = slotIndex & 63;

        AtomicVm.ReleaseBit(ref this.slotWords[wordIndex], bit);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnterRead(int slotIndex)
    {
        if (slotIndex < 0 || this.qsbr.Slots == null)
            return;

        var epoch = Volatile.Read(ref this.qsbr.GlobalEpoch);
        Volatile.Write(ref this.SlotAt(slotIndex), epoch);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ref long SlotAt(int slotIndex) =>
        ref Unsafe.Add(ref Unsafe.AsRef<long>(this.qsbr.Slots), slotIndex * this.qsbr.SlotStride);

    private void AcquireLock(int shardIndex, ulong hash)
    {
        if (this.locks.LockScheme == LockScheme.Matrix2D)
        {
            var lockHash = MixLock(hash);
            var row = (int)(lockHash & 63);
            var col = (int)((lockHash >> 6) & 63);

            this.AcquireBit(ref this.locks.LockWords[0], row, this.locks.SlowLocks[0], ref this.locks.SlowWaiters[0]);
            this.AcquireBit(ref this.locks.LockWords[1], col, this.locks.SlowLocks[1], ref this.locks.SlowWaiters[1]);
        }
        else
        {
            var lockIndex = this.shards[shardIndex].LockIndex;
            var wordIndex = lockIndex >> 6;
            var bit = lockIndex & 63;

            this.AcquireBit(ref this.locks.LockWords[wordIndex], bit, this.locks.SlowLocks[wordIndex], ref this.locks.SlowWaiters[wordIndex]);
        }
    }

    private void ReleaseLock(int shardIndex, ulong hash)
    {
        if (this.locks.LockScheme == LockScheme.Matrix2D)
        {
            var lockHash = MixLock(hash);
            var row = (int)(lockHash & 63);
            var col = (int)((lockHash >> 6) & 63);

            ReleaseBit(ref this.locks.LockWords[1], col, this.locks.SlowLocks[1], ref this.locks.SlowWaiters[1]);
            ReleaseBit(ref this.locks.LockWords[0], row, this.locks.SlowLocks[0], ref this.locks.SlowWaiters[0]);
        }
        else
        {
            var lockIndex = this.shards[shardIndex].LockIndex;
            var wordIndex = lockIndex >> 6;
            var bit = lockIndex & 63;

            ReleaseBit(ref this.locks.LockWords[wordIndex], bit, this.locks.SlowLocks[wordIndex], ref this.locks.SlowWaiters[wordIndex]);
        }
    }

    private bool TryAcquireLock(int shardIndex, ulong hash)
    {
        if (this.locks.LockScheme == LockScheme.Matrix2D)
        {
            var lockHash = MixLock(hash);
            var row = (int)(lockHash & 63);
            var col = (int)((lockHash >> 6) & 63);

            if (AtomicVm.TryAcquireBit(ref this.locks.LockWords[0], row) >= 64)
                return false;

            if (AtomicVm.TryAcquireBit(ref this.locks.LockWords[1], col) < 64)
                return true;

            AtomicVm.ReleaseBit(ref this.locks.LockWords[0], row);

            return false;
        }

        var lockIndex = this.shards[shardIndex].LockIndex;
        var wordIndex = lockIndex >> 6;
        var bit = lockIndex & 63;

        return AtomicVm.TryAcquireBit(ref this.locks.LockWords[wordIndex], bit) < 64;
    }

    private void AcquireBit(ref long word, int bitIndex, object slowLock, ref int slowWaiter)
    {
        var mask = 1L << (bitIndex & 63);
        var sw = new SpinWait();

        for (var i = 0; i < this.locks.SpinIters; i++)
        {
            if ((word & mask) == 0 && AtomicVm.TryAcquireBit(ref word, bitIndex) < 64)
                return;

            sw.SpinOnce();
        }

        if (this.locks.ContentionMode == ContentionMode.SpinOnly)
        {
            for (var i = 0; i < this.locks.SlowPathIters; i++)
            {
                if ((word & mask) == 0 && AtomicVm.TryAcquireBit(ref word, bitIndex) < 64)
                    return;

                sw.SpinOnce();
            }

            while (true)
            {
                if ((word & mask) == 0 && AtomicVm.TryAcquireBit(ref word, bitIndex) < 64)
                    return;

                Thread.Yield();
            }
        }

        Interlocked.Increment(ref slowWaiter);

        try
        {
            lock (slowLock)
            {
                while (true)
                {
                    if ((word & mask) == 0 && AtomicVm.TryAcquireBit(ref word, bitIndex) < 64)
                        return;

                    Monitor.Wait(slowLock);
                }
            }
        }
        finally
        {
            Interlocked.Decrement(ref slowWaiter);
        }
    }

    private static void ReleaseBit(ref long word, int bitIndex, object slowLock, ref int slowWaiter)
    {
        AtomicVm.ReleaseBit(ref word, bitIndex);

        if (Volatile.Read(ref slowWaiter) > 0)
        {
            lock (slowLock)
                Monitor.Pulse(slowLock);
        }
    }

    private void ProcessCombineQueue(ref Shard shard)
    {
        var slots = shard.CombineSlots;

        if (slots == null || slots.Length == 0)
            return;

        while (true)
        {
            var processed = false;

            for (var i = 0; i < slots.Length; i++)
            {
                ref var slot = ref slots[i];

                if (Volatile.Read(ref slot.State) != combineReady)
                    continue;

                if (Interlocked.CompareExchange(ref slot.State, combineProcessing, combineReady) != combineReady)
                    continue;

                processed = true;
                this.ExecuteCombineRequest(ref shard, ref slot);
                Volatile.Write(ref slot.State, combineDone);
            }

            if (!processed)
                break;
        }
    }

    private void ExecuteCombineRequest(ref Shard shard, ref CombineRequest slot)
    {
        var key = slot.Key;
        var hash = HashKey(in key);
        var h2 = H2(hash);

        switch (slot.Op)
        {
            case CombineOp.TryAddValue:
                slot.ResultBool = this.TryAddLocked(in key, in slot.Value, hash, h2, ref shard);

                break;
            case CombineOp.TryAddFactory:
                slot.ResultBool = this.TryAddLocked(in key, slot.ValueFactory!, hash, h2, ref shard);

                break;
            case CombineOp.TryRemove:
                slot.ResultBool = this.TryRemoveLocked(in key, hash, h2, ref shard, out slot.ResultValue);

                break;
            case CombineOp.TryUpdate:
                slot.ResultBool = this.TryUpdateLocked(in key, in slot.Value, in slot.Comparison, hash, h2, ref shard);

                break;
            case CombineOp.GetOrAddValue:
                slot.ResultValue = this.GetOrAddLocked(in key, in slot.Value, hash, h2, ref shard);

                break;
            case CombineOp.GetOrAddFactory:
                slot.ResultValue = this.GetOrAddLocked(in key, slot.ValueFactory!, hash, h2, ref shard);

                break;
            case CombineOp.AddOrUpdateValue:
                slot.ResultValue = this.AddOrUpdateLocked(in key, in slot.Value, slot.UpdateFactory!, hash, h2, ref shard);

                break;
            case CombineOp.AddOrUpdateFactory:
                slot.ResultValue = this.AddOrUpdateLocked(in key, slot.ValueFactory!, slot.UpdateFactory!, hash, h2, ref shard);

                break;
        }
    }

    private static int TryPublishCombineRequest(ref Shard shard, ref CombineRequest request, int start)
    {
        var slots = shard.CombineSlots;

        if (slots == null || slots.Length == 0)
            return -1;

        var mask = shard.CombineSlotMask;

        for (var i = 0; i < slots.Length; i++)
        {
            var index = (start + i) & mask;
            ref var slot = ref slots[index];

            if (Volatile.Read(ref slot.State) != combineFree)
                continue;

            if (Interlocked.CompareExchange(ref slot.State, combinePublishing, combineFree) != combineFree)
                continue;

            slot.Op = request.Op;
            slot.Key = request.Key;
            slot.Value = request.Value;
            slot.Comparison = request.Comparison;
            slot.ValueFactory = request.ValueFactory;
            slot.UpdateFactory = request.UpdateFactory;

            Volatile.Write(ref slot.State, combineReady);

            return index;
        }

        return -1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WaitForCombineDone(ref CombineRequest slot)
    {
        var sw = new SpinWait();
        var yields = 0;

        while (Volatile.Read(ref slot.State) != combineDone)
        {
            sw.SpinOnce();

            if (!sw.NextSpinWillYield)
                continue;

            if (yields < 4)
                System.Threading.Thread.Yield();
            else
                System.Threading.Thread.Sleep(0);

            yields++;
        }
    }

    private static bool WaitForCombineBool(ref CombineRequest slot)
    {
        WaitForCombineDone(ref slot);

        var result = slot.ResultBool;
        slot.ValueFactory = null;
        slot.UpdateFactory = null;
        Volatile.Write(ref slot.State, combineFree);

        return result;
    }

    private static bool WaitForCombineBoolValue(ref CombineRequest slot, out TValue value)
    {
        WaitForCombineDone(ref slot);

        var result = slot.ResultBool;
        value = slot.ResultValue;
        slot.ValueFactory = null;
        slot.UpdateFactory = null;
        Volatile.Write(ref slot.State, combineFree);

        return result;
    }

    private static TValue WaitForCombineValue(ref CombineRequest slot)
    {
        WaitForCombineDone(ref slot);

        var result = slot.ResultValue;
        slot.ValueFactory = null;
        slot.UpdateFactory = null;
        Volatile.Write(ref slot.State, combineFree);

        return result;
    }

    private enum ContentionMode
    {
        SpinOnly,
        SpinThenMonitor,
    }

    private enum LockScheme
    {
        Bitset,
        Matrix2D,
    }

    private readonly struct FastConcurrentDictionaryOptions(
        int shardCount = 256,
        int initialCapacityPerShard = 1024,
        float maxLoadFactor = 0.75f,
        float tombstoneRatio = 0.20f,
        int groupWidth = 16,
        int maxProbeGroups = 0,
        int spinIters = 128,
        int slowPathIters = 4096,
        ContentionMode contentionMode = ContentionMode.SpinThenMonitor,
        LockScheme lockScheme = LockScheme.Bitset,
        int maxSessions = 256,
        bool enableCombining = false,
        int combiningSlots = 64)
    {
        public readonly int ShardCount = shardCount;
        public readonly int InitialCapacityPerShard = initialCapacityPerShard;
        public readonly float MaxLoadFactor = maxLoadFactor;
        public readonly float TombstoneRatio = tombstoneRatio;
        public readonly int GroupWidth = groupWidth;
        public readonly int MaxProbeGroups = maxProbeGroups;

        public readonly int SpinIters = spinIters;
        public readonly int SlowPathIters = slowPathIters;
        public readonly ContentionMode ContentionMode = contentionMode;
        public readonly LockScheme LockScheme = lockScheme;
        public readonly int MaxSessions = maxSessions;
        public readonly bool EnableCombining = enableCombining;
        public readonly int CombiningSlots = combiningSlots;
    }

    private static class MemoryComparer<T> where T : unmanaged
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Equals(in T left, in T right)
        {
            if (typeof(T) == typeof(int))
                return Unsafe.As<T, int>(ref Unsafe.AsRef(in left)) == Unsafe.As<T, int>(ref Unsafe.AsRef(in right));

            if (typeof(T) == typeof(uint))
                return Unsafe.As<T, uint>(ref Unsafe.AsRef(in left)) == Unsafe.As<T, uint>(ref Unsafe.AsRef(in right));

            if (typeof(T) == typeof(long))
                return Unsafe.As<T, long>(ref Unsafe.AsRef(in left)) == Unsafe.As<T, long>(ref Unsafe.AsRef(in right));

            if (typeof(T) == typeof(ulong))
                return Unsafe.As<T, ulong>(ref Unsafe.AsRef(in left)) == Unsafe.As<T, ulong>(ref Unsafe.AsRef(in right));

            if (typeof(T) == typeof(short))
                return Unsafe.As<T, short>(ref Unsafe.AsRef(in left)) == Unsafe.As<T, short>(ref Unsafe.AsRef(in right));

            if (typeof(T) == typeof(ushort))
                return Unsafe.As<T, ushort>(ref Unsafe.AsRef(in left)) == Unsafe.As<T, ushort>(ref Unsafe.AsRef(in right));

            if (typeof(T) == typeof(byte))
                return Unsafe.As<T, byte>(ref Unsafe.AsRef(in left)) == Unsafe.As<T, byte>(ref Unsafe.AsRef(in right));

            if (typeof(T) == typeof(sbyte))
                return Unsafe.As<T, sbyte>(ref Unsafe.AsRef(in left)) == Unsafe.As<T, sbyte>(ref Unsafe.AsRef(in right));

            if (typeof(T) == typeof(char))
                return Unsafe.As<T, char>(ref Unsafe.AsRef(in left)) == Unsafe.As<T, char>(ref Unsafe.AsRef(in right));

            if (typeof(T) == typeof(nint))
                return Unsafe.As<T, nint>(ref Unsafe.AsRef(in left)) == Unsafe.As<T, nint>(ref Unsafe.AsRef(in right));

            if (typeof(T) == typeof(nuint))
                return Unsafe.As<T, nuint>(ref Unsafe.AsRef(in left)) == Unsafe.As<T, nuint>(ref Unsafe.AsRef(in right));

            if (typeof(T) == typeof(float))
                return Unsafe.As<T, int>(ref Unsafe.AsRef(in left)) == Unsafe.As<T, int>(ref Unsafe.AsRef(in right));

            if (typeof(T) == typeof(double))
                return Unsafe.As<T, long>(ref Unsafe.AsRef(in left)) == Unsafe.As<T, long>(ref Unsafe.AsRef(in right));

            var size = Unsafe.SizeOf<T>();

            if (size == 0)
                return true;

            ref var leftRef = ref Unsafe.As<T, byte>(ref Unsafe.AsRef(in left));
            ref var rightRef = ref Unsafe.As<T, byte>(ref Unsafe.AsRef(in right));

            var offset = 0;

            while (size - offset >= 8)
            {
                if (Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref leftRef, offset))
                    != Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref rightRef, offset)))
                    return false;

                offset += 8;
            }

            if (size - offset >= 4)
            {
                if (Unsafe.ReadUnaligned<uint>(ref Unsafe.Add(ref leftRef, offset))
                    != Unsafe.ReadUnaligned<uint>(ref Unsafe.Add(ref rightRef, offset)))
                    return false;

                offset += 4;
            }

            if (size - offset >= 2)
            {
                if (Unsafe.ReadUnaligned<ushort>(ref Unsafe.Add(ref leftRef, offset))
                    != Unsafe.ReadUnaligned<ushort>(ref Unsafe.Add(ref rightRef, offset)))
                    return false;

                offset += 2;
            }

            if (size - offset == 1)
                return Unsafe.Add(ref leftRef, offset) == Unsafe.Add(ref rightRef, offset);

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong Hash(in T value)
        {
            if (typeof(T) == typeof(int))
                return MixFast((uint)Unsafe.As<T, int>(ref Unsafe.AsRef(in value)));

            if (typeof(T) == typeof(uint))
                return MixFast(Unsafe.As<T, uint>(ref Unsafe.AsRef(in value)));

            if (typeof(T) == typeof(long))
                return MixFast((ulong)Unsafe.As<T, long>(ref Unsafe.AsRef(in value)));

            if (typeof(T) == typeof(ulong))
                return MixFast(Unsafe.As<T, ulong>(ref Unsafe.AsRef(in value)));

            if (typeof(T) == typeof(short))
                return MixFast((ushort)Unsafe.As<T, short>(ref Unsafe.AsRef(in value)));

            if (typeof(T) == typeof(ushort))
                return MixFast(Unsafe.As<T, ushort>(ref Unsafe.AsRef(in value)));

            if (typeof(T) == typeof(byte))
                return MixFast(Unsafe.As<T, byte>(ref Unsafe.AsRef(in value)));

            if (typeof(T) == typeof(sbyte))
                return MixFast((byte)Unsafe.As<T, sbyte>(ref Unsafe.AsRef(in value)));

            if (typeof(T) == typeof(bool))
                return MixFast((byte)(Unsafe.As<T, bool>(ref Unsafe.AsRef(in value)) ? 1 : 0));

            if (typeof(T) == typeof(char))
                return MixFast(Unsafe.As<T, char>(ref Unsafe.AsRef(in value)));

            if (typeof(T) == typeof(nint))
                return MixFast((ulong)(nuint)Unsafe.As<T, nint>(ref Unsafe.AsRef(in value)));

            if (typeof(T) == typeof(nuint))
                return MixFast((ulong)Unsafe.As<T, nuint>(ref Unsafe.AsRef(in value)));

            if (typeof(T) == typeof(float))
                return MixFast((uint)Unsafe.As<T, int>(ref Unsafe.AsRef(in value)));

            if (typeof(T) == typeof(double))
                return MixFast((ulong)Unsafe.As<T, long>(ref Unsafe.AsRef(in value)));

            var size = Unsafe.SizeOf<T>();

            if (size == 0)
                return 0;

            ref var data = ref Unsafe.As<T, byte>(ref Unsafe.AsRef(in value));

            return HashBytes(ref data, size);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong HashBytes(ref byte data, int length)
        {
            const ulong offset = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;

            var hash = offset;
            var offsetIndex = 0;

            while (length - offsetIndex >= 8)
            {
                hash ^= Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref data, offsetIndex));
                hash *= prime;
                offsetIndex += 8;
            }

            if (length - offsetIndex >= 4)
            {
                hash ^= Unsafe.ReadUnaligned<uint>(ref Unsafe.Add(ref data, offsetIndex));
                hash *= prime;
                offsetIndex += 4;
            }

            if (length - offsetIndex >= 2)
            {
                hash ^= Unsafe.ReadUnaligned<ushort>(ref Unsafe.Add(ref data, offsetIndex));
                hash *= prime;
                offsetIndex += 2;
            }

            if (length - offsetIndex == 1)
            {
                hash ^= Unsafe.Add(ref data, offsetIndex);
                hash *= prime;
            }

            return Mix(hash);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong MixFast(ulong value) => unchecked(value * 0x9E3779B97F4A7C15UL);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong Mix(ulong value)
        {
            unchecked
            {
                value ^= value >> 33;
                value *= 0xff51afd7ed558ccdUL;
                value ^= value >> 33;
                value *= 0xc4ceb9fe1a85ec53UL;
                value ^= value >> 33;

                return value;
            }
        }
    }

    private struct ThreadSlotCache
    {
        public int ThreadId;
        public int SlotIndex;
    }

    private enum CombineOp : byte
    {
        TryAddValue,
        TryAddFactory,
        TryRemove,
        TryUpdate,
        GetOrAddValue,
        GetOrAddFactory,
        AddOrUpdateValue,
        AddOrUpdateFactory,
    }

    private struct CombineRequest
    {
        public int State;
        public CombineOp Op;
        public TKey Key;
        public TValue Value;
        public TValue Comparison;
        public TValue ResultValue;
        public bool ResultBool;
        public ValueFactory? ValueFactory;
        public UpdateFactory? UpdateFactory;
    }

    private struct Shard
    {
        public nint TablePtr;
        public int Count;
        public int Tombstones;
        public int LockIndex;
        public object? Owner;
        public CombineRequest[]? CombineSlots;
        public int CombineSlotMask;
        public long Padding0;
        public long Padding1;
    }

    private struct Table
    {
        public byte* Ctrl;
        public Entry* Entries;
        public int Capacity;
        public int Mask;
        public int GroupWidth;
        public int MaxProbeGroups;
    }

    private struct Entry
    {
        public TKey Key;
        public TValue Value;
    }

    private struct Locks
    {
        public long* LockWords;
        public object[] SlowLocks;
        public int[] SlowWaiters;

        public int SpinIters;
        public int SlowPathIters;
        public ContentionMode ContentionMode;
        public LockScheme LockScheme;
        public int LockStrideWords;
    }

    private struct Qsbr
    {
        public long GlobalEpoch;
        public long* Slots;
        public int SlotCount;
        public int SlotStride;
    }

    private struct Retired
    {
        public Table* Table;
        public long RetireEpoch;
        public object? Owner;
    }
}
