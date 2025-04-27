// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.CompilerServices;
using Itexoft.Core;
using Itexoft.Extensions;

namespace Itexoft.Threading.ControlFlow;

public ref struct InvokeGatePulse<TContext, TResult>
{
    private readonly long intervalTicks;
    private TContext? pendingContext;
    private int pendingLock;
    private int phase;
    private long timestamp;
    private readonly Func<TContext?, TResult> callback;

    internal InvokeGatePulse(Func<TContext?, TResult> callback, TContext? pendingContext, int intervalMilliseconds)
    {
        this.callback = callback.Required();
        this.intervalTicks = TimeUtils.ToTicks(intervalMilliseconds.RequiredPositiveOrZero());
        this.pendingContext = pendingContext;
    }

    public int Phase
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Volatile.Read(ref this.phase);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryTrailing(in TContext? context)
    {
        if (this.intervalTicks == 0)
        {
            _ = this.callback(context);

            return true;
        }

        var now = TimeUtils.CachedTimestamp;
        var due = Volatile.Read(ref this.timestamp);

        if (due != 0 && now >= due)
            this.TryInvoke(out _);

        if (!this.TryWritePending(in context))
            return false;

        if (Volatile.Read(ref this.timestamp) == 0)
            Interlocked.CompareExchange(ref this.timestamp, now + this.intervalTicks, 0);

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryDebounce(in TContext? context)
    {
        if (this.intervalTicks == 0)
        {
            _ = this.callback(context);

            return true;
        }

        if (!this.TryWritePending(in context))
            return false;

        Volatile.Write(ref this.timestamp, TimeUtils.CachedTimestamp + this.intervalTicks);

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryInvoke(out TResult result)
    {
        var due = Volatile.Read(ref this.timestamp);

        if (due == 0)
        {
            result = default!;

            return false;
        }

        var now = TimeUtils.CachedTimestamp;

        if (now < due || Interlocked.CompareExchange(ref this.timestamp, 0, due) != due)
        {
            result = default!;

            return false;
        }

        var ctx = this.ReadPendingSpin();

        try
        {
            result = this.callback(ctx);

            return true;
        }
        finally
        {
            Interlocked.Increment(ref this.phase);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryWritePending(in TContext? context)
    {
        if (Interlocked.CompareExchange(ref this.pendingLock, 1, 0) != 0)
            return false;

        try
        {
            this.pendingContext = context;

            return true;
        }
        finally
        {
            Volatile.Write(ref this.pendingLock, 0);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private TContext? ReadPendingSpin()
    {
        var sw = new SpinWait();

        while (Interlocked.CompareExchange(ref this.pendingLock, 1, 0) != 0)
            sw.SpinOnce();

        try
        {
            return this.pendingContext;
        }
        finally
        {
            Volatile.Write(ref this.pendingLock, 0);
        }
    }
}
