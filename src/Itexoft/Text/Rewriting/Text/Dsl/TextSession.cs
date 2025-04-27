// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Core;
using Itexoft.Text.Rewriting.Internal.Runtime;
using Itexoft.Text.Rewriting.Primitives;
using Itexoft.Threading;

namespace Itexoft.Text.Rewriting.Text.Dsl;

/// <summary>
/// Stateful streaming session that applies compiled text rules while writing to an output.
/// </summary>
public sealed class TextSession<THandlers> : IDisposable, IAsyncDisposable
{
    private readonly THandlers handlers;
    private readonly RuleInfo[] ruleInfos;
    private readonly HandlerScope<THandlers> scope;
    private readonly TextRewriteWriter writer;
    private Disposed disposed;

    internal TextSession(
        TextRewritePlan plan,
        HandlerScope<THandlers> scope,
        RuleInfo[] ruleInfos,
        TextWriter output,
        THandlers handlers,
        TextRuntimeOptions? options)
    {
        this.handlers = handlers;
        this.scope = scope;
        this.ruleInfos = ruleInfos;
        this.Metrics = new(0, 0, 0, 0, 0, 0);
        this.writer = new(output, plan, MapOptions(options, ruleInfos, this));
    }

    /// <summary>
    /// Exposes runtime metrics collected for the current session.
    /// </summary>
    public RewriteMetrics Metrics { get; private set; }

    /// <summary>
    /// Asynchronously disposes the session.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (this.disposed.Enter())
            return;

        await this.writer.DisposeAsync();
    }

    /// <summary>
    /// Disposes the session and releases associated resources.
    /// </summary>
    public void Dispose()
    {
        if (this.disposed.Enter())
            return;

        this.writer.Dispose();
    }

    /// <summary>
    /// Writes a text chunk synchronously, applying rewrite rules.
    /// </summary>
    public void Write(ReadOnlySpan<char> chunk)
    {
        this.disposed.ThrowIf();

        using var guard = this.scope.Push(this.handlers);
        this.writer.Write(chunk);
    }

    /// <summary>
    /// Writes a text chunk asynchronously, applying rewrite rules.
    /// </summary>
    public async ValueTask WriteAsync(ReadOnlyMemory<char> chunk, CancelToken cancelToken = default)
    {
        this.disposed.ThrowIf();

        cancelToken.ThrowIf();
        using var guard = this.scope.Push(this.handlers);
        await this.writer.WriteAsync(chunk.ToArray(), 0, chunk.Length).ConfigureAwait(false);
    }

    /// <summary>
    /// Flushes the writer, emitting any buffered tail required for matching.
    /// </summary>
    public void Flush()
    {
        this.disposed.ThrowIf();

        using var guard = this.scope.Push(this.handlers);
        this.writer.Flush();
    }

    /// <summary>
    /// Asynchronously flushes the writer, emitting any buffered tail required for matching.
    /// </summary>
    public async ValueTask FlushAsync(CancelToken cancelToken = default)
    {
        this.disposed.ThrowIf();

        cancelToken.ThrowIf();
        using var guard = this.scope.Push(this.handlers);
        await this.writer.FlushAsync().ConfigureAwait(false);
    }

    private static TextRewriteOptions MapOptions(TextRuntimeOptions? runtime, RuleInfo[] rules, TextSession<THandlers> session)
    {
        runtime ??= new();
        var ruleNames = new string?[rules.Length];
        var ruleGroups = new string?[rules.Length];

        for (var i = 0; i < rules.Length; i++)
        {
            ruleNames[i] = rules[i].Name;
            ruleGroups[i] = rules[i].Group;
        }

        var enabledGroups = runtime.EnabledGroups is null ? null : new HashSet<string>(runtime.EnabledGroups, StringComparer.Ordinal);

        var options = new TextRewriteOptions
        {
            RightWriteBlockSize = runtime.RightWriteBlockSize,
            FlushBehavior = runtime.FlushBehavior,
            InputNormalizer = runtime.InputNormalizer,
            OutputFilter = runtime.OutputFilter,
            OutputFilterAsync = runtime.OutputFilterAsync,
            RuleGate = (id, metrics) =>
            {
                if (enabledGroups is not null && ruleGroups[id] is not null && !enabledGroups.Contains(ruleGroups[id]!))
                    return false;

                if (runtime.RuleGate is null)
                    return true;

                return runtime.RuleGate(new(id, rules[id].Name, rules[id].Group), metrics);
            },
            RuleGateAsync = runtime.RuleGateAsync is null
                ? null
                : async (id, metrics) =>
                {
                    if (enabledGroups is not null && ruleGroups[id] is not null && !enabledGroups.Contains(ruleGroups[id]!))
                        return false;

                    return await runtime.RuleGateAsync(new(id, rules[id].Name, rules[id].Group), metrics).ConfigureAwait(false);
                },
            BeforeApply = runtime.BeforeApply is null ? null : ctx => runtime.BeforeApply(ToContext(ctx, rules)),
            BeforeApplyAsync = runtime.BeforeApplyAsync is null ? null : ctx => runtime.BeforeApplyAsync(ToContext(ctx, rules)),
            AfterApply = runtime.AfterApply is null ? null : ctx => runtime.AfterApply(ToContext(ctx, rules)),
            AfterApplyAsync = runtime.AfterApplyAsync is null ? null : ctx => runtime.AfterApplyAsync(ToContext(ctx, rules)),
            OnMetrics = metrics =>
            {
                session.Metrics = metrics;
                runtime.OnMetrics?.Invoke(metrics);
            },
            OnMetricsAsync = async metrics =>
            {
                session.Metrics = metrics;

                if (runtime.OnMetricsAsync is not null)
                    await runtime.OnMetricsAsync(metrics).ConfigureAwait(false);
                else
                    runtime.OnMetrics?.Invoke(metrics);
            },
            OnRuleMetrics = runtime.OnRuleMetrics is null ? null : stats => runtime.OnRuleMetrics(Project(stats, ruleNames, ruleGroups)),
            OnRuleMetricsAsync = runtime.OnRuleMetricsAsync is null
                ? null
                : stats => runtime.OnRuleMetricsAsync(Project(stats, ruleNames, ruleGroups)),
            RuleNames = ruleNames,
            RuleGroups = ruleGroups,
            EnabledGroups = runtime.EnabledGroups,
            SseDelimiter = runtime.Sse?.Delimiter,
            SseIncludeDelimiter = runtime.Sse?.IncludeDelimiter ?? false,
            SseMaxEventSize = runtime.Sse?.MaxEventSize,
            SseOverflowBehavior = runtime.Sse?.OverflowBehavior ?? OverflowBehavior.Error,
            TextFraming = runtime.TextFraming,
        };

        return options;
    }

    private static TextMatchContext ToContext(MatchContext context, RuleInfo[] rules) => new(
        new(context.RuleId, rules[context.RuleId].Name, rules[context.RuleId].Group),
        context.MatchLength,
        context.Metrics);

    private static IReadOnlyList<RuleStat> Project(IReadOnlyList<RuleStat> stats, string?[] names, string?[] groups)
    {
        var list = new List<RuleStat>(stats.Count);

        for (var i = 0; i < stats.Count; i++)
        {
            var s = stats[i];
            var name = s.RuleId < names.Length ? names[s.RuleId] : null;
            var group = s.RuleId < groups.Length ? groups[s.RuleId] : null;
            list.Add(new(s.RuleId, name, group, s.Hits, s.Elapsed));
        }

        return list;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(this.disposed, "This session has been disposed.");
}
