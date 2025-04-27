// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Threading.ControlFlow;

public static class InvokeGate
{
    public static InvokeGateLatch<TResult> Latch<TResult>(Func<TResult?> callback, TimeSpan interval = default) => new(callback, interval);

    public static InvokeGateLatchAsync<TResult> LatchAsync<TResult>(Func<ValueTask<TResult?>> callback, TimeSpan interval) => new(callback, interval);

    public static InvokeGateDrop<TResult> Drop<TResult>(Func<TResult?> callback, int limit = 1) => new(callback, limit);

    public static InvokeGateWindow<TResult> Window<TResult>(Func<TResult> callback, int limit, TimeSpan period) =>
        new(callback, limit, period);

    public static InvokeGateMeter<TResult> Meter<TResult>(Func<TResult> callback, int limit, TimeSpan period, int burst = 0) =>
        new(callback, limit, period, burst);

    public static InvokeGatePulse<TContext, TResult> Pulse<TContext, TResult>(
        Func<TContext?, TResult> callback,
        int intervalMilliseconds = 0,
        TContext? pendingContext = default) =>
        new(callback, pendingContext, intervalMilliseconds);

    public static InvokeGateOverflow<TResult> Overflow<TResult>(Func<TResult?> callback, TimeSpan interval) =>
        new(callback, interval);
}
