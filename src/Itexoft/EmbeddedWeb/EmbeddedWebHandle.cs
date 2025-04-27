// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Microsoft.AspNetCore.Builder;

namespace Itexoft.EmbeddedWeb;

public sealed class EmbeddedWebHandle : IAsyncDisposable, IDisposable
{
    private readonly WebApplication _app;
    private readonly Task _completion;
    private bool _disposed;

    internal EmbeddedWebHandle(string bundleId, WebApplication app, Task completion)
    {
        this.BundleId = bundleId;
        this._app = app;
        this._completion = completion;
    }

    public string BundleId { get; }

    public Task Completion => this._completion;

    public ICollection<string> Urls => this._app.Urls;

    public async ValueTask DisposeAsync()
    {
        if (this._disposed)
            return;

        this._disposed = true;
        try
        {
            await this._app.StopAsync().ConfigureAwait(false);
        }
        finally
        {
            await this._completion.ConfigureAwait(false);
            await this._app.DisposeAsync().ConfigureAwait(false);
        }
    }

    void IDisposable.Dispose()
        => this.DisposeAsync().AsTask().GetAwaiter().GetResult();

    public async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        if (this._disposed)
            return;

        await this._app.StopAsync(cancellationToken).ConfigureAwait(false);
    }
}