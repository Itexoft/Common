// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.TerminalKit.Rendering;

/// <summary>
/// Safe accessors for console dimensions that fall back to sensible defaults when unavailable.
/// </summary>
internal static class TerminalDimensions
{
    private const int DefaultWidth = 120;
    private const int DefaultHeight = 30;

    public static int GetWindowWidthOrDefault(int fallback = DefaultWidth)
    {
        try
        {
            var width = Console.WindowWidth;

            return width > 0 ? width : fallback;
        }
        catch
        {
            return fallback;
        }
    }

    public static int GetWindowHeightOrDefault(int fallback = DefaultHeight)
    {
        try
        {
            var height = Console.WindowHeight;

            return height > 0 ? height : fallback;
        }
        catch
        {
            return fallback;
        }
    }

    public static int GetBufferWidthOrDefault(int fallback = DefaultWidth)
    {
        try
        {
            var width = Console.BufferWidth;

            return width > 0 ? width : fallback;
        }
        catch
        {
            return fallback;
        }
    }
}