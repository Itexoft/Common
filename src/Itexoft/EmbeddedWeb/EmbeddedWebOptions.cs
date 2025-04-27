// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Microsoft.AspNetCore.StaticFiles;

namespace Itexoft.EmbeddedWeb;

public sealed class EmbeddedWebOptions
{
    private readonly List<string> _defaultFileNames = ["index.html"];

    public IList<string> DefaultFileNames => this._defaultFileNames;

    public bool EnableSpaFallback { get; set; } = true;

    public string? SpaFallbackFile { get; set; }

    public TimeSpan? StaticFilesCacheDuration { get; set; }

    public bool EnableDirectoryBrowsing { get; set; }

    public Action<StaticFileResponseContext>? OnPrepareResponse { get; set; }
}