// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Text;
using Itexoft.Core;
using Itexoft.Text.Rewriting.Internal.Runtime;
using Itexoft.Text.Rewriting.Primitives;
using Itexoft.Threading;

namespace Itexoft.Text.Rewriting.Json.Dsl;

/// <summary>
/// Stateful writer that buffers incoming text, applies JSON rewrite rules, and emits rewritten frames.
/// </summary>
public sealed class JsonSession<THandlers> : IDisposable, IAsyncDisposable
{
    private readonly StringBuilder buffer = new();
    private readonly IJsonFraming? framing;
    private readonly THandlers handlers;
    private readonly JsonRewriteOptions rewriteOptions;
    private readonly HandlerScope<THandlers> scope;
    private readonly JsonRewriteWriter writer;
    private Disposed disposed;

    internal JsonSession(
        JsonRewritePlan plan,
        HandlerScope<THandlers> scope,
        RuleInfo[] rules,
        TextWriter output,
        THandlers handlers,
        JsonKernelOptions? options)
    {
        this.scope = scope;
        this.handlers = handlers;
        this.framing = options?.Framing;

        this.rewriteOptions = MapOptions(options, rules);

        this.writer = new(output, plan, this.rewriteOptions);
    }

    public ValueTask DisposeAsync()
    {
        this.Dispose();

        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        if (this.disposed.Enter())
            return;

        this.writer.Dispose();
    }

    /// <summary>
    /// Appends a chunk of input to the current buffer and emits complete frames synchronously.
    /// </summary>
    public void Write(ReadOnlySpan<char> chunk)
    {
        this.disposed.ThrowIf();

        using var guard = this.scope.Push(this.handlers);
        this.buffer.Append(chunk);
        this.TryEmitFrames();
    }

