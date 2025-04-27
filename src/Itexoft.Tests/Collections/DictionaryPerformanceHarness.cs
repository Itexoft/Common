// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Itexoft.Core;

namespace Itexoft.Tests.Collections;

internal sealed class DictionaryPerformanceHarness<TKey, TValue>
{
    public delegate bool TryGetHandler(in TKey key, out TValue value);
    public delegate bool TryAddHandler(in TKey key, in TValue value);
    public delegate bool TryRemoveHandler(in TKey key, out TValue value);
    public delegate bool TryUpdateHandler(in TKey key, in TValue newValue, in TValue comparisonValue);
    public delegate bool AddOrUpdateHandler(in TKey key, in TValue addValue, in TValue updateValue);
    public delegate bool GetOrAddHandler(in TKey key, in TValue addValue);

    private readonly Func<int, TKey> keyFactory;
    private readonly Func<int, TValue> valueFactory;

    private TryGetHandler? tryGet;
    private TryAddHandler? tryAdd;
    private TryRemoveHandler? tryRemove;
    private TryUpdateHandler? tryUpdate;
    private AddOrUpdateHandler? addOrUpdate;
    private GetOrAddHandler? getOrAdd;

    public DictionaryPerformanceHarness(Func<int, TKey> keyFactory, Func<int, TValue> valueFactory)
    {
        this.keyFactory = keyFactory ?? throw new ArgumentNullException(nameof(keyFactory));
        this.valueFactory = valueFactory ?? throw new ArgumentNullException(nameof(valueFactory));
    }

    public event TryGetHandler? TryGet
    {
        add => AddHandler(ref this.tryGet, value, nameof(this.TryGet));
        remove => RemoveHandler(ref this.tryGet, value);
    }

    public event TryAddHandler? TryAdd
    {
        add => AddHandler(ref this.tryAdd, value, nameof(this.TryAdd));
        remove => RemoveHandler(ref this.tryAdd, value);
    }

    public event TryRemoveHandler? TryRemove
    {
        add => AddHandler(ref this.tryRemove, value, nameof(this.TryRemove));
        remove => RemoveHandler(ref this.tryRemove, value);
    }

    public event TryUpdateHandler? TryUpdate
    {
        add => AddHandler(ref this.tryUpdate, value, nameof(this.TryUpdate));
        remove => RemoveHandler(ref this.tryUpdate, value);
    }

    public event AddOrUpdateHandler? AddOrUpdate
    {
        add => AddHandler(ref this.addOrUpdate, value, nameof(this.AddOrUpdate));
        remove => RemoveHandler(ref this.addOrUpdate, value);
    }

    public event GetOrAddHandler? GetOrAdd
    {
        add => AddHandler(ref this.getOrAdd, value, nameof(this.GetOrAdd));
        remove => RemoveHandler(ref this.getOrAdd, value);
    }

