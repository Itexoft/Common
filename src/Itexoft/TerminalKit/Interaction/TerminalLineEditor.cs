// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Text;
using Itexoft.TerminalKit.Rendering;

namespace Itexoft.TerminalKit.Interaction;

internal static class TerminalLineEditor
{
    public static string? ReadLine(string? initial, bool allowCancel, out bool cancelled)
    {
        var buffer = new StringBuilder(initial ?? string.Empty);
        var position = buffer.Length;
        var startLeft = Console.CursorLeft;
        var startTop = Console.CursorTop;
        var lastLength = buffer.Length;

        void Render()
        {
            Console.SetCursorPosition(startLeft, startTop);
            var text = buffer.ToString();
            Console.Write(text);
            if (lastLength > text.Length)
                Console.Write(new string(' ', lastLength - text.Length));

            lastLength = text.Length;
            var width = GetBufferWidth();
            var offset = startLeft + position;
            var targetTop = startTop + offset / width;
            var targetLeft = offset % width;
            Console.SetCursorPosition(targetLeft, targetTop);
        }

        Render();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            switch (key.Key)
            {
                case ConsoleKey.Enter:
                    MoveToNextLine();
                    cancelled = false;

                    return buffer.ToString();
                case ConsoleKey.Escape when allowCancel:
                    MoveToNextLine();
                    cancelled = true;

                    return null;
                case ConsoleKey.Backspace:
                    if (position > 0)
                    {
                        buffer.Remove(position - 1, 1);
                        position--;
                        Render();
                    }

                    break;
                case ConsoleKey.Delete:
                    if (position < buffer.Length)
                    {
                        buffer.Remove(position, 1);
                        Render();
                    }

                    break;
                case ConsoleKey.LeftArrow:
                    if (position > 0)
                    {
                        position--;
                        Render();
                    }

                    break;
                case ConsoleKey.RightArrow:
                    if (position < buffer.Length)
                    {
                        position++;
                        Render();
                    }

                    break;
                case ConsoleKey.Home:
                    position = 0;
                    Render();

                    break;
                case ConsoleKey.End:
                    position = buffer.Length;
                    Render();

                    break;
                default:
                    if (!char.IsControl(key.KeyChar))
                    {
                        buffer.Insert(position, key.KeyChar);
                        position++;
                        Render();
                    }

                    break;
            }
        }

        void MoveToNextLine()
        {
            var width = GetBufferWidth();
            Console.SetCursorPosition(0, startTop + (startLeft + lastLength) / width + 1);
            Console.WriteLine();
        }

        int GetBufferWidth() => TerminalDimensions.GetBufferWidthOrDefault(1);
    }
}