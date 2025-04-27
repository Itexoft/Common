// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Collections;

namespace Itexoft.TerminalKit.ObjectExplorer;

internal static class TerminalExplorerValueFormatter
{
    public static string Format(object? value)
    {
        if (value == null)
            return "null";

        if (value is string text)
            return text;

        if (value is IEnumerable enumerable and not string)
            return $"Collection ({GetCount(enumerable)} items)";

        if (value is DateTime dateTime)
            return dateTime.ToString("u");

        if (value is DateOnly dateOnly)
            return dateOnly.ToString("O");

        return value.ToString() ?? value.GetType().Name;
    }

    internal static int GetCount(IEnumerable enumerable)
    {
        if (enumerable is ICollection collection)
            return collection.Count;

        var count = 0;
        foreach (var _ in enumerable)
            count++;

        return count;
    }
}