    public TResult Run<TResult>(Scenario scenario, long durationTimestampTicks, Func<PerfMetrics, TResult> metricsFactory, int workers = 100)
    {
        if (durationTimestampTicks <= 0)
            throw new ArgumentOutOfRangeException(nameof(durationTimestampTicks));

        if (workers <= 0)
            throw new ArgumentOutOfRangeException(nameof(workers));

        metricsFactory = metricsFactory ?? throw new ArgumentNullException(nameof(metricsFactory));
        scenario = scenario.Normalize();

        var handlers = this.EnsureHandlers(scenario);

        if (scenario.prefill > 0)
            this.Prefill(scenario, handlers.tryAdd);

        var counters = new Counters[workers];
        var threads = new Thread[workers];
        var startGate = new ManualResetEventSlim(false);
        var readyGate = new CountdownEvent(workers);
        var errors = new ConcurrentQueue<Exception>();
        var startTimestamp = 0L;
        var endTimestamp = 0L;
        var mix = scenario.mix;

        for (var i = 0; i < workers; i++)
        {
            var workerIndex = i;
            threads[i] = new Thread(
                () =>
                {
                    try
                    {
                        var local = new Counters();
                        var rng = new XorShift64(Seed(workerIndex));

                        readyGate.Signal();
                        startGate.Wait();

                        var ops = 0;
                        var keySpace = scenario.keySpace;
                        var hotKeySpace = scenario.hotKeySpace;
                        var hotKeyPercent = scenario.hotKeyPercent;
                        var timeCheckMask = scenario.timeCheckMask;

                        while (true)
                        {
                            if ((ops & timeCheckMask) == 0 && TimeUtils.RealTimestamp >= endTimestamp)
                                break;

                            ops++;

                            var op = mix.Pick(ref rng);
                            var keyIndex = SelectKey(ref rng, hotKeyPercent, hotKeySpace, keySpace);
                            var key = this.keyFactory(keyIndex);

                            switch (op)
                            {
                                case OperationKind.TryGet:
                                    local.tryGet++;

                                    if (handlers.tryGet(in key, out _))
                                        local.tryGetHit++;
                                    break;
                                case OperationKind.TryAdd:
                                {
                                    local.tryAdd++;
                                    var value = this.valueFactory(keyIndex);

                                    if (handlers.tryAdd(in key, in value))
                                        local.tryAddOk++;
                                    break;
                                }
                                case OperationKind.TryRemove:
                                    local.tryRemove++;

                                    if (handlers.tryRemove(in key, out _))
                                        local.tryRemoveOk++;
                                    break;
                                case OperationKind.TryUpdate:
                                {
                                    local.tryUpdate++;
                                    var comparisonValue = this.valueFactory(keyIndex);
                                    var newValue = this.valueFactory(unchecked(keyIndex ^ 0x5a5a5a5a));

                                    if (handlers.tryUpdate(in key, in newValue, in comparisonValue))
                                        local.tryUpdateOk++;
                                    break;
                                }
                                case OperationKind.AddOrUpdate:
                                {
                                    local.addOrUpdate++;
                                    var addValue = this.valueFactory(keyIndex);
                                    var updateValue = this.valueFactory(unchecked(keyIndex ^ 0x5a5a5a5a));

                                    if (handlers.addOrUpdate(in key, in addValue, in updateValue))
                                        local.addOrUpdateAdd++;
                                    else
                                        local.addOrUpdateUpdate++;
                                    break;
                                }
                                case OperationKind.GetOrAdd:
                                {
                                    local.getOrAdd++;
                                    var addValue = this.valueFactory(keyIndex);

                                    if (handlers.getOrAdd(in key, in addValue))
                                        local.getOrAddAdd++;
                                    else
                                        local.getOrAddHit++;
                                    break;
                                }
                                default:
                                    throw new ArgumentOutOfRangeException(nameof(op));
                            }
                        }

                        counters[workerIndex] = local;
                    }
                    catch (Exception ex)
                    {
                        errors.Enqueue(ex);
                    }
                })
            {
                IsBackground = true,
                Name = $"DictionaryPerfWorker-{workerIndex}",
            };
        }

        for (var i = 0; i < threads.Length; i++)
            threads[i].Start();

        readyGate.Wait();
        startTimestamp = TimeUtils.RealTimestamp;
        endTimestamp = startTimestamp + durationTimestampTicks;
        startGate.Set();

        for (var i = 0; i < threads.Length; i++)
            threads[i].Join();

        if (!errors.IsEmpty)
            throw new AggregateException(errors);

        var finishTimestamp = TimeUtils.RealTimestamp;
        var metrics = PerfMetrics.FromScenario(scenario, workers, startTimestamp, finishTimestamp, counters);

        return metricsFactory(metrics);
    }

    public TResult Run<TResult>(Scenario scenario, TimeSpan duration, Func<PerfMetrics, TResult> metricsFactory, int workers = 100) =>
        this.Run(scenario, TimeUtils.ToTimestampTicks(duration), metricsFactory, workers);

    private static void AddHandler<T>(ref T? target, T? handler, string name) where T : class
    {
        if (handler is null)
            return;

        if (Interlocked.CompareExchange(ref target, handler, null) is not null)
            throw new InvalidOperationException($"Handler already registered for {name}.");
    }

    private static void RemoveHandler<T>(ref T? target, T? handler) where T : class
    {
        if (handler is null)
            return;

        Interlocked.CompareExchange(ref target, null, handler);
    }

    private void Prefill(Scenario scenario, TryAddHandler tryAddHandler)
    {
        for (var i = 0; i < scenario.prefill; i++)
        {
            var key = this.keyFactory(i);
            var value = this.valueFactory(i);

            tryAddHandler(in key, in value);
        }
    }

