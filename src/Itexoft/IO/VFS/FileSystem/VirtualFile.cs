// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Extensions;

namespace Itexoft.IO.VFS.FileSystem;

public static class VirtualFile
{
    public static void Create(VirtualFileSystem vfs, string path, FileAttributes attributes = FileAttributes.Normal)
    {
        vfs.Required();
        vfs.CreateFile(path, attributes);
    }

    public static bool Exists(VirtualFileSystem vfs, string path)
    {
        vfs.Required();

        return vfs.FileExists(path);
    }

    public static void Delete(VirtualFileSystem vfs, string path)
    {
        vfs.Required();
        vfs.DeleteFile(path);
    }

    public static Stream Open(VirtualFileSystem vfs, string path, FileMode mode, FileAccess access)
    {
        vfs.Required();

        return vfs.OpenFile(path, mode, access);
    }

    public static Stream OpenRead(VirtualFileSystem vfs, string path) => Open(vfs, path, FileMode.Open, FileAccess.Read);

    public static Stream OpenWrite(VirtualFileSystem vfs, string path) => Open(vfs, path, FileMode.OpenOrCreate, FileAccess.Write);

    public static void SetAttribute(VirtualFileSystem vfs, string path, string attributeName, ReadOnlySpan<byte> value)
    {
        vfs.Required();
        vfs.SetAttribute(path, attributeName, value);
    }

    public static bool TryGetAttribute(VirtualFileSystem vfs, string path, string attributeName, out byte[] value)
    {
        vfs.Required();

        return vfs.TryGetAttribute(path, attributeName, out value);
    }

    public static bool RemoveAttribute(VirtualFileSystem vfs, string path, string attributeName)
    {
        vfs.Required();

        return vfs.RemoveAttribute(path, attributeName);
    }
}
