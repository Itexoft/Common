// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Collections.Concurrent;
using Itexoft.Core;
using Itexoft.Tests.Diagnostics;
using AtomicTestDictionary = Itexoft.Collections.AtomicDictionaryTest<int, int>;

namespace Itexoft.Tests.Collections;

internal sealed class DictionaryPerformanceTests
{
    [Test]
    [Category("Performance")]
    public void ConcurrentDictionary_Vs_AtomicDictionaryTest()
    {
#if !RELEASE
        Assert.Ignore("Тест производительности требует Release сборки.");
#endif

        var duration = TimeSpan.FromSeconds(20);
        var workers = 100;
        var scenarios = DictionaryPerformanceHarness<int, int>.Scenario.DefaultScenarios;

        Console.WriteLine($"Dictionary performance: duration={duration.TotalSeconds:F1}s, workers={workers}");

        foreach (var scenario in scenarios)
        {
            var concurrentMetrics = RunConcurrentDictionaryScenario(scenario, duration, workers);
            var atomicMetrics = RunAtomicDictionaryScenario(scenario, duration, workers);

            PrintScenario(scenario, concurrentMetrics, atomicMetrics);
        }
    }

    private static DictionaryPerformanceHarness<int, int>.PerfMetrics RunConcurrentDictionaryScenario(
        DictionaryPerformanceHarness<int, int>.Scenario scenario,
        TimeSpan duration,
        int workers)
    {
        var capacity = Math.Max(1, scenario.prefill);
        var dictionary = new ConcurrentDictionary<int, int>(workers, capacity);
        var harness = new DictionaryPerformanceHarness<int, int>(KeyFactory, ValueFactory);

        harness.TryGet += (in key, out value) => dictionary.TryGetValue(key, out value);
        harness.TryAdd += (in key, in value) => dictionary.TryAdd(key, value);
        harness.TryRemove += (in key, out value) => dictionary.TryRemove(key, out value);
        harness.TryUpdate += (in key, in newValue, in comparisonValue) => dictionary.TryUpdate(key, newValue, comparisonValue);
        harness.AddOrUpdate += (in key, in addValue, in updateValue) =>
            ConcurrentAddOrUpdate(dictionary, in key, in addValue, in updateValue);
        harness.GetOrAdd += (in key, in addValue) =>
            ConcurrentGetOrAdd(dictionary, in key, in addValue);

        return harness.Run(scenario, duration, static metrics => metrics, workers);
    }

