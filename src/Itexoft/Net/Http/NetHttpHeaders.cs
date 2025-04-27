// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Collections;
using System.Globalization;
using Itexoft.Extensions;
using Itexoft.Net.Http.Internal;

namespace Itexoft.Net.Http;

public sealed class NetHttpHeaders : IEnumerable<KeyValuePair<string, string>>
{
    private readonly Dictionary<string, List<string>> headers = new(StringComparer.OrdinalIgnoreCase);

    public NetHttpHeaders() { }

    public NetHttpHeaders(IEnumerable<KeyValuePair<string, string>> headers)
    {
        foreach (var header in headers)
            this.Add(header.Key, header.Value);
    }

    public int Count => this.headers.Count;

    public string? this[string name]
    {
        get => this.TryGetValue(name, out var value) ? value : null;
        set
        {
            name = name.RequiredNotWhiteSpace();

            if (value is null)
            {
                this.headers.Remove(name);

                return;
            }

            this.headers[name] = [value];
        }
    }

    public string? Connection
    {
        get => this["Connection"];
        set => this["Connection"] = value;
    }

    public bool ConnectionKeepAlive =>
        this.TryGetValue("Connection", out var value) && NetHttpParsing.ContainsToken(value.AsSpan(), "keep-alive".AsSpan());

    public bool ConnectionClose =>
        this.TryGetValue("Connection", out var value) && NetHttpParsing.ContainsToken(value.AsSpan(), "close".AsSpan());

    public string? Host
    {
        get => this["Host"];
        set => this["Host"] = value;
    }

    public NetHttpContentType ContentType
    {
        get => this.TryGetValue("Content-Type", out var value) && !string.IsNullOrWhiteSpace(value) ? new NetHttpContentType(value) : default;
        set => this["Content-Type"] = value.ToString();
    }

    public long? ContentLength
    {
        get
        {
            if (!this.TryGetValue("Content-Length", out var value))
                return null;

            if (!NetHttpParsing.TryParseInt64(value.AsSpan(), out var result))
                return null;

            return result < 0 ? null : result;
        }
        set => this["Content-Length"] = value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : null;
    }

    public string? TransferEncoding
    {
        get => this["Transfer-Encoding"];
        set => this["Transfer-Encoding"] = value;
    }

    public bool TransferEncodingChunked =>
        this.TryGetValue("Transfer-Encoding", out var value) && NetHttpParsing.ContainsToken(value.AsSpan(), "chunked".AsSpan());

    public NetHttpContentTypeList Accept => new(this, "Accept");

    public string? AcceptEncoding
    {
        get => this["Accept-Encoding"];
        set => this["Accept-Encoding"] = value;
    }

    public string? AcceptLanguage
    {
        get => this["Accept-Language"];
        set => this["Accept-Language"] = value;
    }

    public string? AcceptCharset
    {
        get => this["Accept-Charset"];
        set => this["Accept-Charset"] = value;
    }

    public string? UserAgent
    {
        get => this["User-Agent"];
        set => this["User-Agent"] = value;
    }

    public string? Referer
    {
        get => this["Referer"];
        set => this["Referer"] = value;
    }

    public string? Origin
    {
        get => this["Origin"];
        set => this["Origin"] = value;
    }

    public string? Expect
    {
        get => this["Expect"];
        set => this["Expect"] = value;
    }

    public string? Authorization
    {
        get => this["Authorization"];
        set => this["Authorization"] = value;
    }

    public string? ProxyAuthorization
    {
        get => this["Proxy-Authorization"];
        set => this["Proxy-Authorization"] = value;
    }

    public string? ProxyConnection
    {
        get => this["Proxy-Connection"];
        set => this["Proxy-Connection"] = value;
    }

    public bool ProxyConnectionClose =>
        this.TryGetValue("Proxy-Connection", out var value) && NetHttpParsing.ContainsToken(value.AsSpan(), "close".AsSpan());

    public string? Location
    {
        get => this["Location"];
        set => this["Location"] = value;
    }

    public string? Server
    {
        get => this["Server"];
        set => this["Server"] = value;
    }

    public string? Date
    {
        get => this["Date"];
        set => this["Date"] = value;
    }

    public string? CacheControl
    {
        get => this["Cache-Control"];
        set => this["Cache-Control"] = value;
    }

    public string? UpgradeInsecureRequests
    {
        get => this["Upgrade-Insecure-Requests"];
        set => this["Upgrade-Insecure-Requests"] = value;
    }

    public string? Pragma
    {
        get => this["Pragma"];
        set => this["Pragma"] = value;
    }

    public string? ContentEncoding
    {
        get => this["Content-Encoding"];
        set => this["Content-Encoding"] = value;
    }

    public string? Cookie
    {
        get => this["Cookie"];
        set => this["Cookie"] = value;
    }

    public IReadOnlyList<string> SetCookie => this.GetValues("Set-Cookie");

    public IEnumerator<KeyValuePair<string, string>> GetEnumerator()
    {
        foreach (var pair in this.headers)
        {
            foreach (var value in pair.Value)
                yield return new(pair.Key, value);
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();

    public void Add(IEnumerable<KeyValuePair<string, string>> values)
    {
        foreach (var pair in values)
            this.Add(pair.Key, pair.Value);
    }

    public void Add(string name, string value)
    {
        name = name.RequiredNotWhiteSpace();
        value = value ?? string.Empty;

        if (!this.headers.TryGetValue(name, out var values))
        {
            values = new(1);
            this.headers.Add(name, values);
        }

        values.Add(value);
    }

    public bool Remove(string name) => this.headers.Remove(name);

    public void Clear() => this.headers.Clear();

    public bool TryGetValue(string name, out string? value)
    {
        value = null;

        if (!this.headers.TryGetValue(name, out var values) || values.Count == 0)
            return false;

        value = values[0];

        return true;
    }

    public IReadOnlyList<string> GetValues(string name) =>
        this.headers.TryGetValue(name, out var values) ? values : Array.Empty<string>();

    public bool Contains(string name) => this.headers.ContainsKey(name);
}
