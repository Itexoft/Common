// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Diagnostics;
using System.Globalization;
using System.Text;
using Itexoft.Core;
using Itexoft.Extensions;
using Itexoft.Text.Rewriting.Json.Dsl;
using Itexoft.Text.Rewriting.Json.Internal.Rules;
using Itexoft.Text.Rewriting.Primitives;

namespace Itexoft.Text.Rewriting.Json;

/// <summary>
/// <see cref="TextWriter" /> that buffers, rewrites, and emits JSON using a <see cref="JsonRewritePlan" />. Input is
/// accumulated until <see cref="Flush" />/<see cref="FlushAsync" />, where the JSON document is parsed and rewritten
/// without <c>System.Text.Json</c>.
/// </summary>
/// <param name="underlying">Destination writer.</param>
/// <param name="plan">Compiled JSON rewrite plan.</param>
/// <param name="options">Runtime options; optional.</param>
public sealed class JsonRewriteWriter : TextWriter
{
    private readonly StringBuilder buffer = new();
    private readonly bool collectRuleMetrics;
    private readonly HashSet<string>? enabledGroups;
    private readonly JsonRewriteOptions options;
    private readonly JsonRewritePlan plan;
    private readonly long[]? ruleElapsedTicks;
    private readonly Func<int, bool>? ruleGate;
    private readonly long[]? ruleHits;
    private readonly TextWriter underlying;
    private Disposed disposed;

    public JsonRewriteWriter(TextWriter underlying, JsonRewritePlan plan, JsonRewriteOptions? options = null)
    {
        this.underlying = underlying ?? throw new ArgumentNullException(nameof(underlying));
        this.plan = plan ?? throw new ArgumentNullException(nameof(plan));
        this.options = options ?? new JsonRewriteOptions();
        this.enabledGroups = this.options.EnabledGroups is null ? null : new HashSet<string>(this.options.EnabledGroups, StringComparer.Ordinal);
        this.ruleGate = this.options.RuleGate;
        this.collectRuleMetrics = this.options.OnRuleMetrics is not null || this.options.OnRuleMetricsAsync is not null;

        if (this.collectRuleMetrics)
        {
            this.ruleHits = new long[this.plan.RuleCount];
            this.ruleElapsedTicks = new long[this.plan.RuleCount];
        }
    }

    public override Encoding Encoding => this.underlying.Encoding;

    public override string NewLine
    {
        get => this.underlying.NewLine;
        set => this.underlying.NewLine = value;
    }

    public override IFormatProvider FormatProvider => this.underlying.FormatProvider;

    public override void Write(char value) => this.buffer.Append(value);

    public override void Write(string? value)
    {
        if (value is not null)
            this.buffer.Append(value);
    }

    public override void Write(char[] buffer, int index, int count)
    {
        buffer.Required();

        this.buffer.Append(buffer, index, count);
    }

    public override Task WriteAsync(char value)
    {
        this.Write(value);

        return Task.CompletedTask;
    }

    public override Task WriteAsync(string? value)
    {
        this.Write(value);

        return Task.CompletedTask;
    }

    public override Task WriteAsync(char[] buffer, int index, int count)
    {
        this.Write(buffer, index, count);

        return Task.CompletedTask;
    }

    public override void Flush()
    {
        var result = this.plan.HasAsyncRules ? this.ProcessBufferAsync().GetAwaiter().GetResult() : this.ProcessBufferSync();
        this.underlying.Write(result.Text);
        this.RunPendingCaptures(result.AsyncCaptures);
        this.PublishRuleMetrics();
        this.underlying.Flush();
    }

    public async override Task FlushAsync()
    {
        var result = await this.ProcessBufferAsync().ConfigureAwait(false);
        await this.underlying.WriteAsync(result.Text);
        await this.RunPendingCapturesAsync(result.AsyncCaptures).ConfigureAwait(false);
        await this.PublishRuleMetricsAsync().ConfigureAwait(false);
        await this.underlying.FlushAsync().ConfigureAwait(false);
    }

