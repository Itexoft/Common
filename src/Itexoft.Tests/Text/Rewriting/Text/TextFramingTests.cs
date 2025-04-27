// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Text.Rewriting.Text;
using Itexoft.Text.Rewriting.Text.Dsl;
using Itexoft.Threading;

namespace Itexoft.Tests.Text.Rewriting.Text;

public sealed class TextFramingTests
{
    [Test]
    public void DelimiterFramingFeedsFilterByFrame()
    {
        var plan = new TextRewritePlanBuilder().Build();
        var writer = new StringWriter();

        using var text = new TextRewriteWriter(
            writer,
            plan,
            new()
            {
                TextFraming = new DelimiterTextFraming("|"),
                OutputFilter = (span, _) => $"[{span.ToString()}]",
            });

        text.Write("one|two|");
        text.Flush();

        Assert.That(writer.ToString(), Is.EqualTo("[one][two]"));
    }

    [Test]
    public void LengthPrefixFramingBuffersUntilFrameIsComplete()
    {
        var plan = new TextRewritePlanBuilder().Build();
        var writer = new StringWriter();

        using var text = new TextRewriteWriter(
            writer,
            plan,
            new()
            {
                TextFraming = new LengthPrefixTextFraming(),
                OutputFilter = (span, _) => $"[{span.ToString()}]",
            });

        text.Write("3:one5:he");
        text.Write("llo2:ok");
        text.Flush();

        Assert.That(writer.ToString(), Is.EqualTo("[one][hello][ok]"));
    }

    [Test]
    public async Task LengthPrefixFramingWorksWithAsyncFilter()
    {
        var plan = new TextRewritePlanBuilder().Build();
        var writer = new StringWriter();

        var text = new TextRewriteWriter(
            writer,
            plan,
            new()
            {
                TextFraming = new LengthPrefixTextFraming(),
                OutputFilterAsync = async (frame, _) =>
                {
                    await Task.Yield();

                    return $"<{frame}>";
                },
            });

        await using var text1 = text.ConfigureAwait(false);

        await text.WriteAsync("4:ping4:pong3:hey".AsMemory(), CancelToken.None).ConfigureAwait(false);
        await text.FlushAsync().ConfigureAwait(false);

        Assert.That(writer.ToString(), Is.EqualTo("<ping><pong><hey>"));
    }
}
