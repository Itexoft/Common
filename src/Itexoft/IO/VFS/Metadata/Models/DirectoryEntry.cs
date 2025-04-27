// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.IO.VFS.Metadata.Models;

internal sealed record class DirectoryEntry
{
    public string Name { get; init; } = string.Empty;
    public FileId TargetId { get; init; }
    public FileKind Kind { get; init; }
    public FileAttributes Attributes { get; init; }
    public DateTime CreatedUtc { get; init; }
    public DateTime ModifiedUtc { get; init; }
    public DateTime AccessedUtc { get; init; }
    public int Generation { get; init; }
}