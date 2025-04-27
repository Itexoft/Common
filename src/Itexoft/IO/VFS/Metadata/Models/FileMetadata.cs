// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Collections.Immutable;
using Itexoft.IO.VFS.Core;

namespace Itexoft.IO.VFS.Metadata.Models;

internal enum FileKind
{
    File,
    Directory,
    AttributeStream
}

internal sealed record class FileMetadata
{
    public FileKind Kind { get; init; }
    public long Length { get; init; }
    public ImmutableArray<PageSpan> Extents { get; init; } = ImmutableArray<PageSpan>.Empty;
    public FileAttributes Attributes { get; init; }
    public DateTime CreatedUtc { get; init; }
    public DateTime ModifiedUtc { get; init; }
    public DateTime AccessedUtc { get; init; }
    public int Generation { get; init; }
}