// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Core;
using Itexoft.Extensions;
using Itexoft.Threading;

namespace Itexoft.Net.Core;

public interface INetLazyStream : INetStream
{
    ValueTask DisconnectAsync(CancelToken cancelToken = default);
}

public sealed class NetLazyStream(Func<CancelToken, ValueTask<INetStream>> connector) : INetLazyStream
{
    private readonly Func<CancelToken, ValueTask<INetStream>> connector = connector.Required();
    private readonly Context context = new();
    private INetStream? inner;
    private TimeSpan readTimeout;
    private bool readTimeoutSet;
    private TimeSpan writeTimeout;
    private bool writeTimeoutSet;

    public NetLazyStream(Func<ValueTask<INetStream>> connector) : this(_ => connector()) { }

    public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancelToken cancelToken = default)
    {
        await using (await this.context.EnterAsync(cancelToken).ConfigureAwait(false))
            return await this.ReadCoreAsync(buffer, cancelToken).ConfigureAwait(false);
    }

    public async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancelToken cancelToken = default)
    {
        await using (await this.context.EnterAsync(cancelToken).ConfigureAwait(false))
            await this.WriteCoreAsync(buffer, cancelToken).ConfigureAwait(false);
    }

    public async ValueTask FlushAsync(CancelToken cancelToken = default)
    {
        await using (await this.context.EnterAsync(cancelToken).ConfigureAwait(false))
            await this.FlushCoreAsync(cancelToken).ConfigureAwait(false);
    }

    public async ValueTask DisconnectAsync(CancelToken cancelToken = default)
    {
        await using (await this.context.EnterAsync(cancelToken).ConfigureAwait(false))
        {
            var stream = this.inner;
            this.inner = null;

            if (stream is not null)
                await stream.DisposeAsync().ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (await this.context.EnterDisposeAsync().ConfigureAwait(false))
            return;

        var stream = this.inner;
        this.inner = null;

        if (stream is not null)
            await stream.DisposeAsync().ConfigureAwait(false);
    }

    public TimeSpan WriteTimeout
    {
        get => this.inner is { } stream ? stream.WriteTimeout : this.writeTimeoutSet ? this.writeTimeout : TimeSpan.Zero;
        set
        {
            this.writeTimeout = value;
            this.writeTimeoutSet = true;

            if (this.inner is { } stream)
                stream.WriteTimeout = value;
        }
    }

    public TimeSpan ReadTimeout
    {
        get => this.inner is { } stream ? stream.ReadTimeout : this.readTimeoutSet ? this.readTimeout : TimeSpan.Zero;
        set
        {
            this.readTimeout = value;
            this.readTimeoutSet = true;

            if (this.inner is { } stream)
                stream.ReadTimeout = value;
        }
    }

    private async ValueTask<int> ReadCoreAsync(Memory<byte> buffer, CancelToken cancelToken)
    {
        var stream = await this.EnsureStreamAsync(cancelToken).ConfigureAwait(false);

        return await stream.ReadAsync(buffer, cancelToken).ConfigureAwait(false);
    }

    private async ValueTask WriteCoreAsync(ReadOnlyMemory<byte> buffer, CancelToken cancelToken)
    {
        var stream = await this.EnsureStreamAsync(cancelToken).ConfigureAwait(false);

        await stream.WriteAsync(buffer, cancelToken).ConfigureAwait(false);
    }

    private async ValueTask FlushCoreAsync(CancelToken cancelToken)
    {
        var stream = await this.EnsureStreamAsync(cancelToken).ConfigureAwait(false);

        await stream.FlushAsync(cancelToken).ConfigureAwait(false);
    }

    private async ValueTask<INetStream> EnsureStreamAsync(CancelToken cancelToken)
    {
        if (this.inner is { } stream)
            return stream;

        stream = await this.connector(cancelToken.ThrowIf()).ConfigureAwait(false);
        this.ApplyTimeouts(stream);
        this.inner = stream;

        return stream;
    }

    private void ApplyTimeouts(INetStream stream)
    {
        if (this.readTimeoutSet)
            stream.ReadTimeout = this.readTimeout;

        if (this.writeTimeoutSet)
            stream.WriteTimeout = this.writeTimeout;
    }
}
