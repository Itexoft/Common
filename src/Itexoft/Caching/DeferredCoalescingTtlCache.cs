// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Collections;
using Itexoft.Extensions;
using Itexoft.Threading;

namespace Itexoft.Caching;

/// <summary>
/// Coalesces concurrent async computations per key and stores the result with a per-entry TTL.
/// TTL=0 means no caching (only coalescing).
/// </summary>
public sealed class DeferredCoalescingTtlCache<TKey, TValue> where TKey : notnull
{
    private readonly AtomicDictionary<TKey, CacheEntry> cache = new();
    private readonly DeferredCoalescingCache<TKey, TValue> inFlight = new();

    public void Clear() => this.cache.Clear();

    public Task<TValue> GetOrAddAsync(TKey key, Func<TKey, CancelToken, Task<CacheValue>> valueFactory, CancelToken cancelToken = default)
    {
        if (key is null)
            throw new ArgumentNullException(nameof(key));

        valueFactory.Required();
        cancelToken.ThrowIf();

        var now = DateTimeOffset.UtcNow;

        if (this.TryGetCached(key, now, out var cached))
            return Task.FromResult(cached);

        return this.inFlight.GetOrAddAsync(
            key,
            async (k, ct) =>
            {
                var produced = await valueFactory(k, ct).ConfigureAwait(false);

                if (produced.Ttl > TimeSpan.Zero)
                {
                    var expiresAt = produced.Ttl == TimeSpan.MaxValue ? DateTimeOffset.MaxValue : SafeAdd(DateTimeOffset.UtcNow, produced.Ttl);
                    var entry = new CacheEntry(produced.Value, expiresAt);

                    this.cache.AddOrUpdate(k, entry, static (_, existing, state) => state.ExpiresAt >= existing.ExpiresAt ? state : existing);
                }

                return produced.Value;
            },
            cancelToken);
    }

    public bool TryRemove(TKey key) => this.cache.TryRemove(key, out _);

    private bool TryGetCached(TKey key, DateTimeOffset now, out TValue value)
    {
        if (this.cache.TryGetValue(key, out var entry))
        {
            if (entry.ExpiresAt > now)
            {
                value = entry.Value;

                return true;
            }

            this.cache.TryRemove(key, static (_, e) => e.ExpiresAt <= DateTimeOffset.UtcNow, out _);
        }

        value = default!;

        return false;
    }

    private static DateTimeOffset SafeAdd(DateTimeOffset timestamp, TimeSpan delta)
    {
        var maxDelta = DateTimeOffset.MaxValue - timestamp;

        if (delta >= maxDelta)
            return DateTimeOffset.MaxValue;

        return timestamp + (delta >= TimeSpan.Zero ? delta : TimeSpan.Zero);
    }

    public readonly record struct CacheValue(TValue Value, TimeSpan Ttl);

    private readonly record struct CacheEntry(TValue Value, DateTimeOffset ExpiresAt);
}
