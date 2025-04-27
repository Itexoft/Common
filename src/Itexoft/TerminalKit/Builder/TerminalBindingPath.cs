// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.TerminalKit;

/// <summary>
/// Helper utilities for composing binding expressions.
/// </summary>
internal static class TerminalBindingPath
{
    public static string State<TState>(StateHandle<TState> handle)
    {
        if (string.IsNullOrWhiteSpace(handle.Name))
            throw new ArgumentException("State handle must contain a valid name.", nameof(handle));

        return $"@state.{handle.Name}";
    }
}
