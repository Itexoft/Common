// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Text;
using Itexoft.Extensions;
using Itexoft.Threading;

namespace Itexoft.IO;

public sealed class StringStream(string value, Encoding encoding) : IStreamRals
{
    private readonly ReadOnlyMemory<byte> data = encoding.Required().GetBytes(value.Required());
    private int offset;

    public StringStream(string value) : this(value, Encoding.UTF8) { }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public ValueTask<int> ReadAsync(Memory<byte> buffer, CancelToken cancelToken = default)
    {
        if (buffer.Length == 0)
            return new(0);

        var available = this.data.Length - this.offset;

        if (available <= 0)
            return new(0);

        var toCopy = Math.Min(buffer.Length, available);
        this.data.Slice(this.offset, toCopy).CopyTo(buffer);
        this.offset += toCopy;

        return new(toCopy);
    }

    public long Length => this.data.Length;
    public long Position => this.offset;

    public long Seek(long offset, SeekOrigin origin)
    {
        var target = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => this.offset + offset,
            SeekOrigin.End => this.data.Length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin), origin, "Unsupported origin."),
        };

        if (target is < 0 or > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(offset), offset, "Position is out of range.");

        this.offset = (int)target;

        return target;
    }
}
