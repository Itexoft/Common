// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.IO.VFS.Core;

namespace Itexoft.IO.VFS.Tests;

[SetUpFixture]
internal sealed class TestAssemblySetup
{
    private bool originalAllowTiny = PageSizing.AllowTinyPages;
    private int? originalOverride = PageSizing.DefaultPageSizeOverride;

    [OneTimeSetUp]
    public void ConfigurePageSizing()
    {
        var value = Environment.GetEnvironmentVariable("VFS_FORCE_PAGE_SIZE");
        if (!string.IsNullOrWhiteSpace(value) && int.TryParse(value, out var forced) && forced > 0)
        {
            PageSizing.AllowTinyPages = true;
            PageSizing.DefaultPageSizeOverride = forced;
        }
        else
        {
            PageSizing.AllowTinyPages = false;
            PageSizing.DefaultPageSizeOverride = null;
        }
    }

    [OneTimeTearDown]
    public void RestorePageSizing()
    {
        PageSizing.AllowTinyPages = this.originalAllowTiny;
        PageSizing.DefaultPageSizeOverride = this.originalOverride;
    }
}