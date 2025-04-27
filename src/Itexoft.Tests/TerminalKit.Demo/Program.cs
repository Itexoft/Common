// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.TerminalKit.ObjectExplorer;

namespace TerminalKit.Demo;

internal static class Program
{
    private static void Main()
    {
        if (ShouldDumpSnapshot())
        {
            SnapshotDebugger.DumpExplorerSnapshot(new AccountCatalog());

            return;
        }

        using var explorer = new TerminalFileObjectExplorer<AccountCatalog>("account-catalog.json");
        explorer.Show();
    }

    private static bool ShouldDumpSnapshot()
    {
        var value = Environment.GetEnvironmentVariable("CONSOLE_UI_DUMP");

        return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
               || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }
}