    private Handlers EnsureHandlers(Scenario scenario)
    {
        if (scenario.prefill > 0 && this.tryAdd is null)
            throw new InvalidOperationException("TryAdd handler must be registered for prefill.");

        if (scenario.mix.Requires(OperationKind.TryGet) && this.tryGet is null)
            throw new InvalidOperationException("TryGet handler must be registered.");

        if (scenario.mix.Requires(OperationKind.TryAdd) && this.tryAdd is null)
            throw new InvalidOperationException("TryAdd handler must be registered.");

        if (scenario.mix.Requires(OperationKind.TryRemove) && this.tryRemove is null)
            throw new InvalidOperationException("TryRemove handler must be registered.");

        if (scenario.mix.Requires(OperationKind.TryUpdate) && this.tryUpdate is null)
            throw new InvalidOperationException("TryUpdate handler must be registered.");

        if (scenario.mix.Requires(OperationKind.AddOrUpdate) && this.addOrUpdate is null)
            throw new InvalidOperationException("AddOrUpdate handler must be registered.");

        if (scenario.mix.Requires(OperationKind.GetOrAdd) && this.getOrAdd is null)
            throw new InvalidOperationException("GetOrAdd handler must be registered.");

        return new Handlers(
            this.tryGet!,
            this.tryAdd!,
            this.tryRemove!,
            this.tryUpdate!,
            this.addOrUpdate!,
            this.getOrAdd!);
    }

    internal static int SelectKey(ref XorShift64 rng, int hotKeyPercent, int hotKeySpace, int keySpace)
    {
        if (hotKeyPercent > 0 && hotKeySpace > 0 && rng.NextInt(100) < hotKeyPercent)
            return rng.NextInt(hotKeySpace);

        return rng.NextInt(keySpace);
    }

    internal static ulong Seed(int workerIndex)
    {
        var baseSeed = (ulong)(workerIndex + 1) * 0x9E3779B97F4A7C15UL;
        return baseSeed ^ 0xD1B54A32D192ED03UL;
    }

    private readonly struct Handlers(
        TryGetHandler tryGet,
        TryAddHandler tryAdd,
        TryRemoveHandler tryRemove,
        TryUpdateHandler tryUpdate,
        AddOrUpdateHandler addOrUpdate,
        GetOrAddHandler getOrAdd)
    {
        public readonly TryGetHandler tryGet = tryGet;
        public readonly TryAddHandler tryAdd = tryAdd;
        public readonly TryRemoveHandler tryRemove = tryRemove;
        public readonly TryUpdateHandler tryUpdate = tryUpdate;
        public readonly AddOrUpdateHandler addOrUpdate = addOrUpdate;
        public readonly GetOrAddHandler getOrAdd = getOrAdd;
    }

    internal struct Counters
    {
        public long tryGet;
        public long tryGetHit;
        public long tryAdd;
        public long tryAddOk;
        public long tryRemove;
        public long tryRemoveOk;
        public long tryUpdate;
        public long tryUpdateOk;
        public long addOrUpdate;
        public long addOrUpdateAdd;
        public long addOrUpdateUpdate;
        public long getOrAdd;
        public long getOrAddAdd;
        public long getOrAddHit;
    }

    internal enum OperationKind
    {
        TryGet,
        TryAdd,
        TryRemove,
        TryUpdate,
        AddOrUpdate,
        GetOrAdd,
    }

    internal readonly struct OperationMix
    {
        private readonly OperationKind[] kinds;
        private readonly int[] thresholds;
        private readonly int totalWeight;

        public OperationMix(params (OperationKind kind, int weight)[] operations)
        {
            if (operations is null || operations.Length == 0)
                throw new ArgumentException("Operation mix cannot be empty.", nameof(operations));

            this.kinds = new OperationKind[operations.Length];
            this.thresholds = new int[operations.Length];

            var sum = 0;

            for (var i = 0; i < operations.Length; i++)
            {
                var (kind, weight) = operations[i];

                if (weight <= 0)
                    throw new ArgumentOutOfRangeException(nameof(operations));

                sum += weight;
                this.kinds[i] = kind;
                this.thresholds[i] = sum;
            }

            if (sum <= 0)
                throw new ArgumentOutOfRangeException(nameof(operations));

            this.totalWeight = sum;
        }

        internal OperationKind Pick(ref XorShift64 rng)
        {
            var value = rng.NextInt(this.totalWeight);

            for (var i = 0; i < this.thresholds.Length; i++)
            {
                if (value < this.thresholds[i])
                    return this.kinds[i];
            }

            return this.kinds[^1];
        }

        internal bool Requires(OperationKind kind)
        {
            for (var i = 0; i < this.kinds.Length; i++)
            {
                if (this.kinds[i] == kind)
                    return true;
            }

            return false;
        }
    }

