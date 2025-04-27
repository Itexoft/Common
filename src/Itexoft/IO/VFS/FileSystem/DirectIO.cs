// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Itexoft.IO.VFS.FileSystem;

internal static class DirectIo
{
    private const int fGetfl = 3;
    private const int fSetfl = 4;
    private const int oDirect = 0x4000;

    public static void Enable(SafeFileHandle handle)
    {
        if (!OperatingSystem.IsLinux())
            return;

        if (handle is null || handle.IsInvalid)
            return;

        var fd = (int)handle.DangerousGetHandle();

        if (fd <= 0)
            return;

        var flags = fcntl(fd, fGetfl, 0);

        if (flags == -1)
            return;

        if ((flags & oDirect) != oDirect)
            _ = fcntl(fd, fSetfl, flags | oDirect);
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int fcntl(int fd, int cmd, int arg);
}
