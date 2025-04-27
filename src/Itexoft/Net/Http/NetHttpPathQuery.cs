// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Buffers;
using System.Text;
using Itexoft.Extensions;

namespace Itexoft.Net.Http;

public readonly record struct NetHttpPathQuery
{
    public NetHttpPathQuery(string path, params NetHttpQueryParam[] query)
    {
        this.Path = NormalizePath(path);
        this.Query = query ?? Array.Empty<NetHttpQueryParam>();
    }

    public string Path { get; init; }
    public IReadOnlyList<NetHttpQueryParam> Query { get; init; }

    public static implicit operator NetHttpPathQuery(string value) => Parse(value);

    public static implicit operator NetHttpPathQuery(Uri value) => FromUri(value);

    public override string ToString()
    {
        if (this.Query is null || this.Query.Count == 0)
            return this.Path;

        var writer = new ArrayBufferWriter<byte>(Math.Max(this.Path.Length + 16, 64));
        this.WriteTo(writer);

        return Encoding.UTF8.GetString(writer.WrittenSpan);
    }

    internal void WriteTo(IBufferWriter<byte> writer)
    {
        var path = this.Path;

        if (string.IsNullOrEmpty(path))
            WriteByte(writer, (byte)'/');
        else
            WriteUtf8(writer, path.AsSpan());

        if (this.Query is null || this.Query.Count == 0)
            return;

        WriteByte(writer, (byte)'?');

        for (var i = 0; i < this.Query.Count; i++)
        {
            if (i > 0)
                WriteByte(writer, (byte)'&');

            var param = this.Query[i];
            WriteEncoded(writer, param.Name.AsSpan());
            WriteByte(writer, (byte)'=');
            WriteEncoded(writer, param.Value.AsSpan());
        }
    }

    private static NetHttpPathQuery Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return new("/");

        var span = value.AsSpan();
        var queryIndex = span.IndexOf('?');
        var pathSpan = queryIndex >= 0 ? span[..queryIndex] : span;
        var querySpan = queryIndex >= 0 && queryIndex + 1 < span.Length ? span[(queryIndex + 1)..] : ReadOnlySpan<char>.Empty;
        var path = NormalizePath(new(pathSpan));
        var query = ParseQuery(querySpan);

        return new(path, query);
    }

    private static NetHttpPathQuery FromUri(Uri value)
    {
        value.Required();
        var path = NormalizePath(value.AbsolutePath);
        var query = ParseQuery(value.Query.AsSpan());

        return new(path, query);
    }

    private static NetHttpQueryParam[] ParseQuery(ReadOnlySpan<char> query)
    {
        if (query.IsEmpty)
            return Array.Empty<NetHttpQueryParam>();

        if (!query.IsEmpty && query[0] == '?')
            query = query[1..];

        if (query.IsEmpty)
            return Array.Empty<NetHttpQueryParam>();

        var list = new List<NetHttpQueryParam>();
        var offset = 0;

        while (offset <= query.Length)
        {
            var slice = query[offset..];
            var amp = slice.IndexOf('&');
            var length = amp >= 0 ? amp : slice.Length;

            if (length > 0)
            {
                var segment = slice[..length];
                var eq = segment.IndexOf('=');
                var nameSpan = eq >= 0 ? segment[..eq] : segment;
                var valueSpan = eq >= 0 ? segment[(eq + 1)..] : ReadOnlySpan<char>.Empty;
                var name = UrlDecode(nameSpan);
                var value = UrlDecode(valueSpan);
                list.Add(new(name, value));
            }

            if (amp < 0)
                break;

            offset += length + 1;
        }

        return list.Count == 0 ? Array.Empty<NetHttpQueryParam>() : list.ToArray();
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return "/";

        return path[0] == '/' ? path : $"/{path}";
    }

    private static string UrlDecode(ReadOnlySpan<char> value)
    {
        if (value.IsEmpty)
            return string.Empty;

        if (value.IndexOfAny('%', '+') < 0)
            return new(value);

        var chars = new char[value.Length];
        byte[]? bytes = null;
        var charIndex = 0;
        var byteIndex = 0;

        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];

            if (c == '+')
            {
                flushBytes();
                chars[charIndex++] = ' ';

                continue;
            }

            if (c == '%' && i + 2 < value.Length && TryParseHexByte(value.Slice(i + 1, 2), out var b))
            {
                bytes ??= ArrayPool<byte>.Shared.Rent(value.Length);
                bytes[byteIndex++] = b;
                i += 2;

                continue;
            }

            flushBytes();
            chars[charIndex++] = c;
        }

        flushBytes();

        if (bytes is not null)
            ArrayPool<byte>.Shared.Return(bytes);

        return new(chars, 0, charIndex);

        void flushBytes()
        {
            if (byteIndex == 0)
                return;

            var written = Encoding.UTF8.GetChars(bytes!, 0, byteIndex, chars, charIndex);
            charIndex += written;
            byteIndex = 0;
        }
    }

    private static bool TryParseHexByte(ReadOnlySpan<char> value, out byte result)
    {
        result = 0;

        if (value.Length != 2)
            return false;

        var hi = parseHex(value[0]);
        var lo = parseHex(value[1]);

        if (hi < 0 || lo < 0)
            return false;

        result = (byte)((hi << 4) | lo);

        return true;

        static int parseHex(char c) => c switch
        {
            >= '0' and <= '9' => c - '0',
            >= 'a' and <= 'f' => c - 'a' + 10,
            >= 'A' and <= 'F' => c - 'A' + 10,
            _ => -1,
        };
    }

    private static void WriteAscii(IBufferWriter<byte> writer, ReadOnlySpan<char> text)
    {
        var span = writer.GetSpan(text.Length);

        for (var i = 0; i < text.Length; i++)
            span[i] = text[i] <= 0x7F ? (byte)text[i] : (byte)'?';

        writer.Advance(text.Length);
    }

    private static void WriteUtf8(IBufferWriter<byte> writer, ReadOnlySpan<char> text)
    {
        var maxLength = Encoding.UTF8.GetMaxByteCount(text.Length);
        var span = writer.GetSpan(maxLength);
        var written = Encoding.UTF8.GetBytes(text, span);
        writer.Advance(written);
    }

    private static void WriteEncoded(IBufferWriter<byte> writer, ReadOnlySpan<char> text)
    {
        Span<byte> utf8 = stackalloc byte[4];

        foreach (var rune in text.EnumerateRunes())
        {
            if (rune.Value <= 0x7F && IsUnreserved((char)rune.Value))
            {
                WriteByte(writer, (byte)rune.Value);

                continue;
            }

            var length = rune.EncodeToUtf8(utf8);

            for (var i = 0; i < length; i++)
                WritePercentEncoded(writer, utf8[i]);
        }
    }

    private static void WriteByte(IBufferWriter<byte> writer, byte value)
    {
        var span = writer.GetSpan(1);
        span[0] = value;
        writer.Advance(1);
    }

    private static void WritePercentEncoded(IBufferWriter<byte> writer, byte value)
    {
        var span = writer.GetSpan(3);
        span[0] = (byte)'%';
        span[1] = ToHex(value >> 4);
        span[2] = ToHex(value & 0xF);
        writer.Advance(3);
    }

    private static byte ToHex(int value) => (byte)(value < 10 ? '0' + value : 'A' + (value - 10));

    private static bool IsUnreserved(char c) =>
        c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_' or '.' or '~';
}

public readonly record struct NetHttpQueryParam(string Name, string Value)
{
    public static implicit operator NetHttpQueryParam((string Name, string Value) value) => new(value.Name, value.Value);

    public static implicit operator NetHttpQueryParam(KeyValuePair<string, string> value) => new(value.Key, value.Value);
}
