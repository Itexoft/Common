// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Text;
using Itexoft.Text;

namespace Itexoft.Tests.Text;

public sealed class TextChannelTests
{
    [Test]
    public void SyncWriterReaderRoundTripsSpanAndString()
    {
        using var pipe = new TextChannel(capacity: 16);

        pipe.Writer.Write("abc");
        pipe.Writer.Write(['d', 'e', 'f'], 0, 3);
        pipe.Writer.Write("gh".AsSpan());
        pipe.Complete();

        var buffer = new char[32];
        var read = pipe.Reader.Read(buffer, 0, buffer.Length);

        Assert.That(read, Is.EqualTo(8));
        Assert.That(new string(buffer, 0, read), Is.EqualTo("abcdefgh"));
        Assert.That(pipe.Reader.Read(), Is.EqualTo(-1));
    }

    [Test]
    public async Task AsyncWriterReaderKeepsOrder()
    {
        using var pipe = new TextChannel(capacity: 2);
        var payload = "hello-world";

        var writerTask = Task.Run(async () =>
        {
            foreach (var ch in payload)
                await pipe.Writer.WriteAsync(ch).ConfigureAwait(false);

            await pipe.Writer.WriteAsync("!").ConfigureAwait(false);
            pipe.Complete();
        });

        var result = new StringBuilder();
        var buffer = new char[3];
        int read;

        while ((read = await pipe.Reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
            result.Append(buffer, 0, read);

        await writerTask.ConfigureAwait(false);
        Assert.That(result.ToString(), Is.EqualTo(payload + "!"));
    }

    [Test]
    public async Task BackpressureBlocksUntilReaderConsumes()
    {
        using var pipe = new TextChannel(capacity: 1);

        var writer = Task.Run(async () =>
        {
            await pipe.Writer.WriteAsync('A').ConfigureAwait(false);
            await pipe.Writer.WriteAsync('B').ConfigureAwait(false); // should wait until 'A' consumed
            pipe.Complete();
        });

        await Task.Delay(50).ConfigureAwait(false);
        Assert.That(writer.IsCompleted, Is.False);

        var first = pipe.Reader.Read();
        Assert.That(first, Is.EqualTo('A'));

        await writer.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
        var second = pipe.Reader.Read();
        Assert.That(second, Is.EqualTo('B'));
        Assert.That(pipe.Reader.Read(), Is.EqualTo(-1));
    }

    [Test]
    public void CompleteMakesReaderEndOfStream()
    {
        using var pipe = new TextChannel();

        pipe.Complete();
        Assert.That(pipe.Reader.Read(), Is.EqualTo(-1));

        var buffer = new char[4];
        var read = pipe.Reader.Read(buffer, 0, buffer.Length);
        Assert.That(read, Is.EqualTo(0));
    }

    [Test]
    public async Task DisposeCompletesChannel()
    {
        var pipe = new TextChannel();

        await pipe.Writer.WriteAsync("abc").ConfigureAwait(false);
        pipe.Dispose();

        var buffer = new char[8];
        var read = await pipe.Reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
        Assert.That(read, Is.EqualTo(3));
        Assert.That(new string(buffer, 0, read), Is.EqualTo("abc"));

        var tail = pipe.Reader.Read();
        Assert.That(tail, Is.EqualTo(-1));
        Assert.That(pipe.IsCompleted, Is.True);
    }
}
