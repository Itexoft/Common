// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Diagnostics;

namespace Itexoft.IO.VFS.Diagnostics;

internal static class DebugBreakHelper
{
#if DEBUG
    private static readonly string? Mode = Environment.GetEnvironmentVariable("VFS_DEBUG_BREAK");

    public static void Trigger(string reason)
    {
        if (string.IsNullOrWhiteSpace(Mode))
            return;

        DebugUtility.Log($"[DebugBreak] {reason}");
        DebugUtility.Log(Environment.StackTrace);
        if (Debugger.IsAttached)
        {
            Debugger.Break();

            return;
        }

        if (string.Equals(Mode, "launch", StringComparison.OrdinalIgnoreCase))
            try
            {
                if (!Debugger.Launch())
                    DebugUtility.Log("[DebugBreak] Debugger.Launch returned false.");
            }
            catch (Exception ex)
            {
                DebugUtility.Log($"[DebugBreak] Debugger.Launch failed: {ex.GetType().Name}: {ex.Message}");
            }
    }
#else
    public static void Trigger(string reason) { }
#endif
}