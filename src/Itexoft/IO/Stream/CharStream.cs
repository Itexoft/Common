// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Buffers;
using System.Text;
using Itexoft.Extensions;

namespace Itexoft.IO;

public sealed class CharStreamRead : StreamBase<char>, IStreamR<char>
{
    private const int defaultBufferSize = 4096;
    private readonly Decoder decoder;

    private readonly IStreamR stream;
    private byte[] byteBuffer;
    private int byteCount;
    private int byteOffset;
    private char[] charBuffer;
    private int charCount;
    private int charOffset;
    private bool completed;

    public CharStreamRead(IStreamR stream, Encoding encoding)
    {
        this.stream = stream.Required();
        this.decoder = encoding.Required().GetDecoder();
        this.byteBuffer = ArrayPool<byte>.Shared.Rent(defaultBufferSize);
        var charBufferSize = Math.Max(encoding.GetMaxCharCount(this.byteBuffer.Length), 1);
        this.charBuffer = ArrayPool<char>.Shared.Rent(charBufferSize);
    }

    public CharStreamRead(IStreamR stream) : this(stream, Encoding.UTF8) { }

    public int Read(Span<char> destination)
    {
        if (destination.IsEmpty)
            return 0;

        var total = 0;

        if (this.charCount > 0)
        {
            var toCopy = Math.Min(destination.Length, this.charCount);
            this.charBuffer.AsSpan(this.charOffset, toCopy).CopyTo(destination);
            this.charOffset += toCopy;
            this.charCount -= toCopy;
            total += toCopy;

            if (total == destination.Length)
                return total;
        }

        while (total < destination.Length)
        {
            if (this.byteCount == this.byteOffset && !this.ReadMoreBytes())
            {
                total += this.FlushDecoder(destination[total..]);

                break;
            }

            var bytes = this.byteBuffer.AsSpan(this.byteOffset, this.byteCount - this.byteOffset);
            var target = destination[total..];
            this.decoder.Convert(bytes, target, false, out var bytesUsed, out var charsUsed, out _);
            this.byteOffset += bytesUsed;

            if (this.byteOffset == this.byteCount)
            {
                this.byteOffset = 0;
                this.byteCount = 0;
            }

            total += charsUsed;

            if (charsUsed == 0)
            {
                if (!this.ReadMoreBytes())
                {
                    total += this.FlushDecoder(destination[total..]);

                    break;
                }
            }
        }

        return total;
    }

    protected override ValueTask DisposeAny()
    {
        if (this.byteBuffer.Length != 0)
        {
            ArrayPool<byte>.Shared.Return(this.byteBuffer);
            this.byteBuffer = [];
        }

        if (this.charBuffer.Length != 0)
        {
            ArrayPool<char>.Shared.Return(this.charBuffer);
            this.charBuffer = [];
        }

        return ValueTask.CompletedTask;
    }

    public string? ReadLine()
    {
        char[]? rented = null;
        var length = 0;

        try
        {
            while (true)
            {
                if (this.charCount == 0 && !this.FillCharBuffer())
                    return length == 0 ? null : new string(rented!, 0, length);

                var span = this.charBuffer.AsSpan(this.charOffset, this.charCount);
                var idx = span.IndexOfAny('\r', '\n');

                if (idx >= 0)
                {
                    if (idx > 0)
                    {
                        if (length == 0 && rented is null)
                        {
                            var line = new string(span[..idx]);
                            this.ConsumeLineBreak(span, idx);

                            return line;
                        }

                        ensureCapacity(length + idx);
                        span[..idx].CopyTo(rented.AsSpan(length));
                        length += idx;
                    }
                    else if (length == 0 && rented is null)
                    {
                        this.ConsumeLineBreak(span, idx);

                        return string.Empty;
                    }

                    this.ConsumeLineBreak(span, idx);

                    return new string(rented!, 0, length);
                }

                if (!span.IsEmpty)
                {
                    ensureCapacity(length + span.Length);
                    span.CopyTo(rented.AsSpan(length));
                    length += span.Length;
                    this.charOffset += span.Length;
                    this.charCount -= span.Length;
                }
            }
        }
        finally
        {
            if (rented is not null)
                ArrayPool<char>.Shared.Return(rented);
        }

        void ensureCapacity(int required)
        {
            if (rented is null)
            {
                var size = Math.Max(required, this.charBuffer.Length);
                rented = ArrayPool<char>.Shared.Rent(size);

                return;
            }

            if (rented.Length >= required)
                return;

            var newSize = Math.Max(required, rented.Length * 2);
            var newBuffer = ArrayPool<char>.Shared.Rent(newSize);
            rented.AsSpan(0, length).CopyTo(newBuffer);
            ArrayPool<char>.Shared.Return(rented);
            rented = newBuffer;
        }
    }