    protected override void Dispose(bool disposing)
    {
        if (!disposing)
        {
            base.Dispose(disposing);

            return;
        }

        if (this.disposed.Enter())
            return;

        this.Flush();
        this.underlying.Dispose();
        base.Dispose(disposing);
    }

    public async override ValueTask DisposeAsync()
    {
        if (this.disposed.Enter())
            return;

        await this.FlushAsync().ConfigureAwait(false);
        await this.underlying.DisposeAsync().ConfigureAwait(false);
    }

    private RewriteResult ProcessBufferSync()
    {
        if (this.buffer.Length == 0)
            return RewriteResult.Empty;

        var jsonText = this.buffer.ToString();
        this.ClearBuffer();

        if (!this.plan.HasAsyncRules)
        {
            if (this.TryRewrite(jsonText, out var rewritten, out var asyncCaptures))
                return new(rewritten, asyncCaptures);
        }
        else
        {
            var result = this.TryRewriteAsync(jsonText).GetAwaiter().GetResult();

            if (result is not null)
                return result.Value;
        }

        if (this.options.OnMalformedJson is not null)
        {
            var repaired = this.options.OnMalformedJson(jsonText);

            if (!string.IsNullOrEmpty(repaired))
            {
                if (!this.plan.HasAsyncRules)
                {
                    if (this.TryRewrite(repaired, out var rewritten, out var asyncCaptures))
                        return new(rewritten, asyncCaptures);
                }
                else
                {
                    var retry = this.TryRewriteAsync(repaired).GetAwaiter().GetResult();

                    if (retry is not null)
                        return retry.Value;
                }
            }
        }

        throw new FormatException("Invalid JSON input.");
    }

    private async ValueTask<RewriteResult> ProcessBufferAsync()
    {
        if (this.buffer.Length == 0)
            return RewriteResult.Empty;

        var jsonText = this.buffer.ToString();
        this.ClearBuffer();

        var result = await this.TryRewriteAsync(jsonText).ConfigureAwait(false);

        if (result is not null)
            return result.Value;

        if (this.options.OnMalformedJson is not null)
        {
            var repaired = this.options.OnMalformedJson(jsonText);

            if (!string.IsNullOrEmpty(repaired))
            {
                var retry = await this.TryRewriteAsync(repaired).ConfigureAwait(false);

                if (retry is not null)
                    return retry.Value;
            }
        }

        throw new FormatException("Invalid JSON input.");
    }

    private bool TryRewrite(string json, out string rewritten, out List<PendingAsyncCapture>? asyncCaptures)
    {
        var prepared = this.PrepareInput(json);

        return this.TryRewriteCore(prepared, out rewritten, out asyncCaptures);
    }

    private async ValueTask<RewriteResult?> TryRewriteAsync(string json)
    {
        var prepared = this.PrepareInput(json);
        var success = await this.TryRewriteCoreAsync(prepared).ConfigureAwait(false);

        return success;
    }

    private string PrepareInput(string json)
    {
        if (string.IsNullOrEmpty(this.options.UnwrapPrefix))
            return json;

        if (json.StartsWith(this.options.UnwrapPrefix, StringComparison.Ordinal))
            return json[this.options.UnwrapPrefix.Length..];

        if (this.options.PrefixRequired)
            throw new FormatException("Expected JSON prefix was not found.");

        return json;
    }

