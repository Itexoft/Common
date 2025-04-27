// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Globalization;
using System.Text;

namespace Itexoft.Text.Rewriting.Json;

internal sealed class JsonPathCursor
{
    private readonly Stack<PathFrame> frames = new();
    private readonly List<string> segments = [];

    internal string? PendingProperty { get; set; }

    internal int FrameCount => this.frames.Count;

    internal void PushContainer(bool isArray)
    {
        if (this.PendingProperty is not null)
        {
            this.segments.Add(this.PendingProperty);
            this.PendingProperty = null;
        }
        else if (this.TryPeek(out var parent) && parent.IsArray)
            this.segments.Add(parent.Index.ToString(CultureInfo.InvariantCulture));

        this.frames.Push(new(isArray));
    }

    internal void PopContainer()
    {
        if (this.frames.Count == 0)
            return;

        this.frames.Pop();

        if (this.segments.Count > 0)
            this.segments.RemoveAt(this.segments.Count - 1);

        if (this.TryPeek(out var parent))
        {
            if (parent.IsArray)
            {
                parent.Index++;
                this.ReplaceTop(parent);
            }
            else
            {
                parent.ExpectingProperty = true;
                this.ReplaceTop(parent);
            }
        }
    }

    internal void OnComma()
    {
        if (this.TryPeek(out var frame) && frame.IsObject)
        {
            frame.ExpectingProperty = true;

            this.ReplaceTop(frame);
        }

        this.PendingProperty = null;
    }

    internal void OnColon()
    {
        if (this.TryPeek(out var frame) && frame.IsObject)
        {
            frame.ExpectingProperty = false;
            this.ReplaceTop(frame);
        }
    }

    internal void OnPropertyNameRead(string property)
    {
        this.PendingProperty = property;

        if (this.TryPeek(out var frame) && frame.IsObject)
        {
            frame.ExpectingProperty = false;
            this.ReplaceTop(frame);
        }
    }

    internal void AdvanceAfterValue()
    {
        if (this.PendingProperty is not null)
        {
            this.PendingProperty = null;

            return;
        }

        if (this.TryPeek(out var frame) && frame.IsArray)
        {
            frame.Index++;
            this.ReplaceTop(frame);
        }
    }

    internal string GetValuePath()
    {
        string leaf;

        if (this.PendingProperty is not null)
            leaf = this.PendingProperty;
        else if (this.TryPeek(out var frame) && frame.IsArray)
            leaf = frame.Index.ToString(CultureInfo.InvariantCulture);
        else
            leaf = string.Empty;

        return BuildPath(this.segments, null, leaf);
    }

    internal string BuildPropertyPath(string property)
    {
        string? arraySegment = null;

        if (this.TryPeek(out var frame) && frame.IsArray)
            arraySegment = frame.Index.ToString(CultureInfo.InvariantCulture);

        return BuildPath(this.segments, arraySegment, property);
    }

    internal bool TryPeek(out PathFrame frame)
    {
        if (this.frames.Count > 0)
        {
            frame = this.frames.Peek();

            return true;
        }

        frame = default;

        return false;
    }

    private void ReplaceTop(PathFrame frame)
    {
        this.frames.Pop();
        this.frames.Push(frame);
    }

    private static string BuildPath(List<string> segments, string? midSegment, string leaf)
    {
        var builder = new StringBuilder();

        for (var i = 0; i < segments.Count; i++)
            builder.Append('/').Append(segments[i]);

        if (!string.IsNullOrEmpty(midSegment))
            builder.Append('/').Append(midSegment);

        builder.Append('/').Append(leaf);

        return builder.ToString();
    }
}

internal struct PathFrame(bool isArray)
{
    internal bool IsArray { get; } = isArray;

    internal bool IsObject => !this.IsArray;

    internal int Index { get; set; }

    internal bool ExpectingProperty { get; set; } = !isArray;
}
