// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.CompilerServices;

namespace Itexoft.Text.Rewriting.Text.Internal.Gates;

internal readonly struct RuleGateSnapshot(bool[] states, bool hasGate)
{
    public static RuleGateSnapshot AllEnabled { get; } = new([], false);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Allows(int ruleId)
    {
        if (!hasGate)
            return true;

        return states[ruleId];
    }
}