    private bool TryRewriteCore(string json, out string rewritten, out List<PendingAsyncCapture>? asyncCaptures)
    {
        var jsonSpan = json.AsSpan();
        var output = new StringBuilder(json.Length);
        var cursor = new JsonPathCursor();
        var frames = new Stack<JsonContainerFrame>();
        var requirements = this.CreateRequirementStates();
        asyncCaptures = null;
        var i = 0;

        while (i < jsonSpan.Length)
        {
            var ch = jsonSpan[i];

            if (char.IsWhiteSpace(ch))
            {
                output.Append(ch);
                i++;

                continue;
            }

            switch (ch)
            {
                case '{':
                {
                    var objectPath = cursor.GetValuePath();
                    var startIndex = output.Length;
                    output.Append(ch);
                    frames.Push(new(false, objectPath, startIndex));
                    cursor.PushContainer(false);
                    i++;

                    break;
                }

                case '}':
                {
                    if (!TryPeek(frames, out var objFrame) || objFrame.IsArray)
                    {
                        rewritten = string.Empty;

                        return false;
                    }

                    output.Append(ch);
                    this.HandleValueCompletion(objFrame, output, requirements, ref asyncCaptures);
                    frames.Pop();
                    cursor.PopContainer();
                    i++;

                    break;
                }

                case '[':
                {
                    var arrayPath = cursor.GetValuePath();
                    var startIndex = output.Length;
                    output.Append(ch);
                    frames.Push(new(true, arrayPath, startIndex));
                    cursor.PushContainer(true);
                    i++;

                    break;
                }

                case ']':
                {
                    if (!TryPeek(frames, out var arrFrame) || !arrFrame.IsArray)
                    {
                        rewritten = string.Empty;

                        return false;
                    }

                    output.Append(ch);
                    this.HandleValueCompletion(arrFrame, output, requirements, ref asyncCaptures);
                    frames.Pop();
                    cursor.PopContainer();
                    i++;

                    break;
                }

                case ':':
                    output.Append(ch);
                    cursor.OnColon();

                    i++;

                    break;

                case ',':
                    output.Append(ch);
                    cursor.OnComma();
                    i++;

                    break;

                case '"':
                    if (!JsonStringReader.TryReadString(json, i, out var strValue, out var rawLength))
                    {
                        rewritten = string.Empty;

                        return false;
                    }

                    if (cursor.TryPeek(out var frame) && frame.IsObject && frame.ExpectingProperty)
                    {
                        var path = cursor.BuildPropertyPath(strValue);
                        var newName = this.ApplyRename(path, strValue);

                        output.Append('"').Append(EscapeJsonString(newName)).Append('"');
                        cursor.OnPropertyNameRead(newName);
                    }
                    else
                    {
                        var path = cursor.GetValuePath();
                        var rendered = this.RenderStringValue(path, strValue);

                        output.Append(rendered);
                        this.HandleValueCompletion(path, rendered, requirements, ref asyncCaptures);
                        cursor.AdvanceAfterValue();
                    }

                    i += rawLength;

                    break;

                default:
                    var len = JsonLiteralReader.ReadLiteral(jsonSpan, i);

                    if (len == 0)
                    {
                        rewritten = string.Empty;

                        return false;
                    }

                    var valuePath = cursor.GetValuePath();
                    var literalText = jsonSpan.Slice(i, len).ToString();
                    var renderedLiteral = this.RenderLiteral(valuePath, literalText);

                    output.Append(renderedLiteral);
                    this.HandleValueCompletion(valuePath, renderedLiteral, requirements, ref asyncCaptures);
                    cursor.AdvanceAfterValue();
                    i += len;

                    break;
            }
        }

        if (cursor.PendingProperty is not null || frames.Count != 0 || cursor.FrameCount != 0)
        {
            rewritten = string.Empty;

            return false;
        }

        this.ValidateRequirements(requirements);

        rewritten = output.ToString();

        return true;
    }