    internal readonly struct Scenario
    {
        public readonly string name;
        public readonly int keySpace;
        public readonly int prefill;
        public readonly int hotKeySpace;
        public readonly int hotKeyPercent;
        public readonly int timeCheckMask;
        public readonly OperationMix mix;

        public Scenario(
            string name,
            int keySpace,
            int prefill,
            OperationMix mix,
            int hotKeySpace = 0,
            int hotKeyPercent = 0,
            int timeCheckMask = 1023)
        {
            this.name = name ?? throw new ArgumentNullException(nameof(name));
            this.keySpace = keySpace;
            this.prefill = prefill;
            this.hotKeySpace = hotKeySpace;
            this.hotKeyPercent = hotKeyPercent;
            this.timeCheckMask = timeCheckMask;
            this.mix = mix;
        }

        public Scenario Normalize()
        {
            var normalizedKeySpace = Math.Max(1, this.keySpace);
            var normalizedPrefill = Math.Clamp(this.prefill, 0, normalizedKeySpace);
            var normalizedHotKeySpace = Math.Clamp(this.hotKeySpace, 0, normalizedKeySpace);
            var normalizedHotKeyPercent = Math.Clamp(this.hotKeyPercent, 0, 100);
            var normalizedMask = this.timeCheckMask < 0 ? 0 : this.timeCheckMask;

            return new Scenario(
                this.name,
                normalizedKeySpace,
                normalizedPrefill,
                this.mix,
                normalizedHotKeySpace,
                normalizedHotKeyPercent,
                normalizedMask);
        }

        public static Scenario ReadMostly(int keySpace = 1_000_000, int prefill = 900_000) =>
            new(
                "read-mostly",
                keySpace,
                prefill,
                new OperationMix(
                    (OperationKind.TryGet, 90),
                    (OperationKind.TryAdd, 4),
                    (OperationKind.TryUpdate, 4),
                    (OperationKind.TryRemove, 2)));

        public static Scenario MixedChurn(int keySpace = 200_000, int prefill = 100_000) =>
            new(
                "mixed-churn",
                keySpace,
                prefill,
                new OperationMix(
                    (OperationKind.TryGet, 40),
                    (OperationKind.TryAdd, 20),
                    (OperationKind.TryRemove, 20),
                    (OperationKind.AddOrUpdate, 20)));

        public static Scenario HotSpotContention(int keySpace = 100_000, int prefill = 80_000, int hotKeySpace = 1024) =>
            new(
                "hotspot-contention",
                keySpace,
                prefill,
                new OperationMix(
                    (OperationKind.TryGet, 60),
                    (OperationKind.AddOrUpdate, 20),
                    (OperationKind.TryUpdate, 10),
                    (OperationKind.TryRemove, 10)),
                hotKeySpace,
                hotKeyPercent: 90);

        public static Scenario WriteHeavy(int keySpace = 500_000) =>
            new(
                "write-heavy",
                keySpace,
                prefill: 0,
                new OperationMix(
                    (OperationKind.TryGet, 15),
                    (OperationKind.TryAdd, 40),
                    (OperationKind.TryRemove, 20),
                    (OperationKind.AddOrUpdate, 25)));

        public static Scenario GetOrAddHeavy(int keySpace = 400_000, int prefill = 200_000) =>
            new(
                "get-or-add-heavy",
                keySpace,
                prefill,
                new OperationMix(
                    (OperationKind.GetOrAdd, 70),
                    (OperationKind.TryGet, 30)));

        public static IReadOnlyList<Scenario> DefaultScenarios =>
        [
            ReadMostly(),
            MixedChurn(),
            HotSpotContention(),
            WriteHeavy(),
            GetOrAddHeavy(),
        ];
    }

