// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.IO.VFS.FileSystem;

public static class VirtualDirectory
{
    public static void Create(VirtualFileSystem vfs, string path)
    {
        ArgumentNullException.ThrowIfNull(vfs);
        vfs.CreateDirectory(path);
    }

    public static bool Exists(VirtualFileSystem vfs, string path)
    {
        ArgumentNullException.ThrowIfNull(vfs);

        return vfs.DirectoryExists(path);
    }

    public static void Delete(VirtualFileSystem vfs, string path, bool recursive = false)
    {
        ArgumentNullException.ThrowIfNull(vfs);
        vfs.DeleteDirectory(path, recursive);
    }

    public static IReadOnlyList<string> Enumerate(VirtualFileSystem vfs, string path)
    {
        ArgumentNullException.ThrowIfNull(vfs);

        return vfs.EnumerateDirectory(path);
    }
}