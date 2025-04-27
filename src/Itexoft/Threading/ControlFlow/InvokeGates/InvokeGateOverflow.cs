// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.CompilerServices;
using Itexoft.Core;
using Itexoft.Extensions;

namespace Itexoft.Threading.ControlFlow;

public ref struct InvokeGateOverflow<TResult>
{
    private readonly long intervalTicks;
    private long timestamp;
    private readonly Func<TResult?> callback;

    internal InvokeGateOverflow(Func<TResult?> callback, TimeSpan interval)
    {
        this.callback = callback.Required();
        this.intervalTicks = interval.Ticks;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryInvoke(out TResult? result)
    {
        if (this.intervalTicks == 0)
        {
            result = this.callback();

            return true;
        }

        var now = TimeUtils.CachedTimestamp;
        var last = Volatile.Read(ref this.timestamp);

        if ((last != 0 && now - last < this.intervalTicks) || Interlocked.CompareExchange(ref this.timestamp, now, last) != last)
        {
            result = default!;

            return false;
        }

        result = this.callback();

        return true;
    }
}
