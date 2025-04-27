// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Collections.Concurrent;

namespace Itexoft.Net.Core;

internal sealed class NetDialTracker : INetDialTracker
{
    private readonly ConcurrentQueue<DateTimeOffset> attempts = new();
    private readonly Lock sync = new();
    private DateTimeOffset blacklistedUntil;

    public bool TryStart(DateTimeOffset now, int maxPerMinute, TimeSpan blacklistDuration)
    {
        lock (this.sync)
        {
            this.Trim(now);

            if (this.blacklistedUntil > now)
                return false;

            if (maxPerMinute > 0 && this.CountAttempts(now) >= maxPerMinute)
            {
                this.blacklistedUntil = now + blacklistDuration;

                return false;
            }

            this.attempts.Enqueue(now);

            return true;
        }
    }

    public void RecordSuccess()
    {
        lock (this.sync)
            this.blacklistedUntil = DateTimeOffset.MinValue;
    }

    public void RecordFailure(DateTimeOffset now, TimeSpan blacklistDuration)
    {
        lock (this.sync)
            this.blacklistedUntil = now + blacklistDuration;
    }

    public NetDialSnapshot GetSnapshot(DateTimeOffset now)
    {
        lock (this.sync)
        {
            this.Trim(now);

            return new(this.attempts.Count, this.blacklistedUntil);
        }
    }

    public TimeSpan? GetBlacklistDelay(DateTimeOffset now)
    {
        lock (this.sync)
        {
            this.Trim(now);

            if (this.blacklistedUntil <= now)
                return null;

            return this.blacklistedUntil - now;
        }
    }

    private void Trim(DateTimeOffset now)
    {
        while (this.attempts.TryPeek(out var ts) && now - ts > TimeSpan.FromMinutes(1))
            this.attempts.TryDequeue(out _);
    }

    private int CountAttempts(DateTimeOffset now)
    {
        this.Trim(now);

        return this.attempts.Count;
    }
}

public readonly record struct NetDialSnapshot(int AttemptsLastMinute, DateTimeOffset BlacklistedUntil);
