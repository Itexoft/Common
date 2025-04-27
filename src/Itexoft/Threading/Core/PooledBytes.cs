// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Buffers;

namespace Itexoft.Threading;

#nullable enable

public ref struct PooledBytes
{
    private byte[] buffer;
    private int length;

    public PooledBytes(int length)
    {
        if (length < 0)
            throw new ArgumentOutOfRangeException(nameof(length));

        this.length = length;

        this.buffer = length == 0 ? Array.Empty<byte>() : ArrayPool<byte>.Shared.Rent(length);
    }

    public Span<byte> Span => this.buffer.AsSpan(0, this.length);

    public void Dispose()
    {
        if (this.length == 0)
            return;

        ArrayPool<byte>.Shared.Return(this.buffer);
        this.buffer = Array.Empty<byte>();
        this.length = 0;
    }
}
