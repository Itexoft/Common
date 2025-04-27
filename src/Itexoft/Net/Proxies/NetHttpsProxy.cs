// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

#nullable enable
using System.Net.Security;
using System.Security.Authentication;
using Itexoft.Net.Core;
using Itexoft.Reflection;
using Itexoft.Threading;

namespace Itexoft.Net.Proxies;

public sealed class NetHttpsProxy(NetEndpoint endpoint, NetCredential credentials = default) : NetHttpProxy(endpoint, credentials), INetProxy
{
    public NetHttpsProxy(NetDnsHost host, NetPort port, NetCredential credentials = default) : this(new(host, port), credentials) { }

    public async override ValueTask<TStream> ConnectAsync<TStream>(NetEndpoint endpoint, TStream stream, CancelToken cancelToken = default)
    {
        var sslStream = await this.EnsureSslAsync(endpoint, stream, false, cancelToken).ConfigureAwait(false);

        return await base.ConnectAsync(endpoint, sslStream, cancelToken).ConfigureAwait(false);
    }

    private async ValueTask<TStream> EnsureSslAsync<TStream>(NetEndpoint endpoint, TStream stream, bool leaveInnerOpen, CancelToken cancelToken)
        where TStream : class, INetStream
    {
        var sslStream = new NetSslStream(stream, leaveInnerOpen);

        if (!sslStream.IsAuthenticated)
        {
            var proxyHost = (string)this.Endpoint.Host;

            var opts = new SslClientAuthenticationOptions
            {
                TargetHost = proxyHost,
                EnabledSslProtocols = SslProtocols.None,
                RemoteCertificateValidationCallback = static (_, _, _, _) => true,
            };

            if (string.IsNullOrWhiteSpace(opts.TargetHost))
                opts.TargetHost = proxyHost;

            await sslStream.AuthenticateAsClientAsync(opts, cancelToken).ConfigureAwait(false);
        }

        return Interfaces.Overlay(stream, sslStream);
    }
}
