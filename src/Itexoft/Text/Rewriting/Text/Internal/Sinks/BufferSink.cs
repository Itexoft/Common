// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Buffers;
using Itexoft.Threading;

namespace Itexoft.Text.Rewriting.Text.Internal.Sinks;

internal sealed class BufferSink(ArrayPool<char> pool) : ITextSink
{
    private char[] buffer = [];
    private int start = 0;

    public int Available { get; private set; } = 0;

    public void Write(ReadOnlySpan<char> buffer)
    {
        if (buffer.Length == 0)
            return;

        this.EnsureCapacity(buffer.Length);
        buffer.CopyTo(this.buffer.AsSpan(this.start + this.Available));
        this.Available += buffer.Length;
    }

    public ValueTask WriteAsync(ReadOnlyMemory<char> buffer, CancelToken cancelToken)
    {
        this.Write(buffer.Span);

        return ValueTask.CompletedTask;
    }

    public int Drain(Span<char> destination)
    {
        var toCopy = Math.Min(destination.Length, this.Available);

        if (toCopy == 0)
            return 0;

        this.buffer.AsSpan(this.start, toCopy).CopyTo(destination);

        this.start += toCopy;
        this.Available -= toCopy;

        if (this.Available == 0)
            this.start = 0;

        return toCopy;
    }

    public char? Peek()
    {
        if (this.Available == 0)
            return null;

        return this.buffer[this.start];
    }

    public void Clear(bool clearArray)
    {
        if (this.buffer.Length != 0)
            pool.Return(this.buffer, clearArray);

        this.buffer = [];
        this.Available = 0;
        this.start = 0;
    }

    private void EnsureCapacity(int additional)
    {
        if (this.buffer.Length == 0)
        {
            var size = Math.Max(8, additional);
            this.buffer = pool.Rent(size);
            this.start = 0;
            this.Available = 0;

            return;
        }

        if (this.start + this.Available + additional <= this.buffer.Length)
            return;

        if (this.Available + additional <= this.buffer.Length)
        {
            this.buffer.AsSpan(this.start, this.Available).CopyTo(this.buffer.AsSpan());
            this.start = 0;

            return;
        }

        var required = this.Available + additional;
        var newSize = this.buffer.Length * 2;

        if (newSize < required)
            newSize = required;

        var newBuffer = pool.Rent(newSize);
        this.buffer.AsSpan(this.start, this.Available).CopyTo(newBuffer.AsSpan());

        pool.Return(this.buffer, false);

        this.buffer = newBuffer;
        this.start = 0;
    }
}
