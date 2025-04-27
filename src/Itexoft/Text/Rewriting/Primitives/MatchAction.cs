// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Text.Rewriting.Primitives;

/// <summary>
/// Determines how a matched rule should be applied to the output stream.
/// </summary>
public enum MatchAction
{
    /// <summary>
    /// Keep the matched text unchanged while still triggering callbacks.
    /// </summary>
    None = 0,

    /// <summary>
    /// Alias for <see cref="None" /> when the intention is to hook without mutation.
    /// </summary>
    Hook = None,

    /// <summary>
    /// Remove the matched text from the output.
    /// </summary>
    Remove = 1,

    /// <summary>
    /// Replace the matched text with a provided literal or a factory-produced string.
    /// </summary>
    Replace = 2,
}
