// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.CompilerServices;
using Itexoft.Core;
using Itexoft.Extensions;

namespace Itexoft.Threading.ControlFlow;

public ref struct InvokeGateWindow<TResult>
{
    private readonly Func<TResult> callback;
    private readonly int limit;
    private readonly TimeSpan period;
    private ulong windowState = 0;

    internal InvokeGateWindow(Func<TResult> callback, int limit, TimeSpan period)
    {
        this.callback = callback;
        this.limit = limit.RequiredPositive();
        this.period = period.RequiredPositive();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryInvoke(out TResult result)
    {
        var now = TimeUtils.CachedTimestampMs;
        var snapshot = Volatile.Read(ref this.windowState);

        if (snapshot == 0)
        {
            var desired = Pack(1, now);

            if (Interlocked.CompareExchange(ref this.windowState, desired, 0) != 0)
            {
                result = default!;

                return false;
            }

            result = this.callback();

            return true;
        }

        Unpack(snapshot, out var count, out var start);
        var elapsed = unchecked((uint)(now - start));

        if (elapsed >= (uint)this.period.TotalMilliseconds)
        {
            var desired = Pack(1, now);

            if (Interlocked.CompareExchange(ref this.windowState, desired, snapshot) != snapshot)
            {
                result = default!;

                return false;
            }

            result = this.callback();

            return true;
        }

        if ((uint)count >= (uint)this.limit)
        {
            result = default!;

            return false;
        }

        var desired2 = Pack(count + 1, start);

        if (Interlocked.CompareExchange(ref this.windowState, desired2, snapshot) != snapshot)
        {
            result = default!;

            return false;
        }

        result = this.callback();

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static ulong Pack(int low, int high) => (ulong)(uint)low | ((ulong)(uint)high << 32);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void Unpack(ulong value, out int low, out int high)
    {
        low = unchecked((int)(uint)value);
        high = unchecked((int)(uint)(value >> 32));
    }
}
