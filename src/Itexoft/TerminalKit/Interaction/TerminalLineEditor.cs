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

        void render()
        {
            Console.SetCursorPosition(startLeft, startTop);
            var text = buffer.ToString();
            Console.Write(text);

            if (lastLength > text.Length)
                Console.Write(new string(' ', lastLength - text.Length));

            lastLength = text.Length;
            var width = getBufferWidth();
            var offset = startLeft + position;
            var targetTop = startTop + offset / width;
            var targetLeft = offset % width;
            Console.SetCursorPosition(targetLeft, targetTop);
        }

        render();

        while (true)
        {
            var key = Console.ReadKey(intercept: true);

            switch (key.Key)
            {
                case ConsoleKey.Enter:
                    moveToNextLine();
                    cancelled = false;

                    return buffer.ToString();
                case ConsoleKey.Escape when allowCancel:
                    moveToNextLine();
                    cancelled = true;

                    return null;
                case ConsoleKey.Backspace:
                    if (position > 0)
                    {
                        buffer.Remove(position - 1, 1);
                        position--;
                        render();
                    }

                    break;
                case ConsoleKey.Delete:
                    if (position < buffer.Length)
                    {
                        buffer.Remove(position, 1);
                        render();
                    }

                    break;
                case ConsoleKey.LeftArrow:
                    if (position > 0)
                    {
                        position--;
                        render();
                    }

                    break;
                case ConsoleKey.RightArrow:
                    if (position < buffer.Length)
                    {
                        position++;
                        render();
                    }

                    break;
                case ConsoleKey.Home:
                    position = 0;
                    render();

                    break;
                case ConsoleKey.End:
                    position = buffer.Length;
                    render();

                    break;
                default:
                    if (!char.IsControl(key.KeyChar))
                    {
                        buffer.Insert(position, key.KeyChar);
                        position++;
                        render();
                    }

                    break;
            }
        }

        void moveToNextLine()
        {
            var width = getBufferWidth();
            Console.SetCursorPosition(0, startTop + (startLeft + lastLength) / width + 1);
            Console.WriteLine();
        }

        int getBufferWidth() => TerminalDimensions.GetBufferWidthOrDefault(1);
    }
}
