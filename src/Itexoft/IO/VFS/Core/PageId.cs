// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Diagnostics;

namespace Itexoft.IO.VFS.Core;

[DebuggerDisplay("{Value}")]
internal readonly record struct PageId(long Value)
{
    public static readonly PageId Invalid = new(-1);

    public bool IsValid => this.Value >= 0;

    public static PageId FromOffset(long pageSize, long offset)
    {
        if (offset < 0 || offset % pageSize != 0)
            throw new ArgumentOutOfRangeException(nameof(offset), offset, "Offset must align to page size.");

        return new(offset / pageSize);
    }

    public long ToOffset(int pageSize)
    {
        if (!this.IsValid)
            throw new InvalidOperationException("Cannot convert invalid PageId to offset.");

        return this.Value * pageSize;
    }

    public override string ToString() => this.IsValid ? this.Value.ToString() : "<invalid>";
}
