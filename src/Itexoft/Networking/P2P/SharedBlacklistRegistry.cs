// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Collections.Concurrent;
using Itexoft.Networking.Core;

namespace Itexoft.Networking.P2P;

public static class SharedBlacklistRegistry
{
    private static readonly ConcurrentDictionary<string, Entry> _entries = new();
    private static readonly ConcurrentDictionary<int, Action<SharedBlacklistNotification>> _subscribers = new();
    private static int _nextSubscriberId;
    private static long _lastPruneTicks;
    private static readonly long DefaultPruneIntervalTicks = TimeSpan.FromMilliseconds(250).Ticks;

    public static bool TryGetPenalty(string key, DateTimeOffset now, out Entry entry)
    {
        if (_entries.TryGetValue(key, out var value))
        {
            if (value.ExpiresAt > now)
            {
                entry = value;

                return true;
            }

            _entries.TryRemove(key, out _);
        }

        entry = default;

        return false;
    }

    public static bool SetPenalty(
        string key,
        FailureSeverity severity,
        TimeSpan duration,
        Guid? sourceId = null,
        bool fromRetryAfter = false)
    {
        if (key is null)
        {
            throw new ArgumentNullException(nameof(key));
        }

        var now = DateTimeOffset.UtcNow;
        if (duration <= TimeSpan.Zero)
        {
            duration = TimeSpan.FromMilliseconds(1);
        }

        var expires = now + duration;
        var origin = sourceId ?? Guid.Empty;

        while (true)
        {
            if (_entries.TryGetValue(key, out var existing))
            {
                if (existing.ExpiresAt >= expires && existing.Severity >= severity)
                {
                    return false;
                }

                var finalSeverity = MaxSeverity(existing.Severity, severity);
                var finalExpires = expires > existing.ExpiresAt ? expires : existing.ExpiresAt;
                var updated = new Entry(finalExpires, finalSeverity, origin, fromRetryAfter);
                if (_entries.TryUpdate(key, updated, existing))
                {
                    NotifySubscribers(new SharedBlacklistNotification(key, finalSeverity, finalExpires, origin, fromRetryAfter));

                    return true;
                }

                continue;
            }

            var entry = new Entry(expires, severity, origin, fromRetryAfter);
            if (_entries.TryAdd(key, entry))
            {
                NotifySubscribers(new SharedBlacklistNotification(key, severity, expires, origin, fromRetryAfter));

                return true;
            }
        }
    }

    public static void PruneExpiredEntries(DateTimeOffset now, bool force = false)
    {
        if (!force)
        {
            var last = Volatile.Read(ref _lastPruneTicks);
            var delta = now.UtcTicks - last;
            if (delta < 0)
            {
                delta = 0;
            }

            if (delta < DefaultPruneIntervalTicks)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref _lastPruneTicks, now.UtcTicks, last) != last)
            {
                return;
            }
        }
        else
        {
            Interlocked.Exchange(ref _lastPruneTicks, now.UtcTicks);
        }

        foreach (var pair in _entries)
        {
            if (pair.Value.ExpiresAt <= now)
            {
                _entries.TryRemove(pair.Key, out _);
            }
        }
    }

    public static void Clear()
    {
        _entries.Clear();
        _subscribers.Clear();
        Interlocked.Exchange(ref _lastPruneTicks, 0);
    }

    private static FailureSeverity MaxSeverity(FailureSeverity left, FailureSeverity right) =>
        left >= right ? left : right;

    private static void NotifySubscribers(SharedBlacklistNotification notification)
    {
        foreach (var pair in _subscribers)
        {
            try
            {
                pair.Value(notification);
            }
            catch
            {
                // swallow handler errors to keep the bus responsive
            }
        }
    }

    public static IDisposable Subscribe(Action<SharedBlacklistNotification> handler)
    {
        if (handler is null)
        {
            throw new ArgumentNullException(nameof(handler));
        }

        var id = Interlocked.Increment(ref _nextSubscriberId);
        _subscribers[id] = handler;

        return new Subscription(id);
    }

    public readonly record struct Entry(DateTimeOffset ExpiresAt, FailureSeverity Severity, Guid SourceId, bool FromRetryAfter);

    public readonly record struct SharedBlacklistNotification(
        string Key,
        FailureSeverity Severity,
        DateTimeOffset ExpiresAt,
        Guid SourceId,
        bool FromRetryAfter);

    private sealed class Subscription : IDisposable
    {
        private readonly int _id;
        private int _disposed;

        public Subscription(int id) => this._id = id;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref this._disposed, 1) == 1)
            {
                return;
            }

            _subscribers.TryRemove(this._id, out _);
        }
    }
}