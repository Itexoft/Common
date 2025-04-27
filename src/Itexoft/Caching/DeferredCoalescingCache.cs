// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Collections;
using Itexoft.Extensions;
using Itexoft.Threading;

namespace Itexoft.Caching;

/// <summary>
/// Asynchronous coalescing cache: runs the async factory once per key and returns the same task to concurrent callers.
/// Removes the entry after the task completes.
/// </summary>
public sealed class DeferredCoalescingCache<TKey, TValue> where TKey : notnull
{
    private readonly AtomicDictionary<TKey, Task<TValue>> source;

    public DeferredCoalescingCache() => this.source = new();

    public DeferredCoalescingCache(int concurrencyLevel, int capacity) => this.source = new(concurrencyLevel, capacity);

    public DeferredCoalescingCache(IEqualityComparer<TKey>? comparer) => this.source = new(comparer);

    public DeferredCoalescingCache(int concurrencyLevel, int capacity, IEqualityComparer<TKey>? comparer) =>
        this.source = new(concurrencyLevel, capacity, comparer);

    /// <summary>
    /// Returns a task for the specified key; concurrent callers share the same task.
    /// Entry is removed after completion (success, failure, or cancellation).
    /// </summary>
    public Task<TValue> GetOrAddAsync(TKey key, Func<TKey, CancelToken, Task<TValue>> valueFactory, CancelToken cancelToken = default)
    {
        if (key is null)
            throw new ArgumentNullException(nameof(key));

        valueFactory.Required();
        cancelToken.ThrowIf();

        Task<TValue>? createdTask = null;

        var task = this.source.GetOrAdd(
            key,
            (k, vf) =>
            {
                var valueTask = CreateTask(k, vf, cancelToken);
                Volatile.Write(ref createdTask, valueTask);

                return valueTask;
            },
            valueFactory);

        if (ReferenceEquals(task, Volatile.Read(ref createdTask)))
        {
            _ = task.ContinueWith(
                static (Task<TValue> _, object? state) =>
                {
                    var (dictionary, k, current) = ((AtomicDictionary<TKey, Task<TValue>>, TKey, Task<TValue>))state!;
                    dictionary.TryRemove(k, (kk, vv) => ReferenceEquals(vv, current), out var _);
                },
                (this.source, key, task),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        return task;
    }

    public bool ContainsKey(TKey key) => this.source.ContainsKey(key);

    public bool TryRemove(TKey key) => this.source.TryRemove(key, out _);

    private static Task<TValue> CreateTask(TKey key, Func<TKey, CancelToken, Task<TValue>> valueFactory, CancelToken cancelToken)
    {
        try
        {
            return valueFactory(key, cancelToken);
        }
        catch (Exception ex)
        {
            return Task.FromException<TValue>(ex);
        }
    }
}
