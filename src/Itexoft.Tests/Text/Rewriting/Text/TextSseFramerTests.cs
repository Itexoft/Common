// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Text.Rewriting.Primitives;
using Itexoft.Text.Rewriting.Text;

namespace Itexoft.Tests.Text.Rewriting.Text;

public sealed class TextSseFramerTests
{
    [Test]
    public void DropOversizedEventWhenConfigured()
    {
        var plan = new TextRewritePlanBuilder().Build();
        var output = new StringWriter();

        using var writer = new TextRewriteWriter(
            output,
            plan,
            new()
            {
                SseDelimiter = ";;",
                SseMaxEventSize = 3,
                SseOverflowBehavior = OverflowBehavior.Drop,
            });

        writer.Write("1234;;OK;;");
        writer.Flush();

        Assert.That(output.ToString(), Is.EqualTo("OK;;"));
    }

    [Test]
    public void DropOversizedEventAcrossChunks()
    {
        var plan = new TextRewritePlanBuilder().Build();
        var output = new StringWriter();

        using var writer = new TextRewriteWriter(
            output,
            plan,
            new()
            {
                SseDelimiter = "||",
                SseMaxEventSize = 3,
                SseOverflowBehavior = OverflowBehavior.Drop,
            });

        writer.Write("12");
        writer.Write("345||ok||");
        writer.Flush();

        Assert.That(output.ToString(), Is.EqualTo("ok||"));
    }

    [Test]
    public void DropOversizedEventWithDelimiterIncluded()
    {
        var plan = new TextRewritePlanBuilder().Build();
        var output = new StringWriter();

        using var writer = new TextRewriteWriter(
            output,
            plan,
            new()
            {
                SseDelimiter = "|",
                SseIncludeDelimiter = true,
                SseMaxEventSize = 3,
                SseOverflowBehavior = OverflowBehavior.Drop,
            });

        writer.Write("123|ok|");
        writer.Flush();

        Assert.That(output.ToString(), Is.EqualTo("ok|"));
    }

    [Test]
    public void ThrowsOnOversizedEventWhenBehaviorIsError() => Assert.Throws<FormatException>(() =>
    {
        var plan = new TextRewritePlanBuilder().Build();

        using var writer = new TextRewriteWriter(
            new StringWriter(),
            plan,
            new()
            {
                SseDelimiter = ";;",
                SseMaxEventSize = 3,
                SseOverflowBehavior = OverflowBehavior.Error,
            });

        writer.Write("TOOBIG;;");
        writer.Flush();
    });
}
