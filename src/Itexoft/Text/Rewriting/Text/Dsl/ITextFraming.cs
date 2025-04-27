// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Text.Rewriting.Text.Dsl;

/// <summary>
/// Defines a strategy for cutting outgoing text into frames before filters are applied.
/// </summary>
public interface ITextFraming
{
    /// <summary>
    /// Attempts to cut a frame from the provided span, advancing the span when successful.
    /// </summary>
    bool TryCutFrame(ref ReadOnlySpan<char> buffer, out ReadOnlySpan<char> frame);

    /// <summary>
    /// Attempts to cut a frame from the provided memory, advancing the memory when successful.
    /// </summary>
    bool TryCutFrame(ref ReadOnlyMemory<char> buffer, out ReadOnlyMemory<char> frame);
}
