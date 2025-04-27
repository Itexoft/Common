// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Networking.Core;

/// <summary>
/// Token bucket for throttling retry attempts within a configurable window.
/// </summary>
public sealed class RetryBudget
{
    private readonly int _capacity;
    private readonly TimeSpan _window;
    private int _tokens;
    private DateTimeOffset _windowStart;

    public RetryBudget(int capacity, TimeSpan window)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));

        if (window <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(window));

        this._capacity = capacity;
        this._window = window;
        this._tokens = capacity;
        this._windowStart = DateTimeOffset.UtcNow;
    }

    public bool TryConsume(DateTimeOffset now)
    {
        this.Refill(now);

        if (this._tokens <= 0)
            return false;

        this._tokens--;

        return true;
    }

    public void Refund(DateTimeOffset now)
    {
        this.Refill(now);
        if (this._tokens < this._capacity)
            this._tokens++;
    }

    private void Refill(DateTimeOffset now)
    {
        if (now - this._windowStart >= this._window)
        {
            this._tokens = this._capacity;
            this._windowStart = now;
        }
    }
}