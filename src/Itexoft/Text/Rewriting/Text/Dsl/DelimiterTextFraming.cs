// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Text.Rewriting.Text.Dsl;

/// <summary>
/// Frames text using a delimiter sequence.
/// </summary>
public sealed class DelimiterTextFraming : ITextFraming
{
    private readonly string delimiter;
    private readonly bool includeDelimiter;

    public DelimiterTextFraming(string delimiter, bool includeDelimiter = false)
    {
        if (string.IsNullOrEmpty(delimiter))
            throw new ArgumentException("Delimiter must be non-empty.", nameof(delimiter));

        this.delimiter = delimiter;
        this.includeDelimiter = includeDelimiter;
    }

    public bool TryCutFrame(ref ReadOnlySpan<char> buffer, out ReadOnlySpan<char> frame)
    {
        var idx = buffer.IndexOf(this.delimiter.AsSpan());

        if (idx < 0)
        {
            frame = default;

            return false;
        }

        var payloadLength = this.includeDelimiter ? idx + this.delimiter.Length : idx;
        frame = buffer[..payloadLength];
        buffer = buffer[(idx + this.delimiter.Length)..];

        return true;
    }

    public bool TryCutFrame(ref ReadOnlyMemory<char> buffer, out ReadOnlyMemory<char> frame)
    {
        var idx = buffer.Span.IndexOf(this.delimiter.AsSpan());

        if (idx < 0)
        {
            frame = default;

            return false;
        }

        var payloadLength = this.includeDelimiter ? idx + this.delimiter.Length : idx;
        frame = buffer[..payloadLength];
        buffer = buffer[(idx + this.delimiter.Length)..];

        return true;
    }
}
