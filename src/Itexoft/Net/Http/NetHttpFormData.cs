// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Buffers;
using System.Collections;
using System.Globalization;
using System.Text;
using Itexoft.Extensions;
using Itexoft.IO;

namespace Itexoft.Net.Http;

public enum NetHttpFormEncoding
{
    FormUrlEncoded,
    QueryString,
}

public enum NetHttpFormCollectionMode
{
    Repeat,
    Brackets,
    Indexed,
}

public enum NetHttpFormEnumFormat
{
    Name,
    Numeric,
}

public readonly record struct NetHttpFormField(string Name, string Value)
{
    public static implicit operator NetHttpFormField((string Name, string Value) value) => new(value.Name, value.Value);
    public static implicit operator NetHttpFormField(KeyValuePair<string, string> value) => new(value.Key, value.Value);
}

public sealed class NetHttpFormData : IEnumerable<NetHttpFormField>
{
    public const string DefaultContentType = "application/x-www-form-urlencoded; charset=UTF-8";
    private readonly Encoding encoding = Encoding.UTF8;

    private readonly List<NetHttpFormField> fields;

    public NetHttpFormData() => this.fields = new();

    public NetHttpFormData(int capacity) => this.fields = capacity > 0 ? new(capacity) : new();

    public NetHttpFormData(IEnumerable<NetHttpFormField> fields)
    {
        this.fields = new();
        this.AddRange(fields);
    }

    public int Count => this.fields.Count;

    public IEnumerator<NetHttpFormField> GetEnumerator() => this.fields.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();

    public NetHttpFormData Add(NetHttpFormField field) => this.Add(field.Name, field.Value);

    public NetHttpFormData Add(string name, string? value)
    {
        name = name.RequiredNotWhiteSpace();
        this.fields.Add(new(name, value ?? string.Empty));

        return this;
    }

    public NetHttpFormData Add(string name, ReadOnlySpan<char> value) => this.Add(name, value.ToString());

    public NetHttpFormData Add(string name, bool value) => this.Add(name, value ? "true" : "false");

    public NetHttpFormData Add(string name, byte value) => this.Add(name, value.ToString(CultureInfo.InvariantCulture));
    public NetHttpFormData Add(string name, sbyte value) => this.Add(name, value.ToString(CultureInfo.InvariantCulture));
    public NetHttpFormData Add(string name, short value) => this.Add(name, value.ToString(CultureInfo.InvariantCulture));
    public NetHttpFormData Add(string name, ushort value) => this.Add(name, value.ToString(CultureInfo.InvariantCulture));
    public NetHttpFormData Add(string name, int value) => this.Add(name, value.ToString(CultureInfo.InvariantCulture));
    public NetHttpFormData Add(string name, uint value) => this.Add(name, value.ToString(CultureInfo.InvariantCulture));
    public NetHttpFormData Add(string name, long value) => this.Add(name, value.ToString(CultureInfo.InvariantCulture));
    public NetHttpFormData Add(string name, ulong value) => this.Add(name, value.ToString(CultureInfo.InvariantCulture));
    public NetHttpFormData Add(string name, float value) => this.Add(name, value.ToString(CultureInfo.InvariantCulture));
    public NetHttpFormData Add(string name, double value) => this.Add(name, value.ToString(CultureInfo.InvariantCulture));
    public NetHttpFormData Add(string name, decimal value) => this.Add(name, value.ToString(CultureInfo.InvariantCulture));

    public NetHttpFormData Add(string name, Guid value) => this.Add(name, value.ToString("D", CultureInfo.InvariantCulture));
    public NetHttpFormData Add(string name, TimeSpan value) => this.Add(name, value.ToString("c", CultureInfo.InvariantCulture));

    public NetHttpFormData Add(string name, DateTime value, string format = "O", IFormatProvider? provider = null) =>
        this.Add(name, value.ToString(format, provider ?? CultureInfo.InvariantCulture));

    public NetHttpFormData Add(string name, DateTimeOffset value, string format = "O", IFormatProvider? provider = null) =>
        this.Add(name, value.ToString(format, provider ?? CultureInfo.InvariantCulture));

    public NetHttpFormData Add(string name, DateOnly value, string format = "yyyy-MM-dd", IFormatProvider? provider = null) =>
        this.Add(name, value.ToString(format, provider ?? CultureInfo.InvariantCulture));

    public NetHttpFormData Add(string name, TimeOnly value, string format = "HH':'mm':'ss'.'fffffff", IFormatProvider? provider = null) =>
        this.Add(name, value.ToString(format, provider ?? CultureInfo.InvariantCulture));

    public NetHttpFormData AddEnum<TEnum>(string name, TEnum value, NetHttpFormEnumFormat format = NetHttpFormEnumFormat.Numeric)
        where TEnum : struct, Enum =>
        this.Add(name, Enum.Format(typeof(TEnum), value, format == NetHttpFormEnumFormat.Numeric ? "D" : "G"));

