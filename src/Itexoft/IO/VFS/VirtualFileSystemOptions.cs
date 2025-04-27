// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.IO.VFS;

/// <summary>
/// Configures behavioural aspects of a <see cref="VirtualFileSystem" /> instance.
/// </summary>
public sealed class VirtualFileSystemOptions
{
    /// <summary>
    /// Overrides the default page size (in bytes) used for on-disk storage. Values are normalized to supported boundaries.
    /// </summary>
    public int? PageSize { get; init; }

    /// <summary>
    /// Enables the background compaction engine which keeps free space contiguous under sustained load.
    /// </summary>
    public bool EnableCompaction { get; init; } = true;

    /// <summary>
    /// Enables mirrored persistence to a secondary <c>.bak</c> file.
    /// </summary>
    public bool EnableMirroring { get; init; }
}
