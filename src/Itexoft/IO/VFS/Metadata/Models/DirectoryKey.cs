// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Text;

namespace Itexoft.IO.VFS.Metadata.Models;

internal readonly record struct DirectoryKey(FileId ParentId, string NormalizedName) : IComparable<DirectoryKey>
{
    public int CompareTo(DirectoryKey other)
    {
        var cmp = this.ParentId.CompareTo(other.ParentId);

        if (cmp != 0)
            return cmp;

        return string.CompareOrdinal(this.NormalizedName, other.NormalizedName);
    }

    public static DirectoryKey Create(FileId parentId, string name) => new(parentId, Normalize(name));

    public override string ToString() => $"{this.ParentId.Value}:{this.NormalizedName}";

    private static string Normalize(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        return value.Normalize(NormalizationForm.FormC).ToUpperInvariant();
    }
}