    private static DictionaryPerformanceHarness<int, int>.PerfMetrics RunAtomicDictionaryScenario(
        DictionaryPerformanceHarness<int, int>.Scenario scenario,
        TimeSpan duration,
        int workers)
    {
        var durationTicks = TimeUtils.ToTimestampTicks(duration);

        if (durationTicks <= 0)
            throw new ArgumentOutOfRangeException(nameof(duration));

        scenario = scenario.Normalize();

        var dictionary = new AtomicTestDictionary();

        if (scenario.prefill > 0)
        {
            for (var i = 0; i < scenario.prefill; i++)
            {
                var key = KeyFactory(i);
                var value = ValueFactory(i);
                dictionary.TryAdd(in key, in value);
            }
        }

        var counters = new DictionaryPerformanceHarness<int, int>.Counters[workers];
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
                        var local = new DictionaryPerformanceHarness<int, int>.Counters();
                        var rng = new DictionaryPerformanceHarness<int, int>.XorShift64(
                            DictionaryPerformanceHarness<int, int>.Seed(workerIndex));

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
                            var keyIndex = DictionaryPerformanceHarness<int, int>.SelectKey(
                                ref rng,
                                hotKeyPercent,
                                hotKeySpace,
                                keySpace);
                            var key = KeyFactory(keyIndex);

                            switch (op)
                            {
                                case DictionaryPerformanceHarness<int, int>.OperationKind.TryGet:
                                    local.tryGet++;

                                    if (dictionary.TryGet(in key, out _))
                                        local.tryGetHit++;
                                    break;
                                case DictionaryPerformanceHarness<int, int>.OperationKind.TryAdd:
                                {
                                    local.tryAdd++;
                                    var value = ValueFactory(keyIndex);

                                    if (dictionary.TryAdd(in key, in value))
                                        local.tryAddOk++;
                                    break;
                                }
                                case DictionaryPerformanceHarness<int, int>.OperationKind.TryRemove:
                                    local.tryRemove++;

                                    if (dictionary.TryRemove(in key, out _))
                                        local.tryRemoveOk++;
                                    break;
                                case DictionaryPerformanceHarness<int, int>.OperationKind.TryUpdate:
                                {
                                    local.tryUpdate++;
                                    var comparisonValue = ValueFactory(keyIndex);
                                    var newValue = ValueFactory(unchecked(keyIndex ^ 0x5a5a5a5a));

                                    if (dictionary.TryUpdate(in key, in newValue, in comparisonValue))
                                        local.tryUpdateOk++;
                                    break;
                                }
                                case DictionaryPerformanceHarness<int, int>.OperationKind.AddOrUpdate:
                                {
                                    local.addOrUpdate++;
                                    var addValue = ValueFactory(keyIndex);
                                    var updateValue = ValueFactory(unchecked(keyIndex ^ 0x5a5a5a5a));

                                    if (AtomicAddOrUpdate(dictionary, in key, in addValue, in updateValue))
                                        local.addOrUpdateAdd++;
                                    else
                                        local.addOrUpdateUpdate++;
                                    break;
                                }
                                case DictionaryPerformanceHarness<int, int>.OperationKind.GetOrAdd:
                                {
                                    local.getOrAdd++;
                                    var addValue = ValueFactory(keyIndex);

                                    if (AtomicGetOrAdd(dictionary, in key, in addValue))
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
                Name = $"AtomicDictionaryPerfWorker-{workerIndex}",
            };
        }

        for (var i = 0; i < threads.Length; i++)
            threads[i].Start();

        PerfTraceSession? trace = null;

        try
        {
            readyGate.Wait();
            trace = PerfTraceSession.Start($"Atomic-{scenario.name}");
            startTimestamp = TimeUtils.RealTimestamp;
            endTimestamp = startTimestamp + durationTicks;
            startGate.Set();

            for (var i = 0; i < threads.Length; i++)
                threads[i].Join();
        }
        finally
        {
            trace?.Dispose();
        }

        if (!errors.IsEmpty)
            throw new AggregateException(errors);

        var finishTimestamp = TimeUtils.RealTimestamp;

        return DictionaryPerformanceHarness<int, int>.PerfMetrics.FromScenario(
            scenario,
            workers,
            startTimestamp,
            finishTimestamp,
            counters);
    }

    private static void PrintScenario(
        DictionaryPerformanceHarness<int, int>.Scenario scenario,
        DictionaryPerformanceHarness<int, int>.PerfMetrics concurrent,
        DictionaryPerformanceHarness<int, int>.PerfMetrics atomic)
    {
        Console.WriteLine(
            $"Scenario {scenario.name}: keys={scenario.keySpace:n0}, prefill={scenario.prefill:n0}");

        PrintMetrics("ConcurrentDictionary", concurrent);
        PrintMetrics("AtomicDictionaryTest", atomic);

        var ratio = atomic.opsPerSecond > 0 && concurrent.opsPerSecond > 0
            ? atomic.opsPerSecond / concurrent.opsPerSecond
            : 0;

        Console.WriteLine($"  Ratio Atomic/Concurrent: {ratio:F2}x");
        Console.WriteLine();
    }

