// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Text;

namespace Itexoft.Net.Http.Internal;

internal static class NetHttpResponseParser
{
    public static void Parse(ReadOnlySpan<byte> headerBlock, out NetHttpVersion version, out NetHttpStatus status, out NetHttpHeaders headers)
    {
        headers = new();
        var span = headerBlock;

        if (!NetHttpParsing.TryReadLine(ref span, out var statusLine))
            throw new IOException("HTTP response is missing status line.");

        ParseStatusLine(statusLine, out version, out status);

        while (NetHttpParsing.TryReadLine(ref span, out var line))
        {
            if (line.Length == 0)
                break;

            var colon = line.IndexOf((byte)':');

            if (colon <= 0)
                continue;

            var name = NetHttpParsing.TrimOws(line[..colon]);
            var value = NetHttpParsing.TrimOws(line[(colon + 1)..]);

            if (name.IsEmpty)
                continue;

            headers.Add(Encoding.ASCII.GetString(name), Encoding.ASCII.GetString(value));
        }
    }

    private static void ParseStatusLine(ReadOnlySpan<byte> line, out NetHttpVersion version, out NetHttpStatus status)
    {
        version = NetHttpVersion.Version11;
        status = NetHttpStatus.Unknown;

        if (line.Length < 12)
            throw new IOException("HTTP status line is too short.");

        if (!StartsWith(line, "HTTP/".AsSpan()))
            throw new IOException("Invalid HTTP status line.");

        var index = 5;
        var major = 0;

        while (index < line.Length && IsDigit(line[index]))
        {
            major = major * 10 + (line[index] - (byte)'0');
            index++;
        }

        if (index >= line.Length || line[index] != (byte)'.')
            throw new IOException("Invalid HTTP version.");

        index++;
        var minor = 0;

        while (index < line.Length && IsDigit(line[index]))
        {
            minor = minor * 10 + (line[index] - (byte)'0');
            index++;
        }

        if (major == 1 && minor == 0)
            version = NetHttpVersion.Version10;
        else if (major == 1 && minor == 1)
            version = NetHttpVersion.Version11;

        while (index < line.Length && line[index] == (byte)' ')
            index++;

        if (index >= line.Length)
            throw new IOException("Missing HTTP status code.");

        var statusSlice = line[index..];
        var space = statusSlice.IndexOf((byte)' ');

        if (space >= 0)
            statusSlice = statusSlice[..space];

        if (!NetHttpParsing.TryParseInt32(statusSlice, out var statusCode))
            throw new IOException("Invalid HTTP status code.");

        status = (NetHttpStatus)statusCode;
    }

    private static bool StartsWith(ReadOnlySpan<byte> span, ReadOnlySpan<char> text)
    {
        if (span.Length < text.Length)
            return false;

        for (var i = 0; i < text.Length; i++)
        {
            if (span[i] != (byte)text[i])
                return false;
        }

        return true;
    }

    private static bool IsDigit(byte value) => value is >= (byte)'0' and <= (byte)'9';
}
