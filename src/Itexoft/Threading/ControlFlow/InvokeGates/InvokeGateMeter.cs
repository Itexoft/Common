// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.CompilerServices;
using Itexoft.Core;
using Itexoft.Extensions;

namespace Itexoft.Threading.ControlFlow;

public ref struct InvokeGateMeter<TResult>
{
    private readonly long burstTicks;
    private readonly Func<TResult> callback;
    private readonly long intervalTicks;
    private long timestamp;

    internal InvokeGateMeter(Func<TResult> callback, int limit, TimeSpan period, int burst)
    {
        this.callback = callback.Required();
        var increment = period.Ticks / limit.RequiredPositive();

        if (increment <= 0)
            increment = 1;

        var burstTokens = burst <= 0 ? limit : burst;

        this.intervalTicks = increment;
        this.burstTicks = increment * burstTokens;
        this.timestamp = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryInvoke(out TResult result)
    {
        var now = TimeUtils.CachedTimestamp;
        var oldTat = Volatile.Read(ref this.timestamp);
        var tat = oldTat == 0 ? now : oldTat;

        if (now < tat - this.burstTicks)
        {
            result = default!;

            return false;
        }

        var baseTat = tat > now ? tat : now;
        var newTat = baseTat + this.intervalTicks;

        if (Interlocked.CompareExchange(ref this.timestamp, newTat, oldTat) != oldTat)
        {
            result = default!;

            return false;
        }

        result = this.callback();

        return true;
    }
}