    /// <summary>
    /// Appends a chunk of input to the current buffer and emits complete frames asynchronously.
    /// </summary>
    public async ValueTask WriteAsync(ReadOnlyMemory<char> chunk, CancelToken cancelToken = default)
    {
        this.disposed.ThrowIf();
        cancelToken.ThrowIf();
        using var guard = this.scope.Push(this.handlers);
        this.buffer.Append(chunk.Span);
        await this.TryEmitFramesAsync(cancelToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Forces processing of the buffered content as a single frame.
    /// </summary>
    public void Commit()
    {
        this.disposed.ThrowIf();

        using var guard = this.scope.Push(this.handlers);

        if (this.buffer.Length == 0)
            return;

        if (!this.ShouldProcessFrame(this.buffer.Length))
        {
            this.buffer.Clear();

            return;
        }

        this.ProcessFrame(this.buffer.ToString().AsSpan());
        this.buffer.Clear();
    }

    /// <summary>
    /// Asynchronously forces processing of the buffered content as a single frame.
    /// </summary>
    public async ValueTask CommitAsync(CancelToken cancelToken = default)
    {
        this.disposed.ThrowIf();

        cancelToken.ThrowIf();
        using var guard = this.scope.Push(this.handlers);

        if (this.buffer.Length == 0)
            return;

        if (!this.ShouldProcessFrame(this.buffer.Length))
        {
            this.buffer.Clear();

            return;
        }

        await this.ProcessFrameAsync(this.buffer.ToString().AsMemory(), cancelToken).ConfigureAwait(false);
        this.buffer.Clear();
    }

    /// <summary>
    /// Clears the buffered content without emitting it.
    /// </summary>
    public void Reset()
    {
        this.disposed.ThrowIf();

        this.buffer.Clear();
    }

    private void TryEmitFrames()
    {
        if (this.framing is null)
            return;

        var text = this.buffer.ToString();
        var remaining = text.AsSpan();

        while (this.framing.TryCutFrame(ref remaining, out var frame))
        {
            if (!this.ShouldProcessFrame(frame.Length))
                continue;

            this.ProcessFrame(frame);
        }

        this.buffer.Clear();

        if (!remaining.IsEmpty)
            this.buffer.Append(remaining);
    }

    private async ValueTask TryEmitFramesAsync(CancelToken cancelToken)
    {
        if (this.framing is null)
            return;

        var text = this.buffer.ToString();
        var remaining = text.AsMemory();

        while (this.framing.TryCutFrame(ref remaining, out var frame))
        {
            cancelToken.ThrowIf();

            if (!this.ShouldProcessFrame(frame.Length))
                continue;

            await this.ProcessFrameAsync(frame, cancelToken).ConfigureAwait(false);
        }

        this.buffer.Clear();

        if (!remaining.IsEmpty)
            this.buffer.Append(remaining.Span);
    }

    private void ProcessFrame(ReadOnlySpan<char> frame)
    {
        this.writer.Write(frame.ToString());
        this.writer.Flush();
    }

    private async ValueTask ProcessFrameAsync(ReadOnlyMemory<char> frame, CancelToken cancelToken)
    {
        cancelToken.ThrowIf();
        await this.writer.WriteAsync(frame.ToString());
        await this.writer.FlushAsync().ConfigureAwait(false);
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(this.disposed, "This session has been disposed.");

    private bool ShouldProcessFrame(int length)
    {
        if (!this.rewriteOptions.MaxFrameSize.HasValue)
            return true;

        if (length <= this.rewriteOptions.MaxFrameSize.Value)
            return true;

        if (this.rewriteOptions.FrameOverflowBehavior == OverflowBehavior.Drop)
            return false;

        throw new FormatException("JSON frame exceeded maximum allowed size.");
    }

    private static JsonRewriteOptions MapOptions(JsonKernelOptions? runtime, RuleInfo[] rules)
    {
        runtime ??= new();

        var ruleNames = new string?[rules.Length];
        var ruleGroups = new string?[rules.Length];

        for (var i = 0; i < rules.Length; i++)
        {
            ruleNames[i] = rules[i].Name;
            ruleGroups[i] = rules[i].Group;
        }

        HashSet<string>? enabledGroups = null;

        if (runtime.EnabledGroups is not null)
            enabledGroups = new(runtime.EnabledGroups, StringComparer.Ordinal);

        return new()
        {
            UnwrapPrefix = runtime.UnwrapPrefix,
            PrefixRequired = runtime.PrefixRequired,
            OnMalformedJson = runtime.OnMalformedJson,
            RuleGate = runtime.RuleGate is null && enabledGroups is null
                ? null
                : id =>
                {
                    if (enabledGroups is not null && ruleGroups[id] is not null && !enabledGroups.Contains(ruleGroups[id]!))
                        return false;

                    if (runtime.RuleGate is null)
                        return true;

                    return runtime.RuleGate(new(id, ruleNames[id], ruleGroups[id]));
                },
            OnRuleMetrics = runtime.OnRuleMetrics is null ? null : stats => runtime.OnRuleMetrics(Project(stats, ruleNames, ruleGroups)),
            OnRuleMetricsAsync = runtime.OnRuleMetricsAsync is null
                ? null
                : stats => runtime.OnRuleMetricsAsync(Project(stats, ruleNames, ruleGroups)),
            RuleNames = ruleNames,
            RuleGroups = ruleGroups,
            EnabledGroups = runtime.EnabledGroups,
            MaxFrameSize = runtime.MaxFrameSize,
            FrameOverflowBehavior = runtime.FrameOverflowBehavior,
        };
    }

    private static IReadOnlyList<RuleStat> Project(IReadOnlyList<RuleStat> stats, string?[] names, string?[] groups)
    {
        var list = new List<RuleStat>(stats.Count);

        for (var i = 0; i < stats.Count; i++)
        {
            var stat = stats[i];
            var name = stat.RuleId < names.Length ? names[stat.RuleId] : null;
            var group = stat.RuleId < groups.Length ? groups[stat.RuleId] : null;
            list.Add(new(stat.RuleId, name, group, stat.Hits, stat.Elapsed));
        }

        return list;
    }
}