    private async ValueTask<RewriteResult?> TryRewriteCoreAsync(string json)
    {
        var output = new StringBuilder(json.Length);
        var cursor = new JsonPathCursor();
        var frames = new Stack<JsonContainerFrame>();
        var requirements = this.CreateRequirementStates();
        List<PendingAsyncCapture>? asyncCaptures = null;
        var i = 0;
        var length = json.Length;

        while (i < length)
        {
            var ch = json[i];

            if (char.IsWhiteSpace(ch))
            {
                output.Append(ch);
                i++;

                continue;
            }

            switch (ch)
            {
                case '{':
                {
                    var objectPath = cursor.GetValuePath();
                    var startIndex = output.Length;
                    output.Append(ch);
                    frames.Push(new(false, objectPath, startIndex));
                    cursor.PushContainer(false);
                    i++;

                    break;
                }

                case '}':
                {
                    if (!TryPeek(frames, out var objFrame) || objFrame.IsArray)
                        return null;

                    output.Append(ch);

                    if (objFrame.ValuePath is not null)
                        await this.HandleValueCompletionAsync(objFrame, output, requirements, asyncCaptures ??= []).ConfigureAwait(false);

                    frames.Pop();
                    cursor.PopContainer();
                    i++;

                    break;
                }

                case '[':
                {
                    var arrayPath = cursor.GetValuePath();
                    var startIndex = output.Length;
                    output.Append(ch);
                    frames.Push(new(true, arrayPath, startIndex));
                    cursor.PushContainer(true);
                    i++;

                    break;
                }

                case ']':
                {
                    if (!TryPeek(frames, out var arrFrame) || !arrFrame.IsArray)
                        return null;

                    output.Append(ch);

                    if (arrFrame.ValuePath is not null)
                        await this.HandleValueCompletionAsync(arrFrame, output, requirements, asyncCaptures ??= []).ConfigureAwait(false);

                    frames.Pop();
                    cursor.PopContainer();
                    i++;

                    break;
                }

                case ':':
                    output.Append(ch);
                    cursor.OnColon();

                    i++;

                    break;

                case ',':
                    output.Append(ch);
                    cursor.OnComma();
                    i++;

                    break;

                case '"':
                    if (!JsonStringReader.TryReadString(json, i, out var strValue, out var rawLength))
                        return null;

                    if (cursor.TryPeek(out var frame) && frame.IsObject && frame.ExpectingProperty)
                    {
                        var path = cursor.BuildPropertyPath(strValue);
                        var newName = this.ApplyRename(path, strValue);

                        output.Append('"').Append(EscapeJsonString(newName)).Append('"');
                        cursor.OnPropertyNameRead(newName);
                    }
                    else
                    {
                        var path = cursor.GetValuePath();
                        var rendered = await this.RenderStringValueAsync(path, strValue).ConfigureAwait(false);

                        output.Append(rendered);
                        await this.HandleValueCompletionAsync(path, rendered, requirements, asyncCaptures ??= []).ConfigureAwait(false);
                        cursor.AdvanceAfterValue();
                    }

                    i += rawLength;

                    break;

                default:
                    var len = JsonLiteralReader.ReadLiteral(json.AsSpan(), i);

                    if (len == 0)
                        return null;

                    var valuePath = cursor.GetValuePath();
                    var literalText = json.Substring(i, len);
                    var renderedLiteral = this.RenderLiteral(valuePath, literalText);

                    output.Append(renderedLiteral);
                    await this.HandleValueCompletionAsync(valuePath, renderedLiteral, requirements, asyncCaptures ??= []).ConfigureAwait(false);
                    cursor.AdvanceAfterValue();
                    i += len;

                    break;
            }
        }

        if (cursor.PendingProperty is not null || frames.Count != 0 || cursor.FrameCount != 0)
            return null;

        this.ValidateRequirements(requirements);

        return new RewriteResult(output.ToString(), asyncCaptures);
    }

    private RequirementState[] CreateRequirementStates()
    {
        if (this.plan.RequireRules.Length == 0)
            return [];

        var result = new RequirementState[this.plan.RequireRules.Length];

        for (var i = 0; i < this.plan.RequireRules.Length; i++)
        {
            var rule = this.plan.RequireRules[i];
            result[i] = new(rule, this.IsEnabled(rule.RuleId));
        }

        return result;
    }

    private void ValidateRequirements(RequirementState[] requirements)
    {
        for (var i = 0; i < requirements.Length; i++)
        {
            var state = requirements[i];

            if (!state.Enabled)
                continue;

            if (!state.Found || state.PredicateFailed)
            {
                var message = state.Rule.ErrorMessage ?? "Required JSON node was not found or failed validation.";

                throw new FormatException(message);
            }
        }
    }

    private string ApplyRename(string path, string propertyName)
    {
        var rules = this.plan.RenamePropertyRules;

        for (var i = 0; i < rules.Length; i++)
        {
            var rule = rules[i];

            if (!PointerMatches(rule.Pointer, path))
                continue;

            if (!this.IsEnabled(rule.RuleId))
                continue;

            var start = this.StartRuleMetric(rule.RuleId);
            var renamed = rule.NewName;
            this.StopRuleMetric(rule.RuleId, start);

            return renamed;
        }

        return propertyName;
    }

