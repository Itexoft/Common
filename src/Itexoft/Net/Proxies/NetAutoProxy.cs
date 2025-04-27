// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Net.Sockets;
using Itexoft.Net.Core;
using Itexoft.Threading;

namespace Itexoft.Net.Proxies;

public sealed class NetAutoProxy(NetEndpoint endpoint, NetCredential credential = default) : INetProxy
{
    private static readonly TimeSpan probeTimeout = TimeSpan.FromSeconds(2);

    public NetAutoProxy(NetEndpoint endpoint, string? username = null, string? password = null) : this(endpoint, new(username, password)) { }

    public NetAutoProxy(NetDnsHost host, NetPort port, NetCredential credentials = default) : this(new(host, port), credentials) { }

    public async ValueTask<TStream> ConnectAsync<TStream>(NetEndpoint endpoint, TStream stream, CancelToken cancelToken = default)
        where TStream : class, INetStream
    {
        if (stream is INetLazyStream lazyStream)
            return await this.ProbeAsync(endpoint, stream, lazyStream, cancelToken).ConfigureAwait(false);

        throw new SocketException((int)SocketError.ProtocolNotSupported);
    }

    public NetEndpoint Endpoint { get; } = endpoint;
    public NetCredential Credential { get; } = credential;

    private async ValueTask<TStream> ProbeAsync<TStream>(NetEndpoint endpoint, TStream stream, INetLazyStream lazyStream, CancelToken cancelToken)
        where TStream : class, INetStream
    {
        var socks5 = new NetSocks5Proxy(this.Endpoint, this.Credential);
        var https = new NetHttpsProxy(this.Endpoint, this.Credential);
        var http = new NetHttpProxy(this.Endpoint, this.Credential);
        var socks4 = new NetSocks4Proxy(this.Endpoint, this.Credential);
        Exception? last = null;

        if (await tryProbeAsync(socks5, endpoint, stream, lazyStream, cancelToken).ConfigureAwait(false) is { } socks5Stream)
            return socks5Stream;

        if (await tryProbeAsync(http, endpoint, stream, lazyStream, cancelToken).ConfigureAwait(false) is { } httpStream)
            return httpStream;

        if (await tryProbeAsync(https, endpoint, stream, lazyStream, cancelToken).ConfigureAwait(false) is { } httpsStream)
            return httpsStream;

        if (await tryProbeAsync(socks4, endpoint, stream, lazyStream, cancelToken).ConfigureAwait(false) is { } socks4Stream)
            return socks4Stream;

        if (last is not null)
            throw last;

        throw new SocketException((int)SocketError.ProtocolNotSupported);

        async ValueTask<TStream?> tryProbeAsync<TProxy>(TProxy proxy, NetEndpoint target, TStream netStream, INetLazyStream lazy, CancelToken token)
            where TProxy : INetProxy
        {
            var probeToken = token.Branch(probeTimeout);
            var name = proxy.GetType().Name;

            try
            {
                var result = await proxy.ConnectAsync(target, netStream, probeToken).ConfigureAwait(false);

                return result;
            }
            catch (Exception ex) when (ShouldRetryProbe(ex, token))
            {
                last = ex;
                await lazy.DisconnectAsync(token).ConfigureAwait(false);

                return null;
            }
        }
    }

    private static bool ShouldRetryProbe(Exception exception, CancelToken cancelToken)
    {
        if (exception is OperationCanceledException)
            return !cancelToken.IsRequested;

        return !cancelToken.IsRequested;
    }
}
