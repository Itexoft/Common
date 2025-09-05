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
    
    public bool IsCompleted => this.channel.Reader.Completion.IsCompleted;

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

    private sealed class BufferedTextWriter(Channel<char> channel) : TextWriter
    {
        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char value)
        {
            channel.Writer.WriteAsync(value).AsTask().Wait();
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