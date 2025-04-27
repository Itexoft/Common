// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Text.Rewriting.Json.Dsl;

/// <summary>
/// Defines a strategy for splitting incoming text into JSON frames.
/// </summary>
public interface IJsonFraming
{
    /// <summary>
    /// Attempts to cut a frame from the span buffer, advancing the buffer when successful.
    /// </summary>
    bool TryCutFrame(ref ReadOnlySpan<char> buffer, out ReadOnlySpan<char> frame);

    /// <summary>
    /// Attempts to cut a frame from the memory buffer, advancing the buffer when successful.
    /// </summary>
    bool TryCutFrame(ref ReadOnlyMemory<char> buffer, out ReadOnlyMemory<char> frame);
}
