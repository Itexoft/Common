// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Itexoft.Extensions;
using Itexoft.IO;
using Itexoft.Threading;

namespace Itexoft.Net.Core;

public class NetSslStream(
    INetStream innerStream,
    bool leaveInnerStreamOpen,
    RemoteCertificateValidationCallback? userCertificateValidationCallback = null,
    LocalCertificateSelectionCallback? userCertificateSelectionCallback = null,
    EncryptionPolicy encryptionPolicy = EncryptionPolicy.RequireEncryption) : AuthStream, INetStream
{
    private readonly SslStream sslStream = new(
        innerStream.AsStream(),
        leaveInnerStreamOpen,
        userCertificateValidationCallback,
        userCertificateSelectionCallback,
        encryptionPolicy);

    public NetSslStream(NetStream innerStream) : this(innerStream, false, null, null) { }

    public NetSslStream(NetStream innerStream, bool leaveInnerStreamOpen, RemoteCertificateValidationCallback? userCertificateValidationCallback) :
        this(innerStream, leaveInnerStreamOpen, userCertificateValidationCallback, null, EncryptionPolicy.RequireEncryption) { }

    public NetSslStream(
        NetStream innerStream,
        bool leaveInnerStreamOpen,
        RemoteCertificateValidationCallback? userCertificateValidationCallback,
        LocalCertificateSelectionCallback? userCertificateSelectionCallback) : this(
        innerStream,
        leaveInnerStreamOpen,
        userCertificateValidationCallback,
        userCertificateSelectionCallback,
        EncryptionPolicy.RequireEncryption) { }

    public bool IsAuthenticated => this.sslStream.IsAuthenticated;

    public TimeSpan ReadTimeout
    {
        get => TimeSpan.FromMilliseconds(this.sslStream.ReadTimeout);
        set => this.sslStream.ReadTimeout = value.TimeoutMilliseconds;
    }

    public TimeSpan WriteTimeout
    {
        get => TimeSpan.FromMilliseconds(this.sslStream.WriteTimeout);
        set => this.sslStream.WriteTimeout = value.TimeoutMilliseconds;
    }

    public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancelToken cancelToken = default)
    {
        using (cancelToken.Bridge(out var token))
            return await this.sslStream.ReadAsync(buffer, token);
    }

    public async ValueTask FlushAsync(CancelToken cancelToken)
    {
        using (cancelToken.Bridge(out var token))
            await this.sslStream.FlushAsync(token);
    }

    public async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancelToken cancelToken = default)
    {
        using (cancelToken.Bridge(out var token))
            await this.sslStream.WriteAsync(buffer, token);
    }

    protected override ValueTask DisposeAny() => this.sslStream.DisposeAsync();

    public async Task AuthenticateAsClientAsync(SslClientAuthenticationOptions sslOptions, CancelToken cancelToken)
    {
        using (cancelToken.Bridge(out var token))
            await this.sslStream.AuthenticateAsClientAsync(sslOptions, token);
    }

    public Task AuthenticateAsServerAsync(X509Certificate2 serverCertificate, bool b, SslProtocols none, bool b1) =>
        this.sslStream.AuthenticateAsServerAsync(serverCertificate, b1, none, b1);
}