    private bool FillCharBuffer()
    {
        if (this.charCount > 0)
            return true;

        while (true)
        {
            var available = this.byteCount - this.byteOffset;

            if (available == 0)
            {
                if (!this.ReadMoreBytes())
                {
                    var flushed = this.FlushDecoder(this.charBuffer);

                    if (flushed > 0)
                    {
                        this.charOffset = 0;
                        this.charCount = flushed;

                        return true;
                    }

                    return false;
                }

                available = this.byteCount - this.byteOffset;
            }

            var bytes = this.byteBuffer.AsSpan(this.byteOffset, available);
            var destination = this.charBuffer.AsSpan();
            this.decoder.Convert(bytes, destination, false, out var bytesUsed, out var charsUsed, out _);
            this.byteOffset += bytesUsed;

            if (this.byteOffset == this.byteCount)
            {
                this.byteOffset = 0;
                this.byteCount = 0;
            }

            if (charsUsed > 0)
            {
                this.charOffset = 0;
                this.charCount = charsUsed;

                return true;
            }

            if (bytesUsed == 0)
            {
                if (!this.ReadMoreBytes())
                {
                    var flushed = this.FlushDecoder(this.charBuffer);

                    if (flushed > 0)
                    {
                        this.charOffset = 0;
                        this.charCount = flushed;

                        return true;
                    }

                    return false;
                }
            }
        }
    }

    private bool ReadMoreBytes()
    {
        if (this.completed)
            return false;

        if (this.byteOffset > 0 && this.byteOffset < this.byteCount)
        {
            var remaining = this.byteCount - this.byteOffset;
            Buffer.BlockCopy(this.byteBuffer, this.byteOffset, this.byteBuffer, 0, remaining);
            this.byteOffset = 0;
            this.byteCount = remaining;
        }
        else if (this.byteOffset == this.byteCount)
        {
            this.byteOffset = 0;
            this.byteCount = 0;
        }

        if (this.byteCount == this.byteBuffer.Length)
        {
            var newBuffer = ArrayPool<byte>.Shared.Rent(this.byteBuffer.Length * 2);
            Buffer.BlockCopy(this.byteBuffer, 0, newBuffer, 0, this.byteCount);
            ArrayPool<byte>.Shared.Return(this.byteBuffer);
            this.byteBuffer = newBuffer;
        }

        var read = this.stream.Read(this.byteBuffer.AsSpan(this.byteCount));

        if (read == 0)
        {
            this.completed = true;

            return false;
        }

        this.byteCount += read;

        return true;
    }

    private int FlushDecoder(Span<char> destination)
    {
        if (!this.completed)
            return 0;

        this.decoder.Convert(ReadOnlySpan<byte>.Empty, destination, true, out _, out var charsUsed, out _);

        return charsUsed;
    }

    private void ConsumeLineBreak(ReadOnlySpan<char> span, int index)
    {
        if (span[index] == '\r')
        {
            if (index + 1 < span.Length)
            {
                var advance = span[index + 1] == '\n' ? 2 : 1;
                this.charOffset += index + advance;
                this.charCount -= index + advance;

                return;
            }

            this.charOffset += index + 1;
            this.charCount -= index + 1;

            if (this.charCount == 0 && this.FillCharBuffer())
            {
                if (this.charBuffer[this.charOffset] == '\n')
                {
                    this.charOffset++;
                    this.charCount--;
                }
            }

            return;
        }

        this.charOffset += index + 1;
        this.charCount -= index + 1;
    }
}

