// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Networking.Core;

/// <summary>
/// Signals that the remote peer asked to retry after the specified delay.
/// </summary>
public sealed class RetryAfterException : Exception
{
    public RetryAfterException(TimeSpan delay)
        : base($"Peer requested retry after {delay}.")
    {
        if (delay <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(delay), "Delay must be positive.");

        this.Delay = delay;
    }

    public RetryAfterException(TimeSpan delay, string message)
        : base(message)
    {
        if (delay <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(delay), "Delay must be positive.");

        this.Delay = delay;
    }

    public RetryAfterException(TimeSpan delay, string message, Exception innerException)
        : base(message, innerException)
    {
        if (delay <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(delay), "Delay must be positive.");

        this.Delay = delay;
    }

    public TimeSpan Delay { get; }
}