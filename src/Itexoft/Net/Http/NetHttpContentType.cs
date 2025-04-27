// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Core;
using Itexoft.Net.Http.Internal;

namespace Itexoft.Net.Http;

public readonly record struct NetHttpContentType
{
    public static readonly string ApplicationJson = "application/json";
    public static readonly string ApplicationNdJson = "application/x-ndjson";
    public static readonly string ApplicationFormUrlEncoded = "application/x-www-form-urlencoded";
    public static readonly string MultipartFormData = "multipart/form-data";
    public static readonly string ApplicationOctetStream = "application/octet-stream";
    public static readonly string ApplicationXml = "application/xml";
    public static readonly string ApplicationJavascript = "application/javascript";
    public static readonly string ApplicationPdf = "application/pdf";
    public static readonly string ApplicationZip = "application/zip";
    public static readonly string TextPlain = "text/plain";
    public static readonly string TextHtml = "text/html";
    public static readonly string TextCss = "text/css";
    public static readonly string TextEventStream = "text/event-stream";
    public static readonly string TextXml = "text/xml";
    public static readonly string ImagePng = "image/png";
    public static readonly string ImageJpeg = "image/jpeg";
    public static readonly string ImageGif = "image/gif";
    public static readonly string ImageWebp = "image/webp";
    public static readonly string ImageSvg = "image/svg+xml";
    private readonly string? boundary;
    private readonly string? charset;
    private readonly string? mediaType;
    private readonly NetHttpHeaderParam[]? parameters;
    private readonly double? quality;

    private readonly string? value;

    public NetHttpContentType(string? value)
    {
        value ??= string.Empty;
        this.value = value;

        ParseCore(value.AsSpan(), out this.mediaType, out this.parameters, out this.charset, out this.boundary, out this.quality);
    }

    public string Value => this.value ?? string.Empty;
    public string MediaType => this.mediaType ?? string.Empty;
    public IReadOnlyList<NetHttpHeaderParam> Parameters => this.parameters ?? Array.Empty<NetHttpHeaderParam>();
    public string? Charset => this.charset;
    public string? Boundary => this.boundary;
    public double? Quality => this.quality;

    public bool TryGetParameter(string name, out string value)
    {
        value = string.Empty;
        var list = this.parameters;

        if (list is null || list.Length == 0)
            return false;

        foreach (var param in list)
        {
            if (!param.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                continue;

            value = param.Value;

            return true;
        }

        return false;
    }

    public override string ToString() => this.value ?? string.Empty;

    public static implicit operator NetHttpContentType(string value) => new(value);
    public static implicit operator NetHttpContentType(LString value) => new(value);

    public static bool TryParse(string? value, out NetHttpContentType contentType)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            contentType = default;

            return false;
        }

        contentType = new NetHttpContentType(value);

        return true;
    }

    public static NetHttpContentType[] ParseList(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Array.Empty<NetHttpContentType>();

        var list = new List<NetHttpContentType>();
        AppendList(list, value.AsSpan());

        return list.Count == 0 ? Array.Empty<NetHttpContentType>() : list.ToArray();
    }

    public static NetHttpContentType[] ParseList(IEnumerable<string> values)
    {
        if (values is null)
            return Array.Empty<NetHttpContentType>();

        var list = new List<NetHttpContentType>();

        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
                continue;

            AppendList(list, value.AsSpan());
        }

        return list.Count == 0 ? Array.Empty<NetHttpContentType>() : list.ToArray();
    }

    private static void ParseCore(
        ReadOnlySpan<char> span,
        out string? mediaType,
        out NetHttpHeaderParam[]? parameters,
        out string? charset,
        out string? boundary,
        out double? quality)
    {
        mediaType = null;
        parameters = null;
        charset = null;
        boundary = null;
        quality = null;

        span = NetHttpParsing.TrimOws(span);

        if (span.IsEmpty)
            return;

        var semi = IndexOfDelimiter(span);
        var mediaSpan = semi < 0 ? span : span[..semi];
        mediaSpan = NetHttpParsing.TrimOws(mediaSpan);
        mediaType = mediaSpan.IsEmpty ? string.Empty : mediaSpan.ToString();

        if (semi < 0)
            return;

        span = span[(semi + 1)..];
        var list = new List<NetHttpHeaderParam>();

        while (true)
        {
            var part = ReadPart(ref span);

            if (part.IsEmpty)
                break;

            part = NetHttpParsing.TrimOws(part);

            if (part.IsEmpty)
                continue;

            var eq = part.IndexOf('=');
            var nameSpan = eq >= 0 ? part[..eq] : part;
            var valueSpan = eq >= 0 ? part[(eq + 1)..] : ReadOnlySpan<char>.Empty;

            nameSpan = NetHttpParsing.TrimOws(nameSpan);
            valueSpan = NetHttpParsing.TrimOws(valueSpan);

            if (nameSpan.IsEmpty)
                continue;

            if (valueSpan.Length >= 2 && valueSpan[0] == '"' && valueSpan[^1] == '"')
                valueSpan = valueSpan[1..^1];

            var name = nameSpan.ToString();
            var value = valueSpan.ToString();
            list.Add(new(name, value));

            if (charset is null && nameSpan.Equals("charset".AsSpan(), StringComparison.OrdinalIgnoreCase))
                charset = value;
            else if (boundary is null && nameSpan.Equals("boundary".AsSpan(), StringComparison.OrdinalIgnoreCase))
                boundary = value;
            else if (quality is null
                     && nameSpan.Equals("q".AsSpan(), StringComparison.OrdinalIgnoreCase)
                     && TryParseQuality(valueSpan, out var parsedQuality))
                quality = parsedQuality;
        }

        parameters = list.Count == 0 ? Array.Empty<NetHttpHeaderParam>() : list.ToArray();
    }

    private static void AppendList(List<NetHttpContentType> list, ReadOnlySpan<char> span)
    {
        span = NetHttpParsing.TrimOws(span);

        while (!span.IsEmpty)
        {
            var part = ReadListItem(ref span);

            if (!part.IsEmpty)
            {
                part = NetHttpParsing.TrimOws(part);

                if (!part.IsEmpty)
                    list.Add(new NetHttpContentType(part.ToString()));
            }

            span = NetHttpParsing.TrimOws(span);
        }
    }

    private static ReadOnlySpan<char> ReadListItem(ref ReadOnlySpan<char> span)
    {
        if (span.IsEmpty)
            return ReadOnlySpan<char>.Empty;

        var inQuotes = false;
        var prev = '\0';

        for (var i = 0; i < span.Length; i++)
        {
            var c = span[i];

            if (c == '"' && prev != '\\')
                inQuotes = !inQuotes;
            else if (!inQuotes && c == ',')
            {
                var part = span[..i];
                span = span[(i + 1)..];

                return part;
            }

            prev = c;
        }

        var last = span;
        span = ReadOnlySpan<char>.Empty;

        return last;
    }

    private static int IndexOfDelimiter(ReadOnlySpan<char> span)
    {
        var inQuotes = false;
        var prev = '\0';

        for (var i = 0; i < span.Length; i++)
        {
            var c = span[i];

            if (c == '"' && prev != '\\')
                inQuotes = !inQuotes;
            else if (!inQuotes && c == ';')
                return i;

            prev = c;
        }

        return -1;
    }

    private static ReadOnlySpan<char> ReadPart(ref ReadOnlySpan<char> span)
    {
        if (span.IsEmpty)
            return ReadOnlySpan<char>.Empty;

        var inQuotes = false;
        var prev = '\0';

        for (var i = 0; i < span.Length; i++)
        {
            var c = span[i];

            if (c == '"' && prev != '\\')
                inQuotes = !inQuotes;
            else if (!inQuotes && c == ';')
            {
                var part = span[..i];
                span = span[(i + 1)..];

                return part;
            }

            prev = c;
        }

        var last = span;
        span = ReadOnlySpan<char>.Empty;

        return last;
    }

    private static bool TryParseQuality(ReadOnlySpan<char> span, out double value)
    {
        value = 0;
        span = NetHttpParsing.TrimOws(span);

        if (span.IsEmpty)
            return false;

        var index = 0;
        long whole = 0;

        while (index < span.Length)
        {
            var digit = span[index] - '0';

            if ((uint)digit > 9)
                break;

            whole = whole * 10 + digit;
            index++;
        }

        var fraction = 0L;
        var scale = 1L;

        if (index < span.Length && span[index] == '.')
        {
            index++;

            while (index < span.Length)
            {
                var digit = span[index] - '0';

                if ((uint)digit > 9)
                    break;

                fraction = fraction * 10 + digit;
                scale *= 10;
                index++;
            }
        }

        if (index != span.Length)
            return false;

        var result = whole + (scale > 1 ? fraction / (double)scale : 0);

        if (result < 0 || result > 1)
            return false;

        value = result;

        return true;
    }
}

public readonly record struct NetHttpHeaderParam(string Name, string Value)
{
    public static implicit operator NetHttpHeaderParam((string Name, string Value) value) => new(value.Name, value.Value);
    public static implicit operator NetHttpHeaderParam(KeyValuePair<string, string> value) => new(value.Key, value.Value);
}
