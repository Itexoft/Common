// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Text;
using Itexoft.Extensions;

namespace Itexoft.Net.Core;

public readonly struct NetCookie(
    string name,
    string value,
    string? domain = null,
    string? path = null,
    DateTimeOffset? expiresUtc = null,
    bool secure = false,
    bool httpOnly = false,
    bool hostOnly = false)
{
    public string Name { get; } = name.RequiredNotWhiteSpace();
    public string Value { get; } = value ?? string.Empty;
    public string? Domain { get; } = NormalizeDomain(domain);
    public string? Path { get; } = NormalizePath(path);
    public DateTimeOffset? ExpiresUtc { get; } = expiresUtc;
    public bool Secure { get; } = secure;
    public bool HttpOnly { get; } = httpOnly;
    public bool HostOnly { get; } = hostOnly;

    public bool IsExpired(DateTimeOffset now) => this.ExpiresUtc.HasValue && this.ExpiresUtc.Value <= now;

    internal static bool TryParseSetCookie(
        ReadOnlySpan<char> value,
        string defaultDomain,
        string defaultPath,
        DateTimeOffset now,
        out NetCookie cookie)
    {
        cookie = default;

        if (value.IsEmpty)
            return false;

        var remaining = value;
        var first = ReadSegment(ref remaining);

        if (!TrySplitNameValue(first, out var nameSpan, out var valueSpan))
            return false;

        var name = nameSpan.ToString();
        var val = valueSpan.ToString();
        var domain = defaultDomain;
        var path = defaultPath;
        var secure = false;
        var httpOnly = false;
        var hostOnly = true;
        DateTimeOffset? expiresUtc = null;

        while (!remaining.IsEmpty)
        {
            var segment = ReadSegment(ref remaining);

            if (segment.IsEmpty)
                continue;

            if (IsToken(segment, "secure"))
            {
                secure = true;

                continue;
            }

            if (IsToken(segment, "httponly"))
            {
                httpOnly = true;

                continue;
            }

            if (!TrySplitNameValue(segment, out var attrName, out var attrValue))
                continue;

            if (IsToken(attrName, "domain"))
            {
                domain = NormalizeDomain(attrValue.ToString()) ?? domain;
                hostOnly = false;

                continue;
            }

            if (IsToken(attrName, "path"))
            {
                path = NormalizePath(attrValue.ToString()) ?? path;

                continue;
            }

            if (IsToken(attrName, "max-age") && TryParseInt64(attrValue, out var seconds))
            {
                expiresUtc = seconds <= 0 ? now.AddSeconds(-1) : now.AddSeconds(seconds);

                continue;
            }

            if (IsToken(attrName, "expires") && TryParseHttpDate(attrValue, out var expires))
                expiresUtc = expires;
        }

        cookie = new(name, val, domain, path, expiresUtc, secure, httpOnly, hostOnly);

        return true;
    }

    private static ReadOnlySpan<char> ReadSegment(ref ReadOnlySpan<char> value)
    {
        value = TrimWhitespace(value);

        if (value.IsEmpty)
            return ReadOnlySpan<char>.Empty;

        var idx = value.IndexOf(';');

        if (idx < 0)
        {
            var segment = TrimWhitespace(value);
            value = ReadOnlySpan<char>.Empty;

            return segment;
        }

        var result = TrimWhitespace(value[..idx]);
        value = value[(idx + 1)..];

        return result;
    }

    private static bool TrySplitNameValue(ReadOnlySpan<char> value, out ReadOnlySpan<char> name, out ReadOnlySpan<char> val)
    {
        var idx = value.IndexOf('=');

        if (idx < 0)
        {
            name = TrimWhitespace(value);
            val = ReadOnlySpan<char>.Empty;

            return false;
        }

        name = TrimWhitespace(value[..idx]);
        val = TrimWhitespace(value[(idx + 1)..]);

        if (val.Length >= 2 && val[0] == '"' && val[^1] == '"')
            val = val[1..^1];

        return !name.IsEmpty;
    }

    private static bool TryParseInt64(ReadOnlySpan<char> value, out long result)
    {
        result = 0;
        value = TrimWhitespace(value);

        if (value.IsEmpty)
            return false;

        var sign = 1;
        var index = 0;

        if (value[0] == '-')
        {
            sign = -1;
            index = 1;
        }
        else if (value[0] == '+')
            index = 1;

        if (index >= value.Length)
            return false;

        long acc = 0;

        for (; index < value.Length; index++)
        {
            var c = value[index];
            var digit = c - '0';

            if ((uint)digit > 9)
                return false;

            acc = acc * 10 + digit;
        }

        result = acc * sign;

        return true;
    }

    private static bool TryParseHttpDate(ReadOnlySpan<char> value, out DateTimeOffset result)
    {
        result = default;
        value = TrimWhitespace(value);

        if (value.IsEmpty)
            return false;

        if (value.Length >= 5 && value[3] == ',' && value[4] == ' ')
            value = value[5..];

        if (value.Length < 20)
            return false;

        if (!TryParse2Digit(value, out var day))
            return false;

        if (value.Length < 3 || value[2] != ' ')
            return false;

        value = value[3..];

        if (!TryParseMonth(value, out var month))
            return false;

        if (value.Length < 5 || value[3] != ' ')
            return false;

        value = value[4..];

        if (!TryParse4Digit(value, out var year))
            return false;

        if (value.Length < 6 || value[4] != ' ')
            return false;

        value = value[5..];

        if (value.Length < 8)
            return false;

        if (!TryParse2Digit(value, out var hour) || value[2] != ':')
            return false;

        if (!TryParse2Digit(value[3..], out var minute) || value[5] != ':')
            return false;

        if (!TryParse2Digit(value[6..], out var second))
            return false;

        try
        {
            result = new(year, month, day, hour, minute, second, TimeSpan.Zero);

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryParse2Digit(ReadOnlySpan<char> value, out int result)
    {
        result = 0;

        if (value.Length < 2)
            return false;

        var d1 = value[0] - '0';
        var d2 = value[1] - '0';

        if ((uint)d1 > 9 || (uint)d2 > 9)
            return false;

        result = d1 * 10 + d2;

        return true;
    }

    private static bool TryParse4Digit(ReadOnlySpan<char> value, out int result)
    {
        result = 0;

        if (value.Length < 4)
            return false;

        var d1 = value[0] - '0';
        var d2 = value[1] - '0';
        var d3 = value[2] - '0';
        var d4 = value[3] - '0';

        if ((uint)d1 > 9 || (uint)d2 > 9 || (uint)d3 > 9 || (uint)d4 > 9)
            return false;

        result = d1 * 1000 + d2 * 100 + d3 * 10 + d4;

        return true;
    }

    private static bool TryParseMonth(ReadOnlySpan<char> value, out int month)
    {
        month = 0;

        if (value.Length < 3)
            return false;

        var token = value[..3];

        if (IsToken(token, "jan"))
        {
            month = 1;

            return true;
        }

        if (IsToken(token, "feb"))
        {
            month = 2;

            return true;
        }

        if (IsToken(token, "mar"))
        {
            month = 3;

            return true;
        }

        if (IsToken(token, "apr"))
        {
            month = 4;

            return true;
        }

        if (IsToken(token, "may"))
        {
            month = 5;

            return true;
        }

        if (IsToken(token, "jun"))
        {
            month = 6;

            return true;
        }

        if (IsToken(token, "jul"))
        {
            month = 7;

            return true;
        }

        if (IsToken(token, "aug"))
        {
            month = 8;

            return true;
        }

        if (IsToken(token, "sep"))
        {
            month = 9;

            return true;
        }

        if (IsToken(token, "oct"))
        {
            month = 10;

            return true;
        }

        if (IsToken(token, "nov"))
        {
            month = 11;

            return true;
        }

        if (IsToken(token, "dec"))
        {
            month = 12;

            return true;
        }

        return false;
    }

    private static bool IsToken(ReadOnlySpan<char> value, ReadOnlySpan<char> token)
    {
        if (value.Length != token.Length)
            return false;

        for (var i = 0; i < value.Length; i++)
        {
            var a = value[i];
            var b = token[i];

            if (a == b)
                continue;

            if (a >= 'A' && a <= 'Z')
                a = (char)(a + 32);

            if (b >= 'A' && b <= 'Z')
                b = (char)(b + 32);

            if (a != b)
                return false;
        }

        return true;
    }

    private static ReadOnlySpan<char> TrimWhitespace(ReadOnlySpan<char> value)
    {
        var start = 0;
        var end = value.Length;

        while (start < end && IsWhitespace(value[start]))
            start++;

        while (end > start && IsWhitespace(value[end - 1]))
            end--;

        return value[start..end];
    }

    private static bool IsWhitespace(char c) => c is ' ' or '\t';

    private static string? NormalizeDomain(string? domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
            return null;

        domain = domain.Trim();

        if (domain.StartsWith('.'))
            domain = domain[1..];

        return domain.Length == 0 ? null : domain.ToLowerInvariant();
    }

    private static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        path = path.Trim();

        if (path.Length == 0)
            return null;

        return path[0] == '/' ? path : $"/{path}";
    }
}

public class NetCookieContainer
{
    private readonly List<NetCookie> cookies = [];
    private readonly object gate = new();

    public void Add(NetCookie cookie)
    {
        if (string.IsNullOrWhiteSpace(cookie.Name))
            return;

        var now = DateTimeOffset.UtcNow;

        lock (this.gate)
        {
            if (cookie.IsExpired(now))
            {
                this.removeNoLock(cookie);

                return;
            }

            for (var i = 0; i < this.cookies.Count; i++)
            {
                if (IsSameIdentity(this.cookies[i], cookie))
                {
                    this.cookies[i] = cookie;

                    return;
                }
            }

            this.cookies.Add(cookie);
        }
    }

    public bool Remove(string name, string? domain = null, string? path = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        var normalizedDomain = NormalizeDomain(domain);
        var normalizedPath = NormalizePath(path);

        lock (this.gate)
        {
            for (var i = this.cookies.Count - 1; i >= 0; i--)
            {
                var existing = this.cookies[i];

                if (!NameEquals(existing.Name, name))
                    continue;

                if (normalizedDomain is not null && !DomainEquals(existing.Domain, normalizedDomain))
                    continue;

                if (normalizedPath is not null && !PathEquals(existing.Path, normalizedPath))
                    continue;

                this.cookies.RemoveAt(i);

                return true;
            }
        }

        return false;
    }

    public void Clear()
    {
        lock (this.gate)
            this.cookies.Clear();
    }

    public string? GetCookieHeader(NetEndpoint endpoint, string path, bool secure)
    {
        var host = (string)endpoint.Host;
        var normalizedHost = host.ToLowerInvariant();
        var normalizedPath = string.IsNullOrEmpty(path) ? "/" : path;
        var now = DateTimeOffset.UtcNow;
        List<NetCookie>? matches = null;

        lock (this.gate)
        {
            for (var i = this.cookies.Count - 1; i >= 0; i--)
            {
                var cookie = this.cookies[i];

                if (cookie.IsExpired(now))
                {
                    this.cookies.RemoveAt(i);

                    continue;
                }

                if (cookie.Secure && !secure)
                    continue;

                if (!MatchDomain(cookie, normalizedHost))
                    continue;

                if (!MatchPath(cookie.Path, normalizedPath))
                    continue;

                matches ??= [];
                matches.Add(cookie);
            }
        }

        if (matches is null || matches.Count == 0)
            return null;

        var builder = new StringBuilder();

        for (var i = 0; i < matches.Count; i++)
        {
            if (i > 0)
                builder.Append("; ");

            builder.Append(matches[i].Name).Append('=').Append(matches[i].Value);
        }

        return builder.ToString();
    }

    private void removeNoLock(NetCookie cookie)
    {
        for (var i = this.cookies.Count - 1; i >= 0; i--)
        {
            if (IsSameIdentity(this.cookies[i], cookie))
            {
                this.cookies.RemoveAt(i);

                return;
            }
        }
    }

    private static bool IsSameIdentity(NetCookie left, NetCookie right) =>
        NameEquals(left.Name, right.Name) && DomainEquals(left.Domain, right.Domain) && PathEquals(left.Path, right.Path);

    private static bool NameEquals(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static bool DomainEquals(string? left, string? right) =>
        string.Equals(left ?? string.Empty, right ?? string.Empty, StringComparison.OrdinalIgnoreCase);

    private static bool PathEquals(string? left, string? right) =>
        string.Equals(left ?? string.Empty, right ?? string.Empty, StringComparison.Ordinal);

    private static bool MatchDomain(NetCookie cookie, string host)
    {
        var domain = cookie.Domain;

        if (string.IsNullOrEmpty(domain))
            return true;

        if (cookie.HostOnly)
            return string.Equals(host, domain, StringComparison.OrdinalIgnoreCase);

        if (string.Equals(host, domain, StringComparison.OrdinalIgnoreCase))
            return true;

        if (host.Length <= domain.Length)
            return false;

        if (!host.EndsWith(domain, StringComparison.OrdinalIgnoreCase))
            return false;

        return host[host.Length - domain.Length - 1] == '.';
    }

    private static bool MatchPath(string? cookiePath, string requestPath)
    {
        if (string.IsNullOrEmpty(cookiePath))
            return true;

        if (requestPath.Length < cookiePath.Length)
            return false;

        return requestPath.StartsWith(cookiePath, StringComparison.Ordinal);
    }

    private static string? NormalizeDomain(string? domain) => NetCookieContainerHelpers.NormalizeDomain(domain);
    private static string? NormalizePath(string? path) => NetCookieContainerHelpers.NormalizePath(path);

    private static class NetCookieContainerHelpers
    {
        public static string? NormalizeDomain(string? domain)
        {
            if (string.IsNullOrWhiteSpace(domain))
                return null;

            domain = domain.Trim();

            if (domain.StartsWith('.'))
                domain = domain[1..];

            return domain.Length == 0 ? null : domain.ToLowerInvariant();
        }

        public static string? NormalizePath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            path = path.Trim();

            if (path.Length == 0)
                return null;

            return path[0] == '/' ? path : $"/{path}";
        }
    }
}
