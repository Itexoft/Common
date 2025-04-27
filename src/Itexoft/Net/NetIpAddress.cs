// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Itexoft.Net;

/// <summary>
/// Allocation-free IP address value with inlined bytes (no array fields).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly struct NetIpAddress : ISpanFormattable, ISpanParsable<NetIpAddress>, IUtf8SpanFormattable, IUtf8SpanParsable<NetIpAddress>
{
    private readonly ulong block0;
    private readonly ulong block1;
    private readonly uint scopeId;

    public NetIpAddress()
    {
        this.block0 = 0;
        this.block1 = 0;
        this.scopeId = 0;
    }

    public NetIpAddress(ReadOnlySpan<byte> address) : this(address, 0) { }

    public NetIpAddress(ReadOnlySpan<byte> address, uint scopeId)
    {
        switch (address.Length)
        {
            case 4:
                this.AddressFamily = AddressFamily.InterNetwork;
                this.block0 = PackIPv4(address);
                this.block1 = 0;
                this.scopeId = 0;

                return;
            case 16:
                this.AddressFamily = AddressFamily.InterNetworkV6;
                this.block0 = Unsafe.ReadUnaligned<ulong>(ref MemoryMarshal.GetReference(address));
                this.block1 = Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref MemoryMarshal.GetReference(address), 8));
                this.scopeId = scopeId;

                return;
            default:
                this.AddressFamily = AddressFamily.Unspecified;
                this.block0 = 0;
                this.block1 = 0;
                this.scopeId = 0;

                return;
        }
    }

    public NetIpAddress(byte a, byte b, byte c, byte d)
    {
        this.AddressFamily = AddressFamily.InterNetwork;
        this.block0 = PackIPv4(a, b, c, d);
        this.block1 = 0;
        this.scopeId = 0;
    }

    public NetIpAddress(ushort s0, ushort s1, ushort s2, ushort s3, ushort s4, ushort s5, ushort s6, ushort s7, uint scopeId = 0)
    {
        this.AddressFamily = AddressFamily.InterNetworkV6;
        this.block0 = PackIpv6Block(s0, s1, s2, s3);
        this.block1 = PackIpv6Block(s4, s5, s6, s7);
        this.scopeId = scopeId;
    }

    public NetIpAddress(AddressFamily addressFamily, ulong block0, ulong block1, uint scopeId)
    {
        this.AddressFamily = addressFamily;
        this.block0 = block0;
        this.block1 = block1;
        this.scopeId = scopeId;
    }

    public AddressFamily AddressFamily { get; }

    public bool IsIPv4 => this.AddressFamily == AddressFamily.InterNetwork;

    public bool IsIPv6 => this.AddressFamily == AddressFamily.InterNetworkV6;

    public bool IsAny => this.block0 == 0 && this.block1 == 0 && this.scopeId == 0;

    public static NetIpAddress Loopback { get; } = IPAddress.Loopback;
    public static NetIpAddress IpV6Loopback { get; } = IPAddress.IPv6Loopback;
    public static NetIpAddress Broadcast { get; } = IPAddress.Broadcast;

    public static NetIpAddress Parse(string s) => TryParse(s, out var address) ? address : throw new FormatException("Invalid IP address.");

    public static NetIpAddress Parse(ReadOnlySpan<char> s) =>
        TryParse(s, out var address) ? address : throw new FormatException("Invalid IP address.");

    public static implicit operator NetIpAddress(IPAddress address) => new(address.GetAddressBytes());

    public static bool TryParse(ReadOnlySpan<char> s, out NetIpAddress result)
    {
        if (s.IsEmpty)
        {
            result = default;

            return false;
        }

        var scopeSeparator = s.IndexOf('%');
        uint scopeId = 0;

        if (scopeSeparator >= 0)
        {
            if (scopeSeparator == s.Length - 1 || !TryParseScope(s[(scopeSeparator + 1)..], out scopeId))
            {
                result = default;

                return false;
            }

            s = s[..scopeSeparator];
        }

        if (s.IndexOf(':') >= 0)
            return TryParseIpv6(s, scopeId, out result);

        if (scopeSeparator >= 0)
        {
            result = default;

            return false;
        }

        return TryParseIpv4(s, out result);
    }

    public static bool TryParse(string? s, out NetIpAddress result) =>
        TryParse(s.AsSpan(), out result);

    public static NetIpAddress Parse(ReadOnlySpan<byte> utf8) =>
        TryParse(utf8, out var address) ? address : throw new FormatException("Invalid IP address.");

    public static bool TryParse(ReadOnlySpan<byte> utf8, out NetIpAddress result)
    {
        if (utf8.IsEmpty)
        {
            result = default;

            return false;
        }

        const int stackLimit = 128;
        var bufferLength = utf8.Length <= stackLimit ? stackLimit : utf8.Length;
        var chars = bufferLength <= stackLimit ? stackalloc char[stackLimit] : new char[bufferLength];

        var written = 0;

        foreach (var b in utf8)
        {
            if (b > 0x7F)
            {
                result = default;

                return false;
            }

            chars[written++] = (char)b;
        }

        return TryParse(chars[..written], out result);
    }

    public int WriteBytes(Span<byte> destination)
    {
        switch (this.AddressFamily)
        {
            case AddressFamily.InterNetwork when destination.Length < 4:
                return 0;
            case AddressFamily.InterNetwork:
                Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(destination), (uint)this.block0);

                return 4;
            case AddressFamily.InterNetworkV6 when destination.Length < 16:
                return 0;
            case AddressFamily.InterNetworkV6:
                Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(destination), this.block0);
                Unsafe.WriteUnaligned(ref Unsafe.Add(ref MemoryMarshal.GetReference(destination), 8), this.block1);

                return 16;
            case AddressFamily.Unknown:
            case AddressFamily.Unspecified:
            case AddressFamily.Unix:
            case AddressFamily.ImpLink:
            case AddressFamily.Pup:
            case AddressFamily.Chaos:
            case AddressFamily.Ipx:
            case AddressFamily.Iso:
            case AddressFamily.Ecma:
            case AddressFamily.DataKit:
            case AddressFamily.Ccitt:
            case AddressFamily.Sna:
            case AddressFamily.DecNet:
            case AddressFamily.DataLink:
            case AddressFamily.Lat:
            case AddressFamily.HyperChannel:
            case AddressFamily.AppleTalk:
            case AddressFamily.NetBios:
            case AddressFamily.VoiceView:
            case AddressFamily.FireFox:
            case AddressFamily.Banyan:
            case AddressFamily.Atm:
            case AddressFamily.Cluster:
            case AddressFamily.Ieee12844:
            case AddressFamily.Irda:
            case AddressFamily.NetworkDesigners:
            case AddressFamily.Max:
            case AddressFamily.Packet:
            case AddressFamily.ControllerAreaNetwork:
            default:
                return 0;
        }
    }

    public string ToString(string? format)
    {
        Span<char> buffer = stackalloc char[80];

        if (!this.TryFormat(buffer, out var written, format))
            return string.Empty;

        return new(buffer[..written]);
    }

    bool ISpanFormattable.TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider) =>
        this.TryFormat(destination, out charsWritten, format);

    public bool TryFormat(Span<byte> utf8Destination, out int bytesWritten, ReadOnlySpan<char> format, IFormatProvider? provider) =>
        this.TryFormatUtf8(utf8Destination, out bytesWritten, format);

    static NetIpAddress IParsable<NetIpAddress>.Parse(string s, IFormatProvider? provider) => Parse(s);

    static bool IParsable<NetIpAddress>.TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out NetIpAddress result) =>
        TryParse(s, out result);

    static NetIpAddress ISpanParsable<NetIpAddress>.Parse(ReadOnlySpan<char> s, IFormatProvider? provider) => Parse(s);

    static bool ISpanParsable<NetIpAddress>.TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out NetIpAddress result) =>
        TryParse(s, out result);

    static NetIpAddress IUtf8SpanParsable<NetIpAddress>.Parse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider) => Parse(utf8Text);

    static bool IUtf8SpanParsable<NetIpAddress>.TryParse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider, out NetIpAddress result) =>
        TryParse(utf8Text, out result);

    private static bool TryParseIpv4(ReadOnlySpan<char> text, out NetIpAddress address)
    {
        var octet = 0;
        var octetCount = 0;
        byte b0 = 0, b1 = 0, b2 = 0, b3 = 0;
        var hasDigit = false;

        foreach (var c in text)
        {
            if ((uint)(c - '0') <= 9)
            {
                octet = octet * 10 + (c - '0');

                if (octet > byte.MaxValue)
                {
                    address = default;

                    return false;
                }

                hasDigit = true;

                continue;
            }

            if (c != '.' || !hasDigit)
            {
                address = default;

                return false;
            }

            switch (octetCount)
            {
                case 0:
                    b0 = (byte)octet;

                    break;
                case 1:
                    b1 = (byte)octet;

                    break;
                case 2:
                    b2 = (byte)octet;

                    break;
            }

            octetCount++;

            if (octetCount > 3)
            {
                address = default;

                return false;
            }

            octet = 0;
            hasDigit = false;
        }

        if (octetCount != 3 || !hasDigit)
        {
            address = default;

            return false;
        }

        b3 = (byte)octet;
        address = new(AddressFamily.InterNetwork, PackIPv4(b0, b1, b2, b3), 0, 0);

        return true;
    }

    private static bool TryParseIpv6(ReadOnlySpan<char> text, uint scopeId, out NetIpAddress address)
    {
        Span<ushort> segments = stackalloc ushort[8];
        var segmentCount = 0;
        var compressAt = -1;
        var i = 0;

        address = default;

        if (text.Length == 0)
            return false;

        while (i < text.Length)
        {
            if (text[i] == ':')
            {
                if (i + 1 < text.Length && text[i + 1] == ':')
                {
                    if (compressAt >= 0)
                        return false;

                    compressAt = segmentCount;
                    i += 2;

                    if (i >= text.Length)
                        break;

                    continue;
                }

                if (i > 0 && text[i - 1] == ':')
                    return false;

                if (i == 0)
                    return false;

                i++;

                continue;
            }

            if (segmentCount >= segments.Length)
                return false;

            var segmentStart = i;
            var value = 0;
            var digits = 0;

            while (i < text.Length)
            {
                var hex = HexValue(text[i]);

                if (hex >= 0)
                {
                    value = (value << 4) + hex;

                    if (value > ushort.MaxValue)
                        return false;

                    digits++;
                    i++;

                    continue;
                }

                break;
            }

            if (digits == 0)
                return false;

            if (i < text.Length && text[i] == '.')
            {
                if (segmentCount > 6)
                    return false;

                if (!TryParseEmbeddedIpv4(text[segmentStart..], out var ipv4Upper, out var ipv4Lower))
                    return false;

                segments[segmentCount++] = ipv4Upper;
                segments[segmentCount++] = ipv4Lower;
                i = text.Length;

                break;
            }

            segments[segmentCount++] = (ushort)value;

            if (i < text.Length && text[i] != ':')
                return false;
        }

        if (compressAt >= 0)
        {
            var zerosToInsert = segments.Length - segmentCount;

            if (zerosToInsert < 1)
                return false;

            for (var idx = segmentCount - 1; idx >= compressAt; idx--)
                segments[idx + zerosToInsert] = segments[idx];

            for (var idx = 0; idx < zerosToInsert; idx++)
                segments[compressAt + idx] = 0;

            segmentCount = segments.Length;
        }

        if (segmentCount != segments.Length)
            return false;

        Span<byte> bytes = stackalloc byte[16];

        for (var idx = 0; idx < segments.Length; idx++)
            BinaryPrimitives.WriteUInt16BigEndian(bytes[(idx * 2)..], segments[idx]);

        var b0 = Unsafe.ReadUnaligned<ulong>(ref MemoryMarshal.GetReference(bytes));
        var b1 = Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref MemoryMarshal.GetReference(bytes), 8));

        address = new(AddressFamily.InterNetworkV6, b0, b1, scopeId);

        return true;
    }

    private static bool TryParseScope(ReadOnlySpan<char> scopeText, out uint scopeId)
    {
        if (scopeText.IsEmpty)
        {
            scopeId = 0;

            return false;
        }

        uint value = 0;

        foreach (var c in scopeText)
        {
            if ((uint)(c - '0') > 9)
            {
                scopeId = 0;

                return false;
            }

            value = checked(value * 10 + (uint)(c - '0'));
        }

        scopeId = value;

        return true;
    }

    private static bool TryParseEmbeddedIpv4(ReadOnlySpan<char> text, out ushort upper, out ushort lower)
    {
        if (!TryParseIpv4(text, out var ipv4))
        {
            upper = lower = 0;

            return false;
        }

        var value = (uint)ipv4.block0;
        var b0 = (byte)value;
        var b1 = (byte)(value >> 8);
        var b2 = (byte)(value >> 16);
        var b3 = (byte)(value >> 24);
        upper = (ushort)((b0 << 8) | b1);
        lower = (ushort)((b2 << 8) | b3);

        return true;
    }

    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format)
    {
        charsWritten = 0;

        if (!format.IsEmpty && !(format.Length == 1 && format[0] == 'G'))
            return false;

        return this.AddressFamily switch
        {
            AddressFamily.InterNetwork => this.TryFormatIpv4(destination, out charsWritten),
            AddressFamily.InterNetworkV6 => this.TryFormatIpv6(destination, out charsWritten),
            _ => false,
        };
    }

    public override string ToString() => this.ToString(null);

    private bool TryFormatIpv4(Span<char> destination, out int charsWritten)
    {
        var value = (uint)this.block0;
        var b0 = (byte)value;
        var b1 = (byte)(value >> 8);
        var b2 = (byte)(value >> 16);
        var b3 = (byte)(value >> 24);
        var required = DecimalDigitCount(b0) + DecimalDigitCount(b1) + DecimalDigitCount(b2) + DecimalDigitCount(b3) + 3;

        if (destination.Length < required)
        {
            charsWritten = 0;

            return false;
        }

        charsWritten = 0;
        var written = 0;
        written += WriteDecimal(b0, destination[written..]);
        destination[written++] = '.';
        written += WriteDecimal(b1, destination[written..]);
        destination[written++] = '.';
        written += WriteDecimal(b2, destination[written..]);
        destination[written++] = '.';
        written += WriteDecimal(b3, destination[written..]);

        charsWritten = written;

        return true;
    }

    private bool TryFormatIpv6(Span<char> destination, out int charsWritten)
    {
        charsWritten = 0;
        Span<ushort> segments = stackalloc ushort[8];
        this.GetSegments(segments);

        var (compressStart, compressLength) = FindLongestZeroRun(segments);

        var pos = 0;

        for (var i = 0; i < segments.Length; i++)
        {
            if (compressLength > 0 && i == compressStart)
            {
                if (pos + 2 > destination.Length)
                    return false;

                destination[pos++] = ':';
                destination[pos++] = ':';
                i += compressLength - 1;

                continue;
            }

            if (pos > 0 && destination[pos - 1] != ':')
            {
                if (pos >= destination.Length)
                    return false;

                destination[pos++] = ':';
            }

            var hexDigits = HexDigitCount(segments[i]);

            if (pos + hexDigits > destination.Length)
                return false;

            pos += WriteHex(segments[i], destination[pos..]);
        }

        if (this.scopeId != 0)
        {
            var scopeDigits = DecimalDigitCount(this.scopeId);

            if (pos + 1 + scopeDigits > destination.Length)
                return false;

            destination[pos++] = '%';
            pos += WriteDecimal(this.scopeId, destination[pos..]);
        }

        charsWritten = pos;

        return true;
    }

    private bool TryFormatUtf8(Span<byte> destination, out int bytesWritten, ReadOnlySpan<char> format)
    {
        bytesWritten = 0;
        Span<char> chars = stackalloc char[80];

        if (!this.TryFormat(chars, out var written, format))
            return false;

        if (destination.Length < written)
            return false;

        for (var i = 0; i < written; i++)
            destination[i] = (byte)chars[i];

        bytesWritten = written;

        return true;
    }

    private void GetSegments(Span<ushort> segments)
    {
        Span<byte> bytes = stackalloc byte[16];
        Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(bytes), this.block0);
        Unsafe.WriteUnaligned(ref Unsafe.Add(ref MemoryMarshal.GetReference(bytes), 8), this.block1);

        for (var i = 0; i < segments.Length; i++)
            segments[i] = BinaryPrimitives.ReadUInt16BigEndian(bytes[(i * 2)..]);
    }

    private static (int Start, int Length) FindLongestZeroRun(ReadOnlySpan<ushort> segments)
    {
        var bestStart = -1;
        var bestLength = 0;
        var currentStart = -1;
        var currentLength = 0;

        for (var i = 0; i < segments.Length; i++)
        {
            if (segments[i] == 0)
            {
                if (currentLength == 0)
                    currentStart = i;

                currentLength++;

                if (currentLength > bestLength)
                {
                    bestStart = currentStart;
                    bestLength = currentLength;
                }
            }
            else
                currentLength = 0;
        }

        return bestLength >= 2 ? (bestStart, bestLength) : (-1, 0);
    }

    private static int WriteDecimal(uint value, Span<char> destination)
    {
        Span<char> tmp = stackalloc char[10];
        var pos = 0;

        do
        {
            tmp[pos++] = (char)('0' + value % 10);
            value /= 10;
        }
        while (value != 0);

        for (var i = 0; i < pos; i++)
            destination[i] = tmp[pos - 1 - i];

        return pos;
    }

    private static int WriteDecimal(byte value, Span<char> destination) => WriteDecimal((uint)value, destination);

    private static int WriteHex(ushort value, Span<char> destination)
    {
        const string hex = "0123456789abcdef";
        var digits = HexDigitCount(value);

        for (var i = digits - 1; i >= 0; i--)
        {
            destination[i] = hex[value & 0xF];
            value >>= 4;
        }

        return digits;
    }

    private static int DecimalDigitCount(uint scopeId) => scopeId switch
    {
        < 10 => 1,
        < 100 => 2,
        < 1000 => 3,
        < 10000 => 4,
        < 100000 => 5,
        < 1000000 => 6,
        < 10000000 => 7,
        < 100000000 => 8,
        < 1000000000 => 9,
        _ => 10,
    };

    private static int DecimalDigitCount(byte value) => DecimalDigitCount((uint)value);

    private static int HexDigitCount(ushort value) => value switch
    {
        < 0x10 => 1,
        < 0x100 => 2,
        < 0x1000 => 3,
        _ => 4,
    };

    private static int HexValue(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        >= 'A' and <= 'F' => c - 'A' + 10,
        _ => -1,
    };

    private static ulong PackIPv4(ReadOnlySpan<byte> bytes) =>
        (uint)(bytes[0] | (bytes[1] << 8) | (bytes[2] << 16) | (bytes[3] << 24));

    private static ulong PackIPv4(byte b0, byte b1, byte b2, byte b3) =>
        (uint)(b0 | ((uint)b1 << 8) | ((uint)b2 << 16) | ((uint)b3 << 24));

    private static ulong PackIpv6Block(ushort s0, ushort s1, ushort s2, ushort s3) =>
        ((ulong)s0 << 48) | ((ulong)s1 << 32) | ((ulong)s2 << 16) | s3;

    string IFormattable.ToString(string? format, IFormatProvider? formatProvider) => this.ToString(format);

    public static implicit operator IPAddress(NetIpAddress ipAddress)
    {
        if (ipAddress.AddressFamily == AddressFamily.InterNetwork
            || (ipAddress.AddressFamily != AddressFamily.InterNetworkV6 && ipAddress.block1 == 0 && ipAddress.scopeId == 0))
        {
            Span<byte> bytes = stackalloc byte[4];
            Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(bytes), (uint)ipAddress.block0);

            return new(bytes);
        }

        Span<byte> v6 = stackalloc byte[16];
        Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(v6), ipAddress.block0);
        Unsafe.WriteUnaligned(ref Unsafe.Add(ref MemoryMarshal.GetReference(v6), 8), ipAddress.block1);

        return new(v6, ipAddress.scopeId);
    }

    public byte[] GetBytes()
    {
        if (this.AddressFamily == AddressFamily.InterNetwork
            || (this.AddressFamily != AddressFamily.InterNetworkV6 && this.block1 == 0 && this.scopeId == 0))
        {
            Span<byte> bytes = stackalloc byte[4];
            Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(bytes), (uint)this.block0);

            return bytes.ToArray();
        }
        else
        {
            Span<byte> bytes = stackalloc byte[16];
            Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(bytes), this.block0);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref MemoryMarshal.GetReference(bytes), 8), this.block1);

            return bytes.ToArray();
        }
    }
}
