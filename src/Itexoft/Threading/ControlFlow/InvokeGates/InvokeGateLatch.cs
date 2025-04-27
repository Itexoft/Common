// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.CompilerServices;
using Itexoft.Core;
using Itexoft.Extensions;

namespace Itexoft.Threading.ControlFlow;

public struct InvokeGateLatch<TResult>
{
    private int state;
    private int phase;
    private int resultSeq;

    private long lastTimestamp;

    private TResult? lastResult;
    private Exception? lastException;

    private readonly TimeSpan interval;
    private readonly Func<TResult?> callback;

    internal InvokeGateLatch(Func<TResult?> callback, TimeSpan interval = default)
    {
        this.callback = callback.Required();
        this.interval = interval;
    }

    public int Phase
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Volatile.Read(ref this.phase);
    }

    public long LastTimestamp
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Volatile.Read(ref this.lastTimestamp);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryInvoke(out TResult? result)
    {
        if (this.TryReadFresh(out result, out var ex))
        {
            if (ex is not null)
                throw ex.Rethrow();

            return true;
        }

        if (Interlocked.CompareExchange(ref this.state, 1, 0) != 0)
        {
            result = default!;

            return false;
        }

        var observed = Volatile.Read(ref this.phase);

        try
        {
            if (this.TryReadFresh(out result, out ex))
            {
                if (ex is not null)
                    throw ex.Rethrow();

                return true;
            }

            try
            {
                var r = this.callback();
                this.WriteResult(r, null);
                this.UpdateTimestamp();

                result = r;

                return true;
            }
            catch (Exception e)
            {
                this.WriteResult(default, e);
                this.UpdateTimestamp();

                throw;
            }
        }
        finally
        {
            Volatile.Write(ref this.phase, observed + 1);
            Volatile.Write(ref this.state, 0);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TResult? Invoke()
    {
        var sw = new SpinWait();

        while (true)
        {
            if (this.TryReadFresh(out var rr, out var ee))
            {
                if (ee is not null)
                    throw ee.Rethrow();

                return rr;
            }

            var observed = Volatile.Read(ref this.phase);

            if (Interlocked.CompareExchange(ref this.state, 1, 0) == 0)
            {
                try
                {
                    if (this.TryReadFresh(out rr, out ee))
                    {
                        if (ee is not null)
                            throw ee.Rethrow();

                        return rr;
                    }

                    try
                    {
                        var r = this.callback();
                        this.WriteResult(r, null);
                        this.UpdateTimestamp();

                        return r;
                    }
                    catch (Exception e)
                    {
                        this.WriteResult(default, e);
                        this.UpdateTimestamp();

                        throw;
                    }
                }
                finally
                {
                    Volatile.Write(ref this.phase, observed + 1);
                    Volatile.Write(ref this.state, 0);
                }
            }

            while (Volatile.Read(ref this.phase) == observed)
                sw.SpinOnce();

            this.ReadResult(out var r2, out var e2);

            if (e2 is not null)
                throw e2.Rethrow();

            return r2;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryReadFresh(out TResult? result, out Exception? exception)
    {
        var last = Volatile.Read(ref this.lastTimestamp);

        if (last == 0 || this.interval == TimeSpan.Zero)
        {
            result = default!;
            exception = null;

            return false;
        }

        var now = TimeUtils.CachedTimestamp;

        if (now - last >= this.interval.Ticks)
        {
            result = default!;
            exception = null;

            return false;
        }

        this.ReadResult(out result, out exception);

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void UpdateTimestamp()
    {
        if (this.interval == TimeSpan.Zero)
            return;

        Volatile.Write(ref this.lastTimestamp, TimeUtils.CachedTimestamp);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteResult(TResult? result, Exception? exception)
    {
        var start = Interlocked.Increment(ref this.resultSeq);

        this.lastException = exception;
        this.lastResult = result;

        Volatile.Write(ref this.resultSeq, start + 1);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ReadResult(out TResult? result, out Exception? exception)
    {
        while (true)
        {
            var v1 = Volatile.Read(ref this.resultSeq);

            if ((v1 & 1) != 0)
                continue;

            exception = this.lastException;
            result = this.lastResult;

            var v2 = Volatile.Read(ref this.resultSeq);

            if (v1 == v2)
                return;
        }
    }
}

public struct InvokeGateLatchAsync<TResult>
{
    private int state;
    private int phase;
    private int resultSeq;

    private long lastTimestamp;

    private TResult? lastResult;
    private Exception? lastException;

    private readonly TimeSpan interval;
    private readonly Func<ValueTask<TResult?>> callback;

    internal InvokeGateLatchAsync(Func<ValueTask<TResult?>> callback, TimeSpan interval = default)
    {
        this.callback = callback.Required();
        this.interval = interval;
    }

    public int Phase
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Volatile.Read(ref this.phase);
    }

    public long LastTimestamp
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Volatile.Read(ref this.lastTimestamp);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public async ValueTask<(bool Success, TResult? Result)> TryInvokeAsync()
    {
        if (this.TryReadFresh(out var result, out var ex))
        {
            if (ex is not null)
                throw ex.Rethrow();

            return (true, result);
        }

        if (Interlocked.CompareExchange(ref this.state, 1, 0) != 0)
            return (false, default);

        var observed = Volatile.Read(ref this.phase);

        try
        {
            if (this.TryReadFresh(out result, out ex))
            {
                if (ex is not null)
                    throw ex.Rethrow();

                return (true, result);
            }

            try
            {
                result = await this.callback();
                this.WriteResult(result, null);
                this.UpdateTimestamp();

                return (true, result);
            }
            catch (Exception e)
            {
                this.WriteResult(default, e);
                this.UpdateTimestamp();

                throw;
            }
        }
        finally
        {
            Volatile.Write(ref this.phase, observed + 1);
            Volatile.Write(ref this.state, 0);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TResult? Invoke() => this.InvokeAsync().GetAwaiter().GetResult();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public async ValueTask<TResult?> InvokeAsync()
    {
        while (true)
        {
            if (this.TryReadFresh(out var rr, out var ee))
            {
                if (ee is not null)
                    throw ee.Rethrow();

                return rr;
            }

            var observed = Volatile.Read(ref this.phase);

            if (Interlocked.CompareExchange(ref this.state, 1, 0) == 0)
            {
                try
                {
                    if (this.TryReadFresh(out rr, out ee))
                    {
                        if (ee is not null)
                            throw ee.Rethrow();

                        return rr;
                    }

                    try
                    {
                        var r = await this.callback();
                        this.WriteResult(r, null);
                        this.UpdateTimestamp();

                        return r;
                    }
                    catch (Exception e)
                    {
                        this.WriteResult(default, e);
                        this.UpdateTimestamp();

                        throw;
                    }
                }
                finally
                {
                    Volatile.Write(ref this.phase, observed + 1);
                    Volatile.Write(ref this.state, 0);
                }
            }

            while (Volatile.Read(ref this.phase) == observed)
                await Task.Yield();

            this.ReadResult(out var r2, out var e2);

            if (e2 is not null)
                throw e2.Rethrow();

            return r2;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public async ValueTask<T?> InvokeAsync<T>(Func<TResult?, T?> map)
    {
        while (true)
        {
            if (this.TryReadFresh(out var rr, out var ee))
            {
                if (ee is not null)
                    throw ee.Rethrow();

                return map(rr);
            }

            var observed = Volatile.Read(ref this.phase);

            if (Interlocked.CompareExchange(ref this.state, 1, 0) == 0)
            {
                try
                {
                    if (this.TryReadFresh(out rr, out ee))
                    {
                        if (ee is not null)
                            throw ee.Rethrow();

                        return map(rr);
                    }

                    try
                    {
                        var r = await this.callback();
                        this.WriteResult(r, null);
                        this.UpdateTimestamp();

                        return map(r);
                    }
                    catch (Exception e)
                    {
                        this.WriteResult(default, e);
                        this.UpdateTimestamp();

                        throw;
                    }
                }
                finally
                {
                    Volatile.Write(ref this.phase, observed + 1);
                    Volatile.Write(ref this.state, 0);
                }
            }

            while (Volatile.Read(ref this.phase) == observed)
                await Task.Yield();

            this.ReadResult(out var r2, out var e2);

            if (e2 is not null)
                throw e2.Rethrow();

            return map(r2);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryReadFresh(out TResult? result, out Exception? exception)
    {
        var last = Volatile.Read(ref this.lastTimestamp);

        if (last == 0 || this.interval == TimeSpan.Zero)
        {
            result = default!;
            exception = null;

            return false;
        }

        var now = TimeUtils.CachedTimestamp;

        if (now - last >= this.interval.Ticks)
        {
            result = default!;
            exception = null;

            return false;
        }

        this.ReadResult(out result, out exception);

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void UpdateTimestamp()
    {
        if (this.interval == TimeSpan.Zero)
            return;

        Volatile.Write(ref this.lastTimestamp, TimeUtils.CachedTimestamp);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteResult(TResult? result, Exception? exception)
    {
        var start = Interlocked.Increment(ref this.resultSeq);

        this.lastException = exception;
        this.lastResult = result;

        Volatile.Write(ref this.resultSeq, start + 1);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ReadResult(out TResult? result, out Exception? exception)
    {
        while (true)
        {
            var v1 = Volatile.Read(ref this.resultSeq);

            if ((v1 & 1) != 0)
                continue;

            exception = this.lastException;
            result = this.lastResult;

            var v2 = Volatile.Read(ref this.resultSeq);

            if (v1 == v2)
                return;
        }
    }
}