    internal readonly struct PerfMetrics
    {
        public readonly string scenarioName;
        public readonly int workers;
        public readonly int keySpace;
        public readonly int prefill;
        public readonly long startTimestamp;
        public readonly long endTimestamp;
        public readonly long elapsedTimestampTicks;
        public readonly long elapsedTimeSpanTicks;
        public readonly double elapsedSeconds;
        public readonly long totalOps;
        public readonly double opsPerSecond;
        public readonly long tryGet;
        public readonly long tryGetHit;
        public readonly long tryAdd;
        public readonly long tryAddOk;
        public readonly long tryRemove;
        public readonly long tryRemoveOk;
        public readonly long tryUpdate;
        public readonly long tryUpdateOk;
        public readonly long addOrUpdate;
        public readonly long addOrUpdateAdd;
        public readonly long addOrUpdateUpdate;
        public readonly long getOrAdd;
        public readonly long getOrAddAdd;
        public readonly long getOrAddHit;

        private PerfMetrics(
            string scenarioName,
            int workers,
            int keySpace,
            int prefill,
            long startTimestamp,
            long endTimestamp,
            long elapsedTimestampTicks,
            long elapsedTimeSpanTicks,
            double elapsedSeconds,
            long totalOps,
            double opsPerSecond,
            long tryGet,
            long tryGetHit,
            long tryAdd,
            long tryAddOk,
            long tryRemove,
            long tryRemoveOk,
            long tryUpdate,
            long tryUpdateOk,
            long addOrUpdate,
            long addOrUpdateAdd,
            long addOrUpdateUpdate,
            long getOrAdd,
            long getOrAddAdd,
            long getOrAddHit)
        {
            this.scenarioName = scenarioName;
            this.workers = workers;
            this.keySpace = keySpace;
            this.prefill = prefill;
            this.startTimestamp = startTimestamp;
            this.endTimestamp = endTimestamp;
            this.elapsedTimestampTicks = elapsedTimestampTicks;
            this.elapsedTimeSpanTicks = elapsedTimeSpanTicks;
            this.elapsedSeconds = elapsedSeconds;
            this.totalOps = totalOps;
            this.opsPerSecond = opsPerSecond;
            this.tryGet = tryGet;
            this.tryGetHit = tryGetHit;
            this.tryAdd = tryAdd;
            this.tryAddOk = tryAddOk;
            this.tryRemove = tryRemove;
            this.tryRemoveOk = tryRemoveOk;
            this.tryUpdate = tryUpdate;
            this.tryUpdateOk = tryUpdateOk;
            this.addOrUpdate = addOrUpdate;
            this.addOrUpdateAdd = addOrUpdateAdd;
            this.addOrUpdateUpdate = addOrUpdateUpdate;
            this.getOrAdd = getOrAdd;
            this.getOrAddAdd = getOrAddAdd;
            this.getOrAddHit = getOrAddHit;
        }

        public long TryGetMiss => this.tryGet - this.tryGetHit;
        public long TryAddFail => this.tryAdd - this.tryAddOk;
        public long TryRemoveFail => this.tryRemove - this.tryRemoveOk;
        public long TryUpdateFail => this.tryUpdate - this.tryUpdateOk;

        internal static PerfMetrics FromScenario(Scenario scenario, int workers, long startTimestamp, long endTimestamp, Counters[] counters)
        {
            var total = new Counters();

            for (var i = 0; i < counters.Length; i++)
            {
                ref var local = ref counters[i];
                total.tryGet += local.tryGet;
                total.tryGetHit += local.tryGetHit;
                total.tryAdd += local.tryAdd;
                total.tryAddOk += local.tryAddOk;
                total.tryRemove += local.tryRemove;
                total.tryRemoveOk += local.tryRemoveOk;
                total.tryUpdate += local.tryUpdate;
                total.tryUpdateOk += local.tryUpdateOk;
                total.addOrUpdate += local.addOrUpdate;
                total.addOrUpdateAdd += local.addOrUpdateAdd;
                total.addOrUpdateUpdate += local.addOrUpdateUpdate;
                total.getOrAdd += local.getOrAdd;
                total.getOrAddAdd += local.getOrAddAdd;
                total.getOrAddHit += local.getOrAddHit;
            }

            var elapsedTimestampTicks = endTimestamp - startTimestamp;
            var elapsedTimeSpanTicks = TimeUtils.ToTimeSpanTicks(elapsedTimestampTicks);
            var elapsedSeconds = elapsedTimeSpanTicks / (double)TimeSpan.TicksPerSecond;
            var totalOps = total.tryGet + total.tryAdd + total.tryRemove + total.tryUpdate + total.addOrUpdate + total.getOrAdd;
            var opsPerSecond = elapsedSeconds > 0 ? totalOps / elapsedSeconds : 0;

            return new PerfMetrics(
                scenario.name,
                workers,
                scenario.keySpace,
                scenario.prefill,
                startTimestamp,
                endTimestamp,
                elapsedTimestampTicks,
                elapsedTimeSpanTicks,
                elapsedSeconds,
                totalOps,
                opsPerSecond,
                total.tryGet,
                total.tryGetHit,
                total.tryAdd,
                total.tryAddOk,
                total.tryRemove,
                total.tryRemoveOk,
                total.tryUpdate,
                total.tryUpdateOk,
                total.addOrUpdate,
                total.addOrUpdateAdd,
                total.addOrUpdateUpdate,
                total.getOrAdd,
                total.getOrAddAdd,
                total.getOrAddHit);
        }
    }

    internal struct XorShift64
    {
        private ulong state;

        public XorShift64(ulong seed)
        {
            this.state = seed == 0 ? 0xA24BAED4963EE407UL : seed;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ulong Next()
        {
            var x = this.state;
            x ^= x << 7;
            x ^= x >> 9;
            x ^= x << 8;
            this.state = x;
            return x;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int NextInt(int maxExclusive) => (int)(this.Next() % (uint)maxExclusive);
    }
}
