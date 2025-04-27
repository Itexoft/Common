// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Threading;

namespace Itexoft.EmbeddedWeb;

internal sealed class EmbeddedWebBundle(string bundleId, IEmbeddedArchiveSource source)
{
    private readonly SemaphoreSlim initializationLock = new(1, 1);
    private readonly IEmbeddedArchiveSource source = source ?? throw new ArgumentNullException(nameof(source));

    private InMemoryArchiveFileProvider? fileProvider;

    public string BundleId { get; } = bundleId ?? throw new ArgumentNullException(nameof(bundleId));

    public EmbeddedArchiveContent? Content { get; private set; }

    public async Task<InMemoryArchiveFileProvider> GetFileProviderAsync(CancelToken cancelToken)
    {
        if (this.fileProvider != null)
            return this.fileProvider;

        using (cancelToken.Bridge(out var token))
            await this.initializationLock.WaitAsync(token).ConfigureAwait(false);

        try
        {
            if (this.fileProvider != null)
                return this.fileProvider;

            this.Content = await EmbeddedArchiveLoader.LoadAsync(this.source, cancelToken).ConfigureAwait(false);
            this.fileProvider = new(this.Content);

            return this.fileProvider;
        }
        finally
        {
            this.initializationLock.Release();
        }
    }

    public async ValueTask<EmbeddedStaticFile?> TryGetFileAsync(string relativePath, CancelToken cancelToken)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return null;

        _ = await this.GetFileProviderAsync(cancelToken).ConfigureAwait(false);

        return this.Content != null && this.Content.TryGetFile(relativePath, out var file) ? file : null;
    }
}