public sealed class CharStreamWrite(IStreamW stream, Encoding encoding) : StreamBase<char>, IStreamW<char>
{
    private const int defaultBufferSize = 4096;
    private static readonly char[] newLine = Environment.NewLine.ToCharArray();
    private readonly Encoder encoder = encoding.Required().GetEncoder();

    private readonly IStreamW stream = stream.Required();
    private byte[] buffer = ArrayPool<byte>.Shared.Rent(defaultBufferSize);
    private int bufferOffset;
    private bool hasPendingChar;
    private char pendingChar;

    public CharStreamWrite(IStreamW stream) : this(stream, Encoding.UTF8) { }

    public void Flush()
    {
        this.FlushEncoder();
        this.stream.Flush();
    }

    public void Write(ReadOnlySpan<char> source)
    {
        if (source.IsEmpty)
            return;

        var span = source;

        if (this.hasPendingChar)
        {
            Span<char> prefix = stackalloc char[2];
            prefix[0] = this.pendingChar;
            prefix[1] = span[0];

            this.pendingChar = '\0';
            this.hasPendingChar = false;

            this.WriteCore(prefix);
            span = span[1..];

            if (span.IsEmpty)
                return;
        }

        this.WriteCore(span);
    }

    protected override ValueTask DisposeAny()
    {
        this.FlushEncoder();

        if (this.buffer.Length != 0)
        {
            ArrayPool<byte>.Shared.Return(this.buffer);
            this.buffer = [];
        }

        this.bufferOffset = 0;
        this.pendingChar = '\0';
        this.hasPendingChar = false;

        return ValueTask.CompletedTask;
    }

    public void Write(string value) => this.Write(value.AsSpan());

    public void WriteLine() => this.Write(newLine);

    public void WriteLine(string value)
    {
        this.Write(value.AsSpan());
        this.Write(newLine.AsSpan());
    }

    public void WriteLine(ReadOnlySpan<char> value)
    {
        this.Write(value);
        this.Write(newLine.AsSpan());
    }

    private void WriteCore(ReadOnlySpan<char> source)
    {
        var span = source;

        while (!span.IsEmpty)
        {
            if (this.bufferOffset == this.buffer.Length)
                this.FlushBuffer();

            var target = this.buffer.AsSpan(this.bufferOffset);
            this.encoder.Convert(span, target, false, out var charsUsed, out var bytesUsed, out _);

            if (charsUsed == 0 && bytesUsed == 0)
            {
                this.pendingChar = span[0];
                this.hasPendingChar = true;

                return;
            }

            this.bufferOffset += bytesUsed;
            span = span[charsUsed..];

            if (this.bufferOffset == this.buffer.Length)
                this.FlushBuffer();
        }
    }

    private void FlushEncoder()
    {
        if (this.hasPendingChar)
        {
            Span<char> tmp = stackalloc char[1];
            tmp[0] = this.pendingChar;

            this.pendingChar = '\0';
            this.hasPendingChar = false;

            while (true)
            {
                if (this.bufferOffset == this.buffer.Length)
                    this.FlushBuffer();

                var target = this.buffer.AsSpan(this.bufferOffset);
                this.encoder.Convert(tmp, target, true, out var charsUsed, out var bytesUsed, out _);

                this.bufferOffset += bytesUsed;
                tmp = tmp[charsUsed..];

                if (tmp.IsEmpty)
                    break;

                if (charsUsed == 0 && bytesUsed == 0)
                    this.FlushBuffer();
            }
        }

        while (true)
        {
            if (this.bufferOffset == this.buffer.Length)
                this.FlushBuffer();

            var target = this.buffer.AsSpan(this.bufferOffset);
            this.encoder.Convert(ReadOnlySpan<char>.Empty, target, true, out _, out var bytesUsed, out var completed);

            this.bufferOffset += bytesUsed;

            if (this.bufferOffset == this.buffer.Length)
                this.FlushBuffer();

            if (completed)
                break;

            if (bytesUsed == 0)
                this.FlushBuffer();
        }

        this.FlushBuffer();
    }

    private void FlushBuffer()
    {
        if (this.bufferOffset == 0)
            return;

        this.stream.Write(this.buffer.AsSpan(0, this.bufferOffset));
        this.bufferOffset = 0;
    }
}