    private string RenderStringValue(string path, string value) => this.RenderStringValueAsync(path, value).GetAwaiter().GetResult();

    private async ValueTask<string> RenderStringValueAsync(string path, string value)
    {
        var replaceRules = this.plan.ReplaceValueRules;

        for (var i = 0; i < replaceRules.Length; i++)
        {
            var rule = replaceRules[i];

            if (!PointerMatches(rule.Pointer, path))
                continue;

            if (!this.IsEnabled(rule.RuleId))
                continue;

            var start = this.StartRuleMetric(rule.RuleId);
            var replacement = Quote(EscapeJsonString(rule.Replacement));
            this.StopRuleMetric(rule.RuleId, start);

            return replacement;
        }

        var updated = value;
        var stringRules = this.plan.ReplaceInStringRules;

        for (var i = 0; i < stringRules.Length; i++)
        {
            var rule = stringRules[i];

            if (!this.IsEnabled(rule.RuleId))
                continue;

            var context = new JsonStringContext(path, updated);
            var apply = true;

            if (rule.Pointer is not null)
            {
                if (!PointerMatches(rule.Pointer!, path))
                    continue;

                if (rule.Predicate is not null && !rule.Predicate(context))
                    apply = false;

                if (apply && rule.PredicateAsync is not null)
                    apply = await rule.PredicateAsync(context).ConfigureAwait(false);
            }
            else
            {
                if (rule.Predicate is not null)
                    apply = rule.Predicate(context);

                if (apply && rule.PredicateAsync is not null)
                    apply = await rule.PredicateAsync(context).ConfigureAwait(false);
            }

            if (!apply)
                continue;

            var start = this.StartRuleMetric(rule.RuleId);

            if (rule.ReplacerAsync is not null)
                updated = await rule.ReplacerAsync(context).ConfigureAwait(false);
            else
                updated = rule.Replacer is not null ? rule.Replacer(context) : updated;

            this.StopRuleMetric(rule.RuleId, start);
        }

        return Quote(EscapeJsonString(updated));
    }

    private string RenderLiteral(string path, string literal)
    {
        var replaceRules = this.plan.ReplaceValueRules;

        for (var i = 0; i < replaceRules.Length; i++)
        {
            var rule = replaceRules[i];

            if (!PointerMatches(rule.Pointer, path))
                continue;

            if (!this.IsEnabled(rule.RuleId))
                continue;

            var start = this.StartRuleMetric(rule.RuleId);
            var rendered = Quote(EscapeJsonString(rule.Replacement));
            this.StopRuleMetric(rule.RuleId, start);

            return rendered;
        }

        return literal;
    }

    private void HandleValueCompletion(
        JsonContainerFrame frame,
        StringBuilder output,
        RequirementState[] requirements,
        ref List<PendingAsyncCapture>? asyncCaptures)
    {
        if (frame.ValuePath is null)
            return;

        var literal = output.ToString(frame.OutputStart, output.Length - frame.OutputStart);
        this.HandleValueCompletion(frame.ValuePath, literal, requirements, ref asyncCaptures);
    }

    private ValueTask HandleValueCompletionAsync(
        JsonContainerFrame frame,
        StringBuilder output,
        RequirementState[] requirements,
        List<PendingAsyncCapture>? asyncCaptures)
    {
        if (frame.ValuePath is null)
            return ValueTask.CompletedTask;

        var literal = output.ToString(frame.OutputStart, output.Length - frame.OutputStart);

        return this.HandleValueCompletionAsync(frame.ValuePath, literal, requirements, asyncCaptures);
    }

