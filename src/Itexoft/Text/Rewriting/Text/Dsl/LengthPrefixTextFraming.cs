// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Globalization;

namespace Itexoft.Text.Rewriting.Text.Dsl;

/// <summary>
/// Frames text using decimal length prefixes in the form &lt;length&gt;:&lt;payload&gt;.
/// </summary>
public sealed class LengthPrefixTextFraming : ITextFraming
{
    public bool TryCutFrame(ref ReadOnlySpan<char> buffer, out ReadOnlySpan<char> frame)
    {
        if (!this.TryReadLength(buffer, out var length, out var consumed))
        {
            frame = default;

            return false;
        }

        if (buffer.Length < consumed + length)
        {
            frame = default;

            return false;
        }

        frame = buffer.Slice(consumed, length);
        buffer = buffer[(consumed + length)..];

        return true;
    }

    public bool TryCutFrame(ref ReadOnlyMemory<char> buffer, out ReadOnlyMemory<char> frame)
    {
        if (!this.TryReadLength(buffer.Span, out var length, out var consumed))
        {
            frame = default;

            return false;
        }

        if (buffer.Length < consumed + length)
        {
            frame = default;

            return false;
        }

        frame = buffer.Slice(consumed, length);
        buffer = buffer[(consumed + length)..];

        return true;
    }

    private bool TryReadLength(ReadOnlySpan<char> buffer, out int length, out int consumed)
    {
        length = 0;
        consumed = 0;

        var idx = buffer.IndexOf(':');

        if (idx < 0)
            return false;

        if (!int.TryParse(buffer[..idx], NumberStyles.None, CultureInfo.InvariantCulture, out length))
            return false;

        consumed = idx + 1;

        return true;
    }
}
