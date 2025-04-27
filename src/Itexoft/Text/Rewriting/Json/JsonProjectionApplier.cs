// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Extensions;

namespace Itexoft.Text.Rewriting.Json;

internal static class JsonProjectionApplier
{
    internal static T Project<T>(string json, JsonProjectionPlan<T> plan) where T : class
    {
        json.Required();
        plan.Required();

        var target = plan.CreateInstance();

        var parser = new ProjectionParser<T>(json, plan, target);
        parser.Parse();

        return target;
    }

    internal static IEnumerable<T> ProjectMany<T>(string json, JsonProjectionPlan<T> plan) where T : class
    {
        json.Required();
        plan.Required();

        var trimmed = json.Trim();

        if (trimmed.Length == 0)
            yield break;

        if (trimmed[0] != '[')
        {
            yield return Project(trimmed, plan);

            yield break;
        }

        foreach (var element in EnumerateArrayElements(trimmed))
            yield return Project(element, plan);
    }

    private static IEnumerable<string> EnumerateArrayElements(string json)
    {
        var i = 1;
        var length = json.Length;

        while (i < length)
        {
            while (i < length && char.IsWhiteSpace(json[i]))
                i++;

            if (i >= length)
                throw new FormatException("Invalid JSON input.");

            if (json[i] == ']')
                yield break;

            var valueLength = ReadValueLength(json, i);

            if (valueLength <= 0)
                throw new FormatException("Invalid JSON input.");

            yield return json.Substring(i, valueLength);

            i += valueLength;

            while (i < length && char.IsWhiteSpace(json[i]))
                i++;

            if (i < length && json[i] == ',')
                i++;
        }

        throw new FormatException("Invalid JSON input.");
    }

    private static int ReadValueLength(string json, int start)
    {
        var ch = json[start];

        switch (ch)
        {
            case '{':
            case '[':
                return ReadContainerLength(json, start, ch == '{' ? '}' : ']');
            case '"':
                return JsonStringReader.TryReadString(json, start, out _, out var rawLength) ? rawLength : 0;
            default:
                return JsonLiteralReader.ReadLiteral(json.AsSpan(), start);
        }
    }

    private static int ReadContainerLength(string json, int start, char closing)
    {
        var depth = 0;
        var inString = false;
        var escaped = false;

        for (var i = start; i < json.Length; i++)
        {
            var ch = json[i];

            if (inString)
            {
                if (escaped)
                {
                    escaped = false;

                    continue;
                }

                if (ch == '\\')
                {
                    escaped = true;

                    continue;
                }

                if (ch == '"')
                    inString = false;

                continue;
            }

            if (ch == '"')
            {
                inString = true;

                continue;
            }

            if (ch == '{' || ch == '[')
                depth++;
            else if (ch == '}' || ch == ']')
            {
                depth--;

                if (depth == 0 && ch == closing)
                    return i - start + 1;
            }
        }

        return 0;
    }

    private static bool TryPeek(Stack<ProjectionFrame> frames, out ProjectionFrame frame)
    {
        if (frames.Count > 0)
        {
            frame = frames.Peek();

            return true;
        }

        frame = default;

        return false;
    }

    private sealed class ProjectionParser<T>(string json, JsonProjectionPlan<T> plan, T target) where T : class
    {
        private readonly JsonPathCursor cursor = new();
        private readonly Stack<ProjectionFrame> frames = new();

        public void Parse()
        {
            var span = json.AsSpan();
            var i = 0;

            while (i < span.Length)
            {
                var ch = span[i];

                if (char.IsWhiteSpace(ch))
                {
                    i++;

                    continue;
                }

                switch (ch)
                {
                    case '{':
                    {
                        var path = this.cursor.GetValuePath();
                        this.frames.Push(new(false, path, i));
                        this.cursor.PushContainer(false);
                        i++;

                        break;
                    }

                    case '}':
                    {
                        if (!TryPeek(this.frames, out var frame) || frame.IsArray)
                            throw new FormatException("Invalid JSON input.");

                        var literal = json.Substring(frame.OutputStart, i - frame.OutputStart + 1);
                        this.HandleValue(frame.ValuePath, literal, false);
                        this.frames.Pop();
                        this.cursor.PopContainer();
                        i++;

                        break;
                    }

                    case '[':
                    {
                        var path = this.cursor.GetValuePath();
                        this.frames.Push(new(true, path, i));
                        this.cursor.PushContainer(true);
                        i++;

                        break;
                    }

                    case ']':
                    {
                        if (!TryPeek(this.frames, out var frame) || !frame.IsArray)
                            throw new FormatException("Invalid JSON input.");

                        var literal = json.Substring(frame.OutputStart, i - frame.OutputStart + 1);
                        this.HandleValue(frame.ValuePath, literal, false);
                        this.frames.Pop();
                        this.cursor.PopContainer();
                        i++;

                        break;
                    }

                    case ':':
                    {
                        this.cursor.OnColon();
                        i++;

                        break;
                    }

                    case ',':
                    {
                        this.cursor.OnComma();
                        i++;

                        break;
                    }

                    case '"':
                    {
                        if (!JsonStringReader.TryReadString(json, i, out var value, out var rawLength))
                            throw new FormatException("Invalid JSON input.");

                        if (this.cursor.TryPeek(out var frame) && frame.IsObject && frame.ExpectingProperty)
                            this.cursor.OnPropertyNameRead(value);
                        else
                        {
                            var path = this.cursor.GetValuePath();
                            this.HandleValue(path, value, true);
                            this.cursor.AdvanceAfterValue();
                        }

                        i += rawLength;

                        break;
                    }

                    default:
                    {
                        var len = JsonLiteralReader.ReadLiteral(span, i);

                        if (len == 0)
                            throw new FormatException("Invalid JSON input.");

                        var literal = json.Substring(i, len);
                        var path = this.cursor.GetValuePath();
                        this.HandleValue(path, literal, false);
                        this.cursor.AdvanceAfterValue();
                        i += len;

                        break;
                    }
                }
            }

            if (this.cursor.PendingProperty is not null || this.frames.Count != 0 || this.cursor.FrameCount != 0)
                throw new FormatException("Invalid JSON input.");
        }

        private void HandleValue(string path, string literal, bool isString)
        {
            if (!plan.TryAssign(target, path, new(literal, isString)))
                return;
        }
    }

    private struct ProjectionFrame(bool isArray, string valuePath, int outputStart)
    {
        public bool IsArray { get; } = isArray;

        public bool IsObject => !this.IsArray;

        public string ValuePath { get; } = valuePath;

        public int OutputStart { get; } = outputStart;
    }
}
