// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Text;
using Itexoft.IO.VFS.Metadata.Models;

namespace Itexoft.IO.VFS.Metadata.Attributes;

internal readonly record struct AttributeKey(FileId FileId, string NormalizedName) : IComparable<AttributeKey>
{
    public int CompareTo(AttributeKey other)
    {
        var cmp = this.FileId.CompareTo(other.FileId);

        if (cmp != 0)
            return cmp;

        return string.CompareOrdinal(this.NormalizedName, other.NormalizedName);
    }

    public static AttributeKey Create(FileId fileId, string name) => new(fileId, Normalize(name));

    private static string Normalize(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        return value.Normalize(NormalizationForm.FormC).ToUpperInvariant();
    }
}