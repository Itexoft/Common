// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Text;
using System.Threading.Channels;
using Itexoft.Core;
using Itexoft.Threading;

namespace Itexoft.Text;

public class TextChannel : IDisposable
{
    private readonly Channel<char> channel;
    private Disposed disposed;

    public TextChannel(int capacity = 512)
    {
        this.channel = Channel.CreateBounded<char>(
            new BoundedChannelOptions(capacity) { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });

        this.Reader = new BufferedTextReader(this.channel);
        this.Writer = new BufferedTextWriter(this.channel);
    }

    public bool IsCompleted => this.channel.Reader.Completion.IsCompleted;

    public TextReader Reader { get; }
    public TextWriter Writer { get; }

    public void Dispose()
    {
        if (this.disposed.Enter())
            return;

        this.channel.Writer.TryComplete();
        this.Reader.Dispose();
        this.Writer.Dispose();
        GC.SuppressFinalize(this);
    }

    public void Complete() => this.channel.Writer.TryComplete();

    private sealed class BufferedTextWriter(Channel<char> channel) : TextWriter
    {
        private readonly ChannelWriter<char> writer = channel.Writer;

        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char value) => this.WriteChar(value);

        public override void Write(ReadOnlySpan<char> buffer)
        {
            foreach (var ch in buffer)
                this.WriteChar(ch);
        }

        public override void Write(char[] buffer, int index, int count) => this.Write(buffer.AsSpan(index, count));

        public override void Write(string? value)
        {
            if (value is null)
                return;

            this.Write(value.AsSpan());
        }

        public override Task WriteAsync(char value) => this.writer.WriteAsync(value).AsTask();

        public override Task WriteAsync(char[] buffer, int index, int count) => this.WriteAsync(buffer.AsMemory(index, count));

        public override Task WriteAsync(string? value) => value is null ? Task.CompletedTask : this.WriteAsync(value.AsMemory());

        public override void Flush() { }

        public override Task FlushAsync() => Task.CompletedTask;

        private void WriteChar(char value)
        {
            if (this.writer.TryWrite(value))
                return;

            this.writer.WriteAsync(value).GetAwaiter().GetResult();
        }

        private async Task WriteAsyncInternal(ReadOnlyMemory<char> buffer)
        {
            for (var index = 0; index < buffer.Span.Length; index++)
            {
                var ch = buffer.Span[index];
                await this.writer.WriteAsync(ch).ConfigureAwait(false);
            }
        }
    }

    private sealed class BufferedTextReader(ChannelReader<char> reader) : TextReader
    {
        public override int Peek()
        {
            while (true)
            {
                if (reader.TryPeek(out var next))
                    return next;

                if (!reader.WaitToReadAsync().GetAwaiter().GetResult())
                    return -1;
            }
        }

        public override int Read()
        {
            while (true)
            {
                if (reader.TryRead(out var next))
                    return next;

                if (!reader.WaitToReadAsync().GetAwaiter().GetResult())
                    return -1;
            }
        }

        public override int Read(char[] buffer, int index, int count) => this.Read(buffer.AsSpan(index, count));

        public override int Read(Span<char> buffer)
        {
            var written = 0;

            while (written < buffer.Length)
            {
                if (reader.TryRead(out var ch))
                {
                    buffer[written++] = ch;

                    continue;
                }

                if (!reader.WaitToReadAsync().GetAwaiter().GetResult())
                    break;
            }

            return written;
        }

        public override Task<int> ReadAsync(char[] buffer, int index, int count) => this.ReadAsync(buffer.AsMemory(index, count)).AsTask();

        public ValueTask<int> ReadAsync(Memory<char> buffer, CancelToken cancelToken = default) => this.ReadAsyncInternal(buffer, cancelToken);

        private async ValueTask<int> ReadAsyncInternal(Memory<char> buffer, CancelToken cancelToken)
        {
            using (cancelToken.Bridge(out var token))
            {
                var written = 0;

                while (written < buffer.Length)
                {
                    while (reader.TryRead(out var ch))
                    {
                        buffer.Span[written++] = ch;

                        if (written == buffer.Length)
                            return written;
                    }

                    if (!await reader.WaitToReadAsync(token).ConfigureAwait(false))
                        break;
                }

                return written;
            }
        }
    }
}
