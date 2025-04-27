// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Net.Core;

/// <summary>
/// Token bucket for throttling retry attempts within a configurable window.
/// </summary>
public sealed class NetRetryBudget
{
    private readonly long capacity;
    private readonly long windowTicks;
    private long tokens;
    private long windowStartTicks;

    public NetRetryBudget(long capacity, TimeSpan window)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(window, TimeSpan.Zero);

        this.capacity = capacity;
        this.windowTicks = window.Ticks;
        this.tokens = capacity;
        this.windowStartTicks = DateTimeOffset.UtcNow.UtcTicks;
    }

    public bool TryConsume(DateTimeOffset now)
    {
        this.Refill(now);

        while (true)
        {
            var current = Interlocked.Read(ref this.tokens);

            if (current <= 0)
                return false;

            if (Interlocked.CompareExchange(ref this.tokens, current - 1, current) == current)
                return true;
        }
    }

    public void Refund(DateTimeOffset now)
    {
        this.Refill(now);

        while (true)
        {
            var current = Interlocked.Read(ref this.tokens);

            if (current >= this.capacity)
                return;

            if (Interlocked.CompareExchange(ref this.tokens, current + 1, current) == current)
                return;
        }
    }

    private void Refill(DateTimeOffset now)
    {
        var nowTicks = now.UtcTicks;

        while (true)
        {
            var start = Interlocked.Read(ref this.windowStartTicks);

            if (nowTicks - start < this.windowTicks)
                return;

            if (Interlocked.CompareExchange(ref this.windowStartTicks, nowTicks, start) == start)
            {
                Interlocked.Exchange(ref this.tokens, this.capacity);

                return;
            }
        }
    }
}
