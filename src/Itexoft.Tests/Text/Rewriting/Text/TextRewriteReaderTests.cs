// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Text.RegularExpressions;
using Itexoft.Text.Rewriting.Primitives;
using Itexoft.Text.Rewriting.Text;
using Itexoft.Threading;

namespace Itexoft.Tests.Text.Rewriting;

public sealed class TextRewriteReaderTests
{
    [Test]
    public void PassThroughWithoutRules()
    {
        var plan = new TextRewritePlanBuilder().Build();

        using var reader = new TextRewriteReader(new StringReader("alpha beta"), plan);
        var buffer = new char[32];
        var read = reader.Read(buffer);

        Assert.That(read, Is.EqualTo("alpha beta".Length));
        Assert.That(new string(buffer.AsSpan(0, read)), Is.EqualTo("alpha beta"));
    }

    [Test]
    public void RemovesLiteralWhileReading()
    {
        var plan = new TextRewritePlanBuilder().RemoveLiteral("secret").Build();

        using var reader = new TextRewriteReader(new StringReader("keep secret hidden"), plan);
        var output = reader.ReadToEnd();

        Assert.That(output, Is.EqualTo("keep  hidden"));
    }

    [Test]
    public void ReplacementFactoryRunsOnMatches()
    {
        var seen = new List<(int, string)>();

        var plan = new TextRewritePlanBuilder().ReplaceLiteral(
            "abc",
            (id, span) =>
            {
                seen.Add((id, span.ToString()));

                return span.ToString().ToUpperInvariant();
            }).Build();

        using var reader = new TextRewriteReader(new StringReader("abc-abc"), plan);
        var output = reader.ReadToEnd();

        Assert.That(output, Is.EqualTo("ABC-ABC"));
        Assert.That(seen, Is.EqualTo(new List<(int, string)> { (0, "abc"), (0, "abc") }));
    }

    [Test]
    public void CaseInsensitiveLiteralIsRemoved()
    {
        var plan = new TextRewritePlanBuilder().RemoveLiteral("PING", comparison: StringComparison.OrdinalIgnoreCase).Build();

        using var reader = new TextRewriteReader(new StringReader("piNg pong"), plan);
        var output = reader.ReadToEnd();

        Assert.That(output, Is.EqualTo(" pong"));
    }

    [Test]
    public void MatchSelectionLongestThenPriority()
    {
        var plan = new TextRewritePlanBuilder().RemoveLiteral("bab", 1).ReplaceLiteral("ab", "X", 0)
            .Build(new() { MatchSelection = MatchSelection.LongestThenPriority });

        using var reader = new TextRewriteReader(new StringReader("bab"), plan);
        var output = reader.ReadToEnd();

        Assert.That(output, Is.EqualTo(string.Empty));
    }

    [Test]
    public void MatchSelectionPriorityThenLongest()
    {
        var plan = new TextRewritePlanBuilder().RemoveLiteral("bab", 1).ReplaceLiteral("ab", "X", 0)
            .Build(new() { MatchSelection = MatchSelection.PriorityThenLongest });

        using var reader = new TextRewriteReader(new StringReader("bab"), plan);
        var output = reader.ReadToEnd();

        Assert.That(output, Is.EqualTo("bX"));
    }

    [Test]
    public void TailContentFlushesAtEndOfStream()
    {
        var plan = new TextRewritePlanBuilder().RemoveLiteral("abc").Build();

        using var reader = new TextRewriteReader(new StringReader("ab"), plan);
        Span<char> buffer = stackalloc char[4];

        var read = reader.Read(buffer);

        Assert.That(read, Is.EqualTo(2));
        Assert.That(new string(buffer[..read]), Is.EqualTo("ab"));
    }

    [Test]
    public async Task ReadAsyncHonorsCancellation()
    {
        var plan = new TextRewritePlanBuilder().RemoveRegex(new Regex("x"), 1).Build();

        await using var reader = new TextRewriteReader(new StringReader("x"), plan, new() { FlushBehavior = FlushBehavior.Commit });

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync().ConfigureAwait(false);

        var buffer = new char[4];
        Assert.ThrowsAsync<OperationCanceledException>(() => reader.ReadAsync(buffer.AsMemory(), cts.Token).AsTask());
    }

    [Test]
    public void ReaderInputNormalizerDropsCharacters()
    {
        var plan = new TextRewritePlanBuilder().Build();

        using var reader = new TextRewriteReader(
            new StringReader("a\u200bb"),
            plan,
            new()
            {
                InputNormalizer = c => c == '\u200b' ? '\0' : c,
                FlushBehavior = FlushBehavior.Commit,
            });

        var output = reader.ReadToEnd();

        Assert.That(output, Is.EqualTo("ab"));
    }

    [Test]
    public void ReaderOutputFilterTransformsText()
    {
        var plan = new TextRewritePlanBuilder().Build();

        using var reader = new TextRewriteReader(
            new StringReader("abc"),
            plan,
            new()
            {
                FlushBehavior = FlushBehavior.Commit,
                OutputFilter = (span, _) => span.ToString().ToUpperInvariant(),
            });

        var output = reader.ReadToEnd();

        Assert.That(output, Is.EqualTo("ABC"));
    }

    [Test]
    public void ReaderRuleGateDisablesRuleAfterFirstMatch()
    {
        var plan = new TextRewritePlanBuilder().RemoveLiteral("x").Build();

        using var reader = new TextRewriteReader(
            new StringReader("xx"),
            plan,
            new()
            {
                FlushBehavior = FlushBehavior.Commit,
                RuleGate = (id, metrics) => metrics.MatchesApplied == 0,
            });

        var output = reader.ReadToEnd();

        Assert.That(output, Is.EqualTo("x"));
    }

    [Test]
    public void ReaderBeforeApplyCancelsMutation()
    {
        var plan = new TextRewritePlanBuilder().RemoveLiteral("x").Build();

        using var reader = new TextRewriteReader(
            new StringReader("x"),
            plan,
            new()
            {
                FlushBehavior = FlushBehavior.Commit,
                BeforeApply = _ => false,
            });

        var output = reader.ReadToEnd();

        Assert.That(output, Is.EqualTo("x"));
    }

    [Test]
    public async Task ReaderAsyncRespectsOutputFilter()
    {
        var plan = new TextRewritePlanBuilder().Build();

        await using var reader = new TextRewriteReader(
            new StringReader("abc"),
            plan,
            new()
            {
                FlushBehavior = FlushBehavior.Commit,
                OutputFilter = (span, _) => span.ToString().ToUpperInvariant(),
            });

        var buffer = new char[4];
        var read = await reader.ReadAsync(buffer.AsMemory(), CancelToken.None).ConfigureAwait(false);

        Assert.That(read, Is.EqualTo(3));
        Assert.That(new string(buffer.AsSpan(0, read)), Is.EqualTo("ABC"));
    }
}
