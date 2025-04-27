// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Itexoft.IO.VFS.FileSystem;

internal static class DirectIO
{
    private const int F_GETFL = 3;
    private const int F_SETFL = 4;
    private const int O_DIRECT = 0x4000;

    public static void Enable(SafeFileHandle handle)
    {
        if (!OperatingSystem.IsLinux())
            return;

        if (handle is null || handle.IsInvalid)
            return;

        var fd = (int)handle.DangerousGetHandle();

        if (fd <= 0)
            return;

        var flags = fcntl(fd, F_GETFL, 0);

        if (flags == -1)
            return;

        if ((flags & O_DIRECT) != O_DIRECT)
            _ = fcntl(fd, F_SETFL, flags | O_DIRECT);
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int fcntl(int fd, int cmd, int arg);
}