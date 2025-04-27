using System.Text;
using System.Threading.Channels;

namespace Itexoft.Common.IO;

public class PipeText : IDisposable
{
    private readonly Channel<char> channel;

    public PipeText(int capacity = 512)
    {
        this.channel = Channel.CreateBounded<char>(
            new BoundedChannelOptions(capacity) { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
        this.Reader = new BufferedTextReader(this.channel);
        this.Writer = new BufferedTextWriter(this.channel);
    }

    public TextReader Reader { get; }
    public TextWriter Writer { get; }

    public void Dispose()
    {
        this.Reader.Dispose();
        this.Writer.Dispose();
    }

    public void Complete()
    {
        this.channel.Writer.TryComplete();
    }

    private sealed class BufferedTextWriter(ChannelWriter<char> writer) : TextWriter
    {
        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char value)
        {
            writer.WriteAsync(value).AsTask().Wait();
        }
    }

    private sealed class BufferedTextReader(ChannelReader<char> reader) : TextReader
    {
        public override int Peek()
        {
            while (true)
            {
                var task = reader.WaitToReadAsync().AsTask();
                task.Wait();

                if (reader.TryPeek(out var next))
                    return next;
                
                if (!task.Result)
                    return -1;
            }
        }

        public override int Read()
        {
            while (true)
            {
                var task = reader.WaitToReadAsync().AsTask();
                task.Wait();

                if (reader.TryRead(out var next))
                {
                    return next;
                }

                if (!task.Result)
                    return -1;
            }
        }
    }
}