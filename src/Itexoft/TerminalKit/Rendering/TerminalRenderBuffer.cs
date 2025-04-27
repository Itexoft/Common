// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.TerminalKit.Rendering;

internal sealed class TerminalRenderBuffer
{
    private readonly List<RenderLine> _lines = [];
    private RenderLine _currentLine = new();
    private int _lastLineCount;

    public void Reset()
    {
        this._lines.Clear();
        this._currentLine = new();
    }

    public void Write(string text) => this.WriteStyled(text ?? string.Empty, null);

    public void WriteLine(string text)
    {
        this.WriteStyled(text ?? string.Empty, null);
        this.CommitLine();
    }

    public void WriteLine() => this.CommitLine();

    public void WriteStyled(string text, TerminalCellStyle? style) => this._currentLine.Add(new(text ?? string.Empty, style));

    public void WriteStyledLine(string text, TerminalCellStyle? style, bool fillToWidth = false)
    {
        var content = text ?? string.Empty;
        if (fillToWidth)
        {
            var width = GetConsoleWidth();
            content = content.Length > width ? content[..width] : content;
            this._currentLine.Add(new(content, style));
            var remaining = width - content.Length;
            if (remaining > 0)
                this._currentLine.Add(new(new(' ', remaining), style));
        }
        else
        {
            this._currentLine.Add(new(content, style));
        }

        this.CommitLine();
    }

    public void Flush()
    {
        this.CommitPendingLine();

        var originalForeground = Console.ForegroundColor;
        var originalBackground = Console.BackgroundColor;
        var width = GetConsoleWidth();
        var blankLine = new string(' ', width);

        ClearSurface();
        var lineCount = 0;
        foreach (var line in this._lines)
        {
            var written = 0;
            foreach (var segment in line.Segments)
            {
                ApplyStyle(segment.Style, originalForeground, originalBackground);
                Console.Write(segment.Text);
                written += segment.Text.Length;
            }

            RestoreColors(originalForeground, originalBackground);

            if (written < width)
                Console.Write(blankLine[..(width - written)]);

            Console.WriteLine();
            lineCount++;
        }

        for (var i = lineCount; i < this._lastLineCount; i++)
        {
            Console.Write(blankLine);
            Console.WriteLine();
        }

        this._lastLineCount = lineCount;
        Console.SetCursorPosition(0, 0);
        Console.CursorVisible = false;
    }

    private static void ClearSurface()
    {
        try
        {
            Console.Write("\u001b[2J\u001b[3J\u001b[H");
        }
        catch
        {
            Console.Clear();
        }
    }

    private void CommitLine()
    {
        this._lines.Add(this._currentLine);
        this._currentLine = new();
    }

    private void CommitPendingLine()
    {
        if (this._currentLine.HasSegments)
            this.CommitLine();
    }

    private static void ApplyStyle(TerminalCellStyle? style, ConsoleColor fallbackForeground, ConsoleColor fallbackBackground)
    {
        Console.ForegroundColor = style?.Foreground ?? fallbackForeground;
        Console.BackgroundColor = style?.Background ?? fallbackBackground;
    }

    private static void RestoreColors(ConsoleColor foreground, ConsoleColor background)
    {
        Console.ForegroundColor = foreground;
        Console.BackgroundColor = background;
    }

    private static int GetConsoleWidth() => TerminalDimensions.GetBufferWidthOrDefault();

    private sealed class RenderLine
    {
        private readonly List<RenderSegment> _segments = [];

        public IReadOnlyList<RenderSegment> Segments => this._segments;

        public bool HasSegments => this._segments.Count > 0;

        public void Add(RenderSegment segment) => this._segments.Add(segment);
    }

    private readonly record struct RenderSegment(string Text, TerminalCellStyle? Style);
}