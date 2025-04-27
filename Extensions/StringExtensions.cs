using System.Text;

namespace Itexoft.Common;

public static class StringExtensions
{
    public static string ToKebabCase(this string value)
    {
        if (string.IsNullOrEmpty(value)) return value;

        var sb = new StringBuilder(value.Length * 2);

        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];

            if (char.IsUpper(c))
            {
                if (i > 0) sb.Append('-');

                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }
}