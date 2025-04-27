// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Core;
using Itexoft.Net.Core;
using Itexoft.Threading;

namespace Itexoft.Net.Http.Internal;

internal sealed class NetHttpConnection(NetHttpClient owner) : IAsyncDisposable
{
    private readonly Context context = new();
    private readonly NetHttpClient owner = owner ?? throw new ArgumentNullException(nameof(owner));
    private int dead;
    private IAsyncDisposable? disposable;
    private INetStream? stream;

    public async ValueTask DisposeAsync()
    {
        if (await this.context.EnterDisposeAsync().ConfigureAwait(false))
            return;

        await this.CloseCoreAsync().ConfigureAwait(false);
    }

    public async ValueTask<NetHttpConnectionLease> AcquireAsync(NetHttpRequest request, CancelToken cancelToken)
    {
        var enter = await this.context.EnterAsync(cancelToken).ConfigureAwait(false);

        try
        {
            if (this.stream is null || Volatile.Read(ref this.dead) != 0)
                await this.OpenAsync(cancelToken).ConfigureAwait(false);

            var reader = new NetHttpBufferedReader(this.stream!, request.ReceiveBufferSize);

            return new(this, enter, this.stream!, reader);
        }
        catch
        {
            await enter.DisposeAsync().ConfigureAwait(false);

            throw;
        }
    }

    private async ValueTask OpenAsync(CancelToken cancelToken)
    {
        await this.CloseCoreAsync().ConfigureAwait(false);
        Volatile.Write(ref this.dead, 0);

        var connectToken = NetHttpClient.ApplyTimeout(cancelToken, this.owner.ConnectTimeout, false);

        var handle = await this.owner.Connector.ConnectAsync(this.owner.Endpoint, this.MarkDeadAsync, connectToken).ConfigureAwait(false);

        if (handle is null)
            throw new IOException("Failed to establish TCP connection.");

        var netStream = handle.Stream;

        if (this.owner.TlsOptions is not null)
        {
            var sslStream = new NetSslStream(netStream, false);
            await sslStream.AuthenticateAsClientAsync(this.owner.TlsOptions, connectToken).ConfigureAwait(false);
            this.stream = sslStream;
            this.disposable = sslStream;
        }
        else
        {
            this.stream = netStream;
            this.disposable = handle;
        }
    }

    private ValueTask MarkDeadAsync()
    {
        Volatile.Write(ref this.dead, 1);

        return ValueTask.CompletedTask;
    }

    internal async ValueTask ReleaseAsync(NetHttpConnectionLease lease, bool close)
    {
        await lease.Reader.DisposeAsync().ConfigureAwait(false);

        if (close)
            await this.CloseCoreAsync().ConfigureAwait(false);

        await lease.Enter.DisposeAsync().ConfigureAwait(false);
    }

    private async ValueTask CloseCoreAsync()
    {
        this.stream = null;

        if (this.disposable is not null)
        {
            await this.disposable.DisposeAsync().ConfigureAwait(false);
            this.disposable = null;
        }
    }
}

internal sealed class NetHttpConnectionLease(NetHttpConnection owner, IAsyncDisposable enter, INetStream stream, NetHttpBufferedReader reader)
{
    private readonly NetHttpConnection owner = owner ?? throw new ArgumentNullException(nameof(owner));
    private int released;

    internal IAsyncDisposable Enter { get; } = enter ?? throw new ArgumentNullException(nameof(enter));
    public INetStream Stream { get; } = stream ?? throw new ArgumentNullException(nameof(stream));
    public NetHttpBufferedReader Reader { get; } = reader ?? throw new ArgumentNullException(nameof(reader));

    public ValueTask ReleaseAsync(bool close)
    {
        if (Interlocked.Exchange(ref this.released, 1) != 0)
            return ValueTask.CompletedTask;

        return this.owner.ReleaseAsync(this, close);
    }
}
