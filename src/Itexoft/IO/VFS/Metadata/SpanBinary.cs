// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Buffers.Binary;
using System.Text;

namespace Itexoft.IO.VFS.Metadata;

internal static class SpanBinary
{
    private static readonly Encoding Utf8 = new UTF8Encoding(false, true);

    internal static int GetStringSize(string value)
    {
        if (string.IsNullOrEmpty(value))
            return 0;

        return Utf8.GetByteCount(value);
    }

    internal ref struct Writer
    {
        private Span<byte> buffer;
        private int position;

        public Writer(Span<byte> buffer)
        {
            this.buffer = buffer;
            this.position = 0;
        }

        public int BytesWritten => this.position;

        public void WriteByte(byte value)
        {
            this.EnsureCapacity(1);
            this.buffer[this.position] = value;
            this.position += 1;
        }

        public void WriteInt32(int value)
        {
            this.EnsureCapacity(sizeof(int));
            BinaryPrimitives.WriteInt32LittleEndian(this.buffer.Slice(this.position, sizeof(int)), value);
            this.position += sizeof(int);
        }

        public void WriteInt64(long value)
        {
            this.EnsureCapacity(sizeof(long));
            BinaryPrimitives.WriteInt64LittleEndian(this.buffer.Slice(this.position, sizeof(long)), value);
            this.position += sizeof(long);
        }

        public void WriteUInt32(uint value)
        {
            this.EnsureCapacity(sizeof(uint));
            BinaryPrimitives.WriteUInt32LittleEndian(this.buffer.Slice(this.position, sizeof(uint)), value);
            this.position += sizeof(uint);
        }

        public void WriteBytes(ReadOnlySpan<byte> value)
        {
            if (value.Length == 0)
                return;

            this.EnsureCapacity(value.Length);
            value.CopyTo(this.buffer.Slice(this.position, value.Length));
            this.position += value.Length;
        }

        public void WriteString(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                this.WriteInt32(0);

                return;
            }

            var byteCount = Utf8.GetByteCount(value);
            this.WriteInt32(byteCount);
            this.EnsureCapacity(byteCount);
            Utf8.GetBytes(value, this.buffer.Slice(this.position, byteCount));
            this.position += byteCount;
        }

        private void EnsureCapacity(int additionalBytes)
        {
            if (this.position + additionalBytes > this.buffer.Length)
                throw new InvalidOperationException("SpanBinary.Writer buffer overflow.");
        }
    }

    internal ref struct Reader
    {
        private ReadOnlySpan<byte> buffer;
        private int position;

        public Reader(ReadOnlySpan<byte> buffer)
        {
            this.buffer = buffer;
            this.position = 0;
        }

        public bool EndOfData => this.position >= this.buffer.Length;

        public byte ReadByte()
        {
            this.EnsureAvailable(1);
            var value = this.buffer[this.position];
            this.position += 1;

            return value;
        }

        public int ReadInt32()
        {
            this.EnsureAvailable(sizeof(int));
            var value = BinaryPrimitives.ReadInt32LittleEndian(this.buffer.Slice(this.position, sizeof(int)));
            this.position += sizeof(int);

            return value;
        }

        public long ReadInt64()
        {
            this.EnsureAvailable(sizeof(long));
            var value = BinaryPrimitives.ReadInt64LittleEndian(this.buffer.Slice(this.position, sizeof(long)));
            this.position += sizeof(long);

            return value;
        }

        public uint ReadUInt32()
        {
            this.EnsureAvailable(sizeof(uint));
            var value = BinaryPrimitives.ReadUInt32LittleEndian(this.buffer.Slice(this.position, sizeof(uint)));
            this.position += sizeof(uint);

            return value;
        }

        public ReadOnlySpan<byte> ReadBytes(int length)
        {
            if (length < 0)
                throw new InvalidDataException("SpanBinary.Reader encountered negative length.");

            this.EnsureAvailable(length);
            var slice = this.buffer.Slice(this.position, length);
            this.position += length;

            return slice;
        }

        public string ReadString()
        {
            var length = this.ReadInt32();

            if (length < 0)
                throw new InvalidDataException("SpanBinary.Reader encountered negative string length.");

            if (length == 0)
                return string.Empty;

            var bytes = this.ReadBytes(length);

            return Utf8.GetString(bytes);
        }

        private void EnsureAvailable(int required)
        {
            if (this.position + required > this.buffer.Length)
                throw new InvalidDataException("SpanBinary.Reader buffer underflow.");
        }
    }
}