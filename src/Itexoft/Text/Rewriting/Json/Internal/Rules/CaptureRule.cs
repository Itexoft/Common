// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Text.Rewriting.Json.Internal.Rules;

internal sealed class CaptureRule : JsonRewriteRule
{
    internal CaptureRule(string pointer, Action<string> onValue)
    {
        this.Pointer = pointer ?? throw new ArgumentNullException(nameof(pointer));
        this.OnValue = onValue ?? throw new ArgumentNullException(nameof(onValue));
    }

    internal override string Pointer { get; }

    internal Action<string> OnValue { get; }
}
