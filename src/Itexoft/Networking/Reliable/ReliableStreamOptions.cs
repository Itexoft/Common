// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Networking.Reliable;

/// <summary>
/// Options for configuring <see cref="ReliableStreamWriter" /> and <see cref="ReliableStreamReader" />.
/// </summary>
public sealed class ReliableStreamOptions
{
    private int _bufferCapacityBytes = 256 * 1024;

    /// <summary>
    /// Maximum number of bytes that can be buffered while waiting for acknowledgements.
    /// </summary>
    public int BufferCapacityBytes
    {
        get => this._bufferCapacityBytes;
        set
        {
            if (value <= 0)
                throw new ArgumentOutOfRangeException(nameof(value), value, "Capacity must be positive.");

            this._bufferCapacityBytes = value;
        }
    }

    /// <summary>
    /// When <c>true</c>, the writer processes acknowledgements automatically and waits before reusing buffer space.
    /// </summary>
    public bool EnsureDeliveredBeforeRelease { get; set; } = true;
}