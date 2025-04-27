// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Text;
using Itexoft.IO;
using Itexoft.Net.Core;

namespace Itexoft.Net.Http;

public interface INetHttpRequestInfo
{
    NetHttpMethod Method { get; }
    NetHttpPathQuery PathAndQuery { get; }
    NetHttpHeaders Headers { get; }
    NetHttpVersion HttpVersion { get; }
    Encoding? Encoding { get; }
    TimeSpan? Timeout { get; }
    bool ReceiveHeadersOnly { get; }
    bool KeepAlive { get; }
    TimeSpan RequestTimeout { get; }
    int SendBufferSize { get; }
    int ReceiveBufferSize { get; }
}

public sealed class NetHttpRequest(NetHttpMethod method, NetHttpPathQuery pathAndQuery) : INetHttpRequestInfo
{
    public IStreamRal? Content { get; set; }
    public NetCookieContainer? CookieContainer { get; set; }
    public NetHttpMethod Method { get; } = method;
    public NetHttpPathQuery PathAndQuery { get; } = pathAndQuery;
    public NetHttpHeaders Headers { get; set; } = new();
    public NetHttpVersion HttpVersion { get; set; } = NetHttpVersion.Version11;

    public Encoding? Encoding { get; set; }
    public TimeSpan? Timeout { get; set; }
    public bool ReceiveHeadersOnly { get; set; }

    public bool KeepAlive { get; set; } = true;
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public int SendBufferSize { get; set; } = 16 * 1024;
    public int ReceiveBufferSize { get; set; } = 16 * 1024;
}