    public NetHttpFormData AddOptional(string name, string? value)
    {
        if (value is null)
            return this;

        return this.Add(name, value);
    }

    public NetHttpFormData AddOptional<T>(string name, T? value, string? format = null, IFormatProvider? provider = null)
        where T : struct, ISpanFormattable
    {
        if (!value.HasValue)
            return this;

        return this.Add(name, FormatInvariant(value.Value, format, provider));
    }

    public NetHttpFormData AddValues(string name, IEnumerable<string?> values, NetHttpFormCollectionMode mode = NetHttpFormCollectionMode.Repeat)
    {
        name = name.RequiredNotWhiteSpace();
        values = values.Required();

        var index = 0;
        var repeated = mode == NetHttpFormCollectionMode.Brackets ? $"{name}[]" : name;

        foreach (var value in values)
        {
            var key = mode == NetHttpFormCollectionMode.Indexed ? $"{name}[{index.ToString(CultureInfo.InvariantCulture)}]" : repeated;
            this.Add(key, value);
            index++;
        }

        return this;
    }

    public NetHttpFormData AddValues<T>(
        string name,
        IEnumerable<T> values,
        NetHttpFormCollectionMode mode = NetHttpFormCollectionMode.Repeat,
        string? format = null,
        IFormatProvider? provider = null) where T : ISpanFormattable
    {
        name = name.RequiredNotWhiteSpace();
        values = values.Required();

        var index = 0;
        var repeated = mode == NetHttpFormCollectionMode.Brackets ? $"{name}[]" : name;

        foreach (var value in values)
        {
            var key = mode == NetHttpFormCollectionMode.Indexed ? $"{name}[{index.ToString(CultureInfo.InvariantCulture)}]" : repeated;
            this.Add(key, FormatInvariant(value, format, provider));
            index++;
        }

        return this;
    }

    public NetHttpFormData AddObject(string name, NetHttpFormData value)
    {
        name = name.RequiredNotWhiteSpace();
        value = value.Required();

        foreach (var field in value.fields)
            this.fields.Add(new(string.Concat(name, "[", field.Name, "]"), field.Value));

        return this;
    }

    public NetHttpFormData AddRange(IEnumerable<NetHttpFormField> fields)
    {
        fields = fields.Required();

        foreach (var field in fields)
            this.Add(field);

        return this;
    }

    public string Serialize(NetHttpFormEncoding encoding = NetHttpFormEncoding.FormUrlEncoded)
    {
        if (this.fields.Count == 0)
            return string.Empty;

        var writer = new ArrayBufferWriter<byte>(EstimateLength(this.fields));
        this.WriteTo(writer, encoding);

        return this.encoding.GetString(writer.WrittenSpan);
    }

    public void WriteTo(IBufferWriter<byte> writer, NetHttpFormEncoding encoding = NetHttpFormEncoding.FormUrlEncoded)
    {
        writer = writer.Required();

        if (this.fields.Count == 0)
            return;

        var spaceAsPlus = encoding == NetHttpFormEncoding.FormUrlEncoded;

        for (var i = 0; i < this.fields.Count; i++)
        {
            if (i > 0)
                WriteByte(writer, (byte)'&');

            var field = this.fields[i];
            WriteEncoded(writer, field.Name.AsSpan(), spaceAsPlus);
            WriteByte(writer, (byte)'=');
            WriteEncoded(writer, field.Value.AsSpan(), spaceAsPlus);
        }
    }

    public override string ToString() => this.Serialize();
    public IStreamRal CreateAsyncStream() => new StringStream(this.Serialize());

    private static int EstimateLength(List<NetHttpFormField> fields)
    {
        var length = 0;

        for (var i = 0; i < fields.Count; i++)
        {
            var field = fields[i];
            length += field.Name.Length + field.Value.Length + 2;
        }

        if (fields.Count > 1)
            length += fields.Count - 1;

        return Math.Max(length, 16);
    }

    private static string FormatInvariant<T>(T value, string? format, IFormatProvider? provider) where T : ISpanFormattable
    {
        provider ??= CultureInfo.InvariantCulture;
        Span<char> buffer = stackalloc char[64];

        if (value.TryFormat(buffer, out var written, format, provider))
            return new(buffer[..written]);

        return value.ToString(format, provider) ?? string.Empty;
    }

    private static void WriteEncoded(IBufferWriter<byte> writer, ReadOnlySpan<char> text, bool spaceAsPlus)
    {
        Span<byte> utf8 = stackalloc byte[4];

        foreach (var rune in text.EnumerateRunes())
        {
            if (rune.Value <= 0x7F)
            {
                var c = (char)rune.Value;

                if (spaceAsPlus && c == ' ')
                {
                    WriteByte(writer, (byte)'+');

                    continue;
                }

                if (IsUnreserved(c))
                {
                    WriteByte(writer, (byte)c);

                    continue;
                }
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
