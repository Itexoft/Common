// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Core;
using Itexoft.Threading;
using Microsoft.AspNetCore.Builder;

namespace Itexoft.EmbeddedWeb;

public sealed class EmbeddedWebHandle : IAsyncDisposable, IDisposable
{
    private readonly WebApplication app;
    private Disposed disposed;

    internal EmbeddedWebHandle(string bundleId, WebApplication app, Task completion)
    {
        this.BundleId = bundleId;
        this.app = app;
        this.Completion = completion;
    }

    public string BundleId { get; }

    public Task Completion { get; }

    public ICollection<string> Urls => this.app.Urls;

    public async ValueTask DisposeAsync()
    {
        if (this.disposed.Enter())
            return;

        try
        {
            await this.app.StopAsync().ConfigureAwait(false);
        }
        finally
        {
            await this.Completion.ConfigureAwait(false);
            await this.app.DisposeAsync().ConfigureAwait(false);
        }
    }

    void IDisposable.Dispose() => this.DisposeAsync().GetAwaiter().GetResult();

    public async ValueTask StopAsync(CancelToken cancelToken = default)
    {
        if (this.disposed)
            return;

        using (cancelToken.Bridge(out var token))
            await this.app.StopAsync(token).ConfigureAwait(false);
    }
}
