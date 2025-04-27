// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Threading;
using Itexoft.Threading.Core;

namespace Itexoft.Core;

public sealed class Context
{
    private readonly AsyncLock asyncLock = new();
    private Disposed disposed = new();

    public void ThrowIf() => this.disposed.ThrowIf();

    public void ThrowIf(CancellationToken cancellationToken)
    {
        this.disposed.ThrowIf();
        cancellationToken.ThrowIfCancellationRequested();
    }

    public void ThrowIf(CancelToken cancelToken)
    {
        this.disposed.ThrowIf();
        cancelToken.ThrowIf();
    }

    public async ValueTask<AsyncLock.IEnter> EnterAsync()
    {
        this.disposed.ThrowIf();

        return await this.asyncLock.EnterAsync();
    }

    public async ValueTask<AsyncLock.IEnter> EnterAsync(CancelToken cancelToken)
    {
        this.disposed.ThrowIf();
        cancelToken.ThrowIf();

        return await this.asyncLock.EnterAsync();
    }

    public async ValueTask<bool> EnterDisposeAsync()
    {
        if (this.disposed.Enter())
            return true;

        await this.asyncLock.DisposeAsync();

        return false;
    }
}
