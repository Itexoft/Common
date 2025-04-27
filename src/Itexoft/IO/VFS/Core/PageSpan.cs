// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.IO.VFS.Core;

internal readonly record struct PageSpan(PageId Start, int Length)
{
    public static readonly PageSpan Invalid = new(PageId.Invalid, 0);
    public bool IsValid => this.Start.IsValid && this.Length > 0;
    public long EndExclusive => this.Start.Value + this.Length;
}
