// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using Itexoft.Threading.ControlFlow;

namespace Itexoft.Core;

public static class TimeUtils
{
    private static readonly double tickFrequency = (double)TimeSpan.TicksPerSecond / Stopwatch.Frequency;
    private static readonly long initTimestamp = RealTimestamp;
    private static InvokeGateLatch<long> timestampLatch = InvokeGate.Latch(Stopwatch.GetTimestamp);

    public static long RealTimestamp
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Stopwatch.GetTimestamp();
    }

    public static long CachedTimestamp
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => timestampLatch.Invoke();
    }

    public static int CachedTimestampMs
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (int)(ToTimeSpanTicks(timestampLatch.Invoke() - initTimestamp) / TimeSpan.TicksPerMillisecond);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long ToTicks(int milliseconds)
    {
        if (milliseconds <= 0)
            return 0;

        return (long)milliseconds * Stopwatch.Frequency / 1000;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long ToTimeSpanTicks(long timestampTicks) => (long)(timestampTicks * tickFrequency);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long ToTimestampTicks(TimeSpan timeSpan)
    {
        if (timeSpan <= TimeSpan.Zero)
            return 0;

        var ticks = timeSpan.Ticks * (double)Stopwatch.Frequency / TimeSpan.TicksPerSecond;

        return ticks >= long.MaxValue ? long.MaxValue : (long)ticks;
    }
}
