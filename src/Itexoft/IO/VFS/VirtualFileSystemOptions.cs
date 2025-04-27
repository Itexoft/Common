// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.IO.VFS.Core;
using Itexoft.IO.VFS.Features;

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

    internal Action<FeatureRegistry>? ConfigureFeatures { get; init; }

    internal (int PageSize, FeatureRegistry Registry) Materialize(int? existingPageSize = null)
    {
        int size;
        if (existingPageSize.HasValue)
        {
            if (this.PageSize.HasValue && this.PageSize.Value != existingPageSize.Value)
                throw new InvalidOperationException(
                    $"Requested page size {this.PageSize.Value} does not match existing container page size {existingPageSize.Value}.");

            size = PageSizing.Normalize(existingPageSize);
        }
        else
        {
            size = PageSizing.Normalize(this.PageSize);
        }

        var registry = new FeatureRegistry();
        this.ConfigureFeatures?.Invoke(registry);
        FeatureDefaults.Apply(registry);

        return (size, registry);
    }
}