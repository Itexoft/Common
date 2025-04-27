// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.EmbeddedWeb;

internal sealed class EmbeddedWebBundle(string bundleId, IEmbeddedArchiveSource source)
{
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private readonly IEmbeddedArchiveSource _source = source ?? throw new ArgumentNullException(nameof(source));

    private InMemoryArchiveFileProvider? _fileProvider;

    public string BundleId { get; } = bundleId ?? throw new ArgumentNullException(nameof(bundleId));

    public EmbeddedArchiveContent? Content { get; private set; }

    public async Task<InMemoryArchiveFileProvider> GetFileProviderAsync(CancellationToken cancellationToken)
    {
        if (this._fileProvider != null)
            return this._fileProvider;

        await this._initializationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (this._fileProvider != null)
                return this._fileProvider;

            this.Content = await EmbeddedArchiveLoader.LoadAsync(this._source, cancellationToken).ConfigureAwait(false);
            this._fileProvider = new(this.Content);

            return this._fileProvider;
        }
        finally
        {
            this._initializationLock.Release();
        }
    }

    public async ValueTask<EmbeddedStaticFile?> TryGetFileAsync(string relativePath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return null;

        _ = await this.GetFileProviderAsync(cancellationToken).ConfigureAwait(false);

        return this.Content != null && this.Content.TryGetFile(relativePath, out var file) ? file : null;
    }
}