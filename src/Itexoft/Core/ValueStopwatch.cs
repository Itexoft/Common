// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Diagnostics;

namespace Itexoft.Core;

internal readonly struct ValueStopwatch
{
    private static readonly double tickFrequency = (double)TimeSpan.TicksPerSecond / Stopwatch.Frequency;
    private readonly long startTimestamp;

    private ValueStopwatch(long startTimestamp) => this.startTimestamp = startTimestamp;

    public static ValueStopwatch StartNew() => new(Stopwatch.GetTimestamp());

    public TimeSpan Elapsed
    {
        get
        {
            var delta = Stopwatch.GetTimestamp() - this.startTimestamp;

            return TimeSpan.FromTicks((long)(delta * tickFrequency));
        }
    }
}