    private static void PrintMetrics(string name, DictionaryPerformanceHarness<int, int>.PerfMetrics metrics)
    {
        var hitRate = metrics.tryGet > 0 ? (double)metrics.tryGetHit / metrics.tryGet * 100 : 0;
        var getOrAddHitRate = metrics.getOrAdd > 0 ? (double)metrics.getOrAddHit / metrics.getOrAdd * 100 : 0;

        Console.WriteLine(
            $"  {name,-22} ops/s={metrics.opsPerSecond:n0}, total={metrics.totalOps:n0}, " +
            $"tryget-hit={hitRate:F1}% goa-hit={getOrAddHitRate:F1}% " +
            $"add-ok={metrics.tryAddOk:n0} rem-ok={metrics.tryRemoveOk:n0} upd-ok={metrics.tryUpdateOk:n0} " +
            $"goa-add={metrics.getOrAddAdd:n0} aou-add={metrics.addOrUpdateAdd:n0} aou-upd={metrics.addOrUpdateUpdate:n0}");
    }

    private static bool ConcurrentAddOrUpdate(
        ConcurrentDictionary<int, int> dictionary,
        in int key,
        in int addValue,
        in int updateValue)
    {
        var state = GetAddOrUpdateState();
        state.Added = false;
        state.Updated = false;
        state.AddValue = addValue;
        state.UpdateValue = updateValue;

        dictionary.AddOrUpdate(key, ConcurrentAddFactory, ConcurrentUpdateFactory, state);

        return state.Added;
    }

    private static int KeyFactory(int index) => index;

    private static int ValueFactory(int index) => unchecked((int)((uint)index * 0x9E3779B9u));

    private static bool ConcurrentGetOrAdd(
        ConcurrentDictionary<int, int> dictionary,
        in int key,
        in int addValue)
    {
        var state = GetGetOrAddState();
        state.Added = false;
        state.AddValue = addValue;

        dictionary.GetOrAdd(key, ConcurrentGetOrAddFactory, state);

        return state.Added;
    }

    private static bool AtomicAddOrUpdate(
        AtomicTestDictionary dictionary,
        in int key,
        in int addValue,
        in int updateValue)
    {
        var state = GetAddOrUpdateState();
        state.Added = false;
        state.Updated = false;
        state.AddValue = addValue;
        state.UpdateValue = updateValue;

        dictionary.AddOrUpdate(in key, state, AtomicAddFactory, AtomicUpdateFactory);

        return state.Added;
    }

    private static bool AtomicGetOrAdd(
        AtomicTestDictionary dictionary,
        in int key,
        in int addValue)
    {
        var state = GetGetOrAddState();
        state.Added = false;
        state.AddValue = addValue;

        dictionary.GetOrAdd(in key, state, AtomicGetOrAddFactory);

        return state.Added;
    }

    [ThreadStatic]
    private static GetOrAddState? getOrAddState;

    [ThreadStatic]
    private static AddOrUpdateState? addOrUpdateState;

    private static GetOrAddState GetGetOrAddState() => getOrAddState ??= new GetOrAddState();

    private static AddOrUpdateState GetAddOrUpdateState() => addOrUpdateState ??= new AddOrUpdateState();

    private static int ConcurrentGetOrAddFactory(int key, GetOrAddState state)
    {
        state.Added = true;
        return state.AddValue;
    }

    private static int ConcurrentAddFactory(int key, AddOrUpdateState state)
    {
        state.Added = true;
        return state.AddValue;
    }

    private static int ConcurrentUpdateFactory(int key, int existing, AddOrUpdateState state)
    {
        state.Updated = true;
        return state.UpdateValue;
    }

    private static int AtomicGetOrAddFactory(in int key, in GetOrAddState state)
    {
        state.Added = true;
        return state.AddValue;
    }

    private static int AtomicAddFactory(in int key, in AddOrUpdateState state)
    {
        state.Added = true;
        return state.AddValue;
    }

    private static int AtomicUpdateFactory(in int key, in int oldValue, in AddOrUpdateState state)
    {
        state.Updated = true;
        return state.UpdateValue;
    }

    private sealed class GetOrAddState
    {
        public int AddValue;
        public bool Added;
    }

    private sealed class AddOrUpdateState
    {
        public int AddValue;
        public int UpdateValue;
        public bool Added;
        public bool Updated;
    }
}
