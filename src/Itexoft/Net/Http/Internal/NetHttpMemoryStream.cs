// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.IO;
using Itexoft.Threading;

namespace Itexoft.Net.Http.Internal;

internal sealed class NetHttpMemoryStream(ReadOnlyMemory<byte> data) : StreamBase, IStreamRal
{
    private int offset;

    public long Length => data.Length;
    public long Position => this.offset;

    public ValueTask<int> ReadAsync(Memory<byte> buffer, CancelToken cancelToken = default)
    {
        if (this.offset >= data.Length)
            return new(0);

        var toCopy = Math.Min(buffer.Length, data.Length - this.offset);
        data.Slice(this.offset, toCopy).CopyTo(buffer);
        this.offset += toCopy;

        return new(toCopy);
    }

    protected override ValueTask DisposeAny() => ValueTask.CompletedTask;
}
