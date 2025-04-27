// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.IO.VFS.Metadata.Models;

internal readonly record struct FileId(long Value) : IComparable<FileId>
{
    public static readonly FileId Root = new(1);
    public static readonly FileId Invalid = new(0);
    public bool IsValid => this.Value > 0;
    public int CompareTo(FileId other) => this.Value.CompareTo(other.Value);
}
