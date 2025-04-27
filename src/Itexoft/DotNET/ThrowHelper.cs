// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.DotNET;

internal static class ThrowHelper
{
    internal static void ThrowKeyNullException() => throw new ArgumentNullException("key");

    internal static void ThrowArgumentNullException(string paramName) => throw new ArgumentNullException(paramName);

    internal static void ThrowArgumentNullException(string paramName, string message) =>
        throw new ArgumentNullException(paramName, message);

    internal static void ThrowIncompatibleComparer() => throw new ArgumentException(SR.ConcurrentDictionary_IncompatibleComparer);

    internal static void ThrowValueNullException() => throw new ArgumentNullException("value");
}