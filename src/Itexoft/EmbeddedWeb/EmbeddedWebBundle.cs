// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.EmbeddedWeb;

internal sealed class EmbeddedWebBundle
{
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private readonly IEmbeddedArchiveSource _source;
    private EmbeddedArchiveContent? _content;

    private InMemoryArchiveFileProvider? _fileProvider;

    public EmbeddedWebBundle(string bundleId, IEmbeddedArchiveSource source)
    {
        this.BundleId = bundleId ?? throw new ArgumentNullException(nameof(bundleId));
        this._source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public string BundleId { get; }

    public EmbeddedArchiveContent? Content => this._content;

    public async Task<InMemoryArchiveFileProvider> GetFileProviderAsync(CancellationToken cancellationToken)
    {
        if (this._fileProvider != null)
            return this._fileProvider;

        await this._initializationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (this._fileProvider != null)
                return this._fileProvider;

            this._content = await EmbeddedArchiveLoader.LoadAsync(this._source, cancellationToken).ConfigureAwait(false);
            this._fileProvider = new(this._content);

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

        return this._content != null && this._content.TryGetFile(relativePath, out var file) ? file : null;
    }
}