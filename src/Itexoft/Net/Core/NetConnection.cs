// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Core;
using Itexoft.Extensions;
using Itexoft.Net.Core.Internal;
using Itexoft.Threading;

namespace Itexoft.Net.Core;

public class NetConnection(NetConnectionOptions options) : IAsyncDisposable
{
    private readonly NetConnectionManager manager = new(options);
    private readonly NetConnectionOptions options = options.Required();
    private Disposed disposed;

    public NetConnectionState State => this.manager.State;

    public ValueTask DisposeAsync()
    {
        if (this.disposed.Enter())
            return ValueTask.CompletedTask;

        return this.manager.DisposeAsync();
    }

    public async ValueTask<INetConnectionHandle?> ConnectAsync(CancelToken cancelToken = default) => await this.manager.ConnectAsync(cancelToken);

    public event EventHandler<NetConnectionEventArgs>? StateChanged
    {
        add => this.manager.StateChanged += value;
        remove => this.manager.StateChanged -= value;
    }
}