    private void HandleValueCompletion(string path, string literal, RequirementState[] requirements, ref List<PendingAsyncCapture>? asyncCaptures)
    {
        for (var i = 0; i < requirements.Length; i++)
        {
            ref var req = ref requirements[i];

            if (!req.Enabled)
                continue;

            if (!PointerMatches(req.Rule.Pointer, path))
                continue;

            var start = this.StartRuleMetric(req.Rule.RuleId);
            req.Mark(literal);
            this.StopRuleMetric(req.Rule.RuleId, start);
        }

        var captureRules = this.plan.CaptureRules;

        for (var i = 0; i < captureRules.Length; i++)
        {
            var rule = captureRules[i];

            if (!PointerMatches(rule.Pointer, path))
                continue;

            if (!this.IsEnabled(rule.RuleId))
                continue;

            var start = this.StartRuleMetric(rule.RuleId);
            rule.OnValue(literal);
            this.StopRuleMetric(rule.RuleId, start);
        }

        var projectionRules = this.plan.ProjectionCaptureRules;

        for (var i = 0; i < projectionRules.Length; i++)
        {
            var rule = projectionRules[i];

            if (!PointerMatches(rule.Pointer, path))
                continue;

            if (!this.IsEnabled(rule.RuleId))
                continue;

            var start = this.StartRuleMetric(rule.RuleId);
            rule.Capture(literal);
            this.StopRuleMetric(rule.RuleId, start);
        }

        var asyncRules = this.plan.CaptureAsyncRules;

        if (asyncRules.Length != 0)
        {
            for (var i = 0; i < asyncRules.Length; i++)
            {
                var rule = asyncRules[i];

                if (!PointerMatches(rule.Pointer, path))
                    continue;

                if (!this.IsEnabled(rule.RuleId))
                    continue;

                asyncCaptures ??= [];
                asyncCaptures.Add(new(rule, literal));
            }
        }
    }

    private async ValueTask HandleValueCompletionAsync(
        string path,
        string literal,
        RequirementState[] requirements,
        List<PendingAsyncCapture>? asyncCaptures)
    {
        for (var i = 0; i < requirements.Length; i++)
        {
            var req = requirements[i];

            if (!req.Enabled)
                continue;

            if (!PointerMatches(req.Rule.Pointer, path))
                continue;

            var start = this.StartRuleMetric(req.Rule.RuleId);
            await req.MarkAsync(literal).ConfigureAwait(false);
            this.StopRuleMetric(req.Rule.RuleId, start);
        }

        var captureRules = this.plan.CaptureRules;

        for (var i = 0; i < captureRules.Length; i++)
        {
            var rule = captureRules[i];

            if (!PointerMatches(rule.Pointer, path))
                continue;

            if (!this.IsEnabled(rule.RuleId))
                continue;

            var start = this.StartRuleMetric(rule.RuleId);
            rule.OnValue(literal);
            this.StopRuleMetric(rule.RuleId, start);
        }

        var projectionRules = this.plan.ProjectionCaptureRules;

        for (var i = 0; i < projectionRules.Length; i++)
        {
            var rule = projectionRules[i];

            if (!PointerMatches(rule.Pointer, path))
                continue;

            if (!this.IsEnabled(rule.RuleId))
                continue;

            var start = this.StartRuleMetric(rule.RuleId);
            rule.Capture(literal);
            this.StopRuleMetric(rule.RuleId, start);
        }

        var asyncRules = this.plan.CaptureAsyncRules;

        if (asyncRules.Length != 0)
        {
            for (var i = 0; i < asyncRules.Length; i++)
            {
                var rule = asyncRules[i];

                if (!PointerMatches(rule.Pointer, path))
                    continue;

                if (!this.IsEnabled(rule.RuleId))
                    continue;

                asyncCaptures ??= [];
                asyncCaptures.Add(new(rule, literal));
            }
        }
    }

