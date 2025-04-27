// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Text;

namespace Itexoft.Text.Rewriting.Json;

internal static class JsonPropertyNameHelper
{
    internal static string Convert(string name, JsonPropertyNameStyle style) => style switch
    {
        JsonPropertyNameStyle.Exact => name,
        JsonPropertyNameStyle.CamelCase => ConvertToCamelCase(name),
        JsonPropertyNameStyle.SnakeCase => ConvertToSnakeCase(name),
        _ => name,
    };

    private static string ConvertToCamelCase(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;

        if (char.IsLower(name[0]))
            return name;

        if (name.Length == 1)
            return name.ToLowerInvariant();

        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    private static string ConvertToSnakeCase(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;

        var builder = new StringBuilder(name.Length + 8);
        char? previous = null;

        for (var i = 0; i < name.Length; i++)
        {
            var ch = name[i];
            var isUpper = char.IsUpper(ch);

            if (isUpper)
            {
                var hasPrevious = i > 0;
                var previousIsLower = previous.HasValue && char.IsLower(previous.Value);
                var nextIsLower = i + 1 < name.Length && char.IsLower(name[i + 1]);
                var previousIsSeparator = previous == '_';

                if (hasPrevious && !previousIsSeparator && (previousIsLower || nextIsLower))
                    builder.Append('_');

                builder.Append(char.ToLowerInvariant(ch));
            }
            else
                builder.Append(char.ToLowerInvariant(ch));

            previous = ch;
        }

        return builder.ToString();
    }
}