    private static bool PointerMatches(string? rulePointer, string path)
    {
        if (rulePointer is null)
            return false;

        if (rulePointer.IndexOf('*') < 0)
            return string.Equals(rulePointer, path, StringComparison.Ordinal);

        var ruleSegments = SplitPointer(rulePointer);
        var pathSegments = SplitPointer(path);

        if (ruleSegments.Length != pathSegments.Length)
            return false;

        for (var i = 0; i < ruleSegments.Length; i++)
        {
            var rp = ruleSegments[i];

            if (rp == "*")
                continue;

            if (!string.Equals(rp, pathSegments[i], StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    private static string[] SplitPointer(string pointer) =>
        pointer.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private void ClearBuffer() => this.buffer.Clear();

    private static string EscapeJsonString(string value)
    {
        var builder = new StringBuilder(value.Length + 8);

        foreach (var ch in value)
        {
            switch (ch)
            {
                case '\\':
                    builder.Append("\\\\");

                    break;
                case '"':
                    builder.Append("\\\"");

                    break;
                case '\b':
                    builder.Append("\\b");

                    break;
                case '\f':
                    builder.Append("\\f");

                    break;
                case '\n':
                    builder.Append("\\n");

                    break;
                case '\r':
                    builder.Append("\\r");

                    break;
                case '\t':
                    builder.Append("\\t");

                    break;
                default:
                    if (ch < ' ')
                        builder.Append("\\u").Append(((int)ch).ToString("X4", CultureInfo.InvariantCulture));
                    else
                        builder.Append(ch);

                    break;
            }
        }

        return builder.ToString();
    }

    private static string Quote(string value) => string.Create(
        value.Length + 2,
        value,
        static (span, val) =>
        {
            span[0] = '"';
            val.AsSpan().CopyTo(span[1..]);
            span[^1] = '"';
        });

    private static bool TryPeek(Stack<JsonContainerFrame> frames, out JsonContainerFrame frame)
    {
        if (frames.Count > 0)
        {
            frame = frames.Peek();

            return true;
        }

        frame = default;

        return false;
    }

    private void RunPendingCaptures(List<PendingAsyncCapture>? asyncCaptures)
    {
        if (asyncCaptures is null || asyncCaptures.Count == 0)
            return;

        foreach (var capture in asyncCaptures)
        {
            var start = this.StartRuleMetric(capture.Rule.RuleId);
            capture.Rule.OnValueAsync(capture.Literal).GetAwaiter().GetResult();
            this.StopRuleMetric(capture.Rule.RuleId, start);
        }
    }

    private async ValueTask RunPendingCapturesAsync(List<PendingAsyncCapture>? asyncCaptures)
    {
        if (asyncCaptures is null || asyncCaptures.Count == 0)
            return;

        for (var i = 0; i < asyncCaptures.Count; i++)
        {
            var capture = asyncCaptures[i];
            var start = this.StartRuleMetric(capture.Rule.RuleId);
            await capture.Rule.OnValueAsync(capture.Literal).ConfigureAwait(false);
            this.StopRuleMetric(capture.Rule.RuleId, start);
        }
    }

    private bool IsEnabled(int ruleId)
    {
        if (this.enabledGroups is not null)
        {
            var group = this.GetGroup(ruleId);

            if (group is not null && !this.enabledGroups.Contains(group))
                return false;
        }

        if (this.ruleGate is not null && !this.ruleGate(ruleId))
            return false;

        return true;
    }

    private string? GetGroup(int ruleId)
    {
        if (ruleId < this.options.RuleGroups.Length)
            return this.options.RuleGroups[ruleId];

        if (ruleId < this.plan.RuleGroups.Length)
            return this.plan.RuleGroups[ruleId];

        if (ruleId < this.plan.Rules.Length)
            return this.plan.Rules[ruleId].Group;

        return null;
    }

    private long StartRuleMetric(int ruleId)
    {
        if (!this.collectRuleMetrics || this.ruleHits is null || this.ruleElapsedTicks is null)
            return 0;

        this.ruleHits[ruleId]++;

        return Stopwatch.GetTimestamp();
    }

    private void StopRuleMetric(int ruleId, long startTicks)
    {
        if (!this.collectRuleMetrics || this.ruleElapsedTicks is null)
            return;

        if (startTicks == 0)
            return;

        this.ruleElapsedTicks[ruleId] += Stopwatch.GetTimestamp() - startTicks;
    }

    private void PublishRuleMetrics()
    {
        if (!this.collectRuleMetrics || this.ruleHits is null || this.ruleElapsedTicks is null)
            return;

        if (this.options.OnRuleMetrics is null && this.options.OnRuleMetricsAsync is null)
            return;

        var frequency = Stopwatch.Frequency;
        var list = new List<RuleStat>(this.plan.RuleCount);

        for (var i = 0; i < this.plan.RuleCount; i++)
        {
            var hits = this.ruleHits[i];
            var elapsed = this.ruleElapsedTicks[i];

            if (hits == 0 && elapsed == 0)
                continue;

            var name = i < this.options.RuleNames.Length ? this.options.RuleNames[i] : null;
            var group = i < this.options.RuleGroups.Length ? this.options.RuleGroups[i] : null;
            list.Add(new(i, name, group, hits, TimeSpan.FromSeconds(elapsed / (double)frequency)));
        }

        if (list.Count == 0)
            return;

        if (this.options.OnRuleMetricsAsync is not null)
            _ = this.options.OnRuleMetricsAsync(list);

        this.options.OnRuleMetrics?.Invoke(list);
    }

    private async ValueTask PublishRuleMetricsAsync()
    {
        if (!this.collectRuleMetrics || this.ruleHits is null || this.ruleElapsedTicks is null)
            return;

        if (this.options.OnRuleMetrics is null && this.options.OnRuleMetricsAsync is null)
            return;

        var frequency = Stopwatch.Frequency;
        var list = new List<RuleStat>(this.plan.RuleCount);

        for (var i = 0; i < this.plan.RuleCount; i++)
        {
            var hits = this.ruleHits[i];
            var elapsed = this.ruleElapsedTicks[i];

            if (hits == 0 && elapsed == 0)
                continue;

            var name = i < this.options.RuleNames.Length ? this.options.RuleNames[i] : null;
            var group = i < this.options.RuleGroups.Length ? this.options.RuleGroups[i] : null;
            list.Add(new(i, name, group, hits, TimeSpan.FromSeconds(elapsed / (double)frequency)));
        }

        if (list.Count == 0)
            return;

        if (this.options.OnRuleMetricsAsync is not null)
            await this.options.OnRuleMetricsAsync(list).ConfigureAwait(false);
        else
            this.options.OnRuleMetrics?.Invoke(list);
    }

    private struct RequirementState(RequireRule rule, bool enabled)
    {
        public RequireRule Rule { get; } = rule;

        public bool Enabled { get; } = enabled;

        public bool Found { get; private set; }

        public bool PredicateFailed { get; private set; }

        public void Mark(string literal)
        {
            if (!this.Enabled)
                return;

            this.Found = true;

            if (this.Rule.PredicateAsync is not null)
            {
                if (!this.Rule.PredicateAsync(literal).GetAwaiter().GetResult())
                    this.PredicateFailed = true;
            }
            else if (this.Rule.Predicate is not null && !this.Rule.Predicate(literal))
                this.PredicateFailed = true;
        }

        public async ValueTask MarkAsync(string literal)
        {
            if (!this.Enabled)
                return;

            this.Found = true;

            if (this.Rule.PredicateAsync is not null)
            {
                if (!await this.Rule.PredicateAsync(literal).ConfigureAwait(false))
                    this.PredicateFailed = true;
            }
            else if (this.Rule.Predicate is not null && !this.Rule.Predicate(literal))
                this.PredicateFailed = true;
        }
    }

    private readonly struct PendingAsyncCapture(CaptureAsyncRule rule, string literal)
    {
        public CaptureAsyncRule Rule { get; } = rule;

        public string Literal { get; } = literal;
    }

    private struct JsonContainerFrame(bool isArray, string? valuePath, int outputStart)
    {
        public bool IsArray { get; } = isArray;

        public bool IsObject => !this.IsArray;

        public string? ValuePath { get; } = valuePath;

        public int OutputStart { get; } = outputStart;
    }

    private readonly record struct RewriteResult(string Text, List<PendingAsyncCapture>? AsyncCaptures)
    {
        public static RewriteResult Empty { get; } = new(string.Empty, null);
    }
}
