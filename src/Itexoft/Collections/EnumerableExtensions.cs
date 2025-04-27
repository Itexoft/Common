// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Collections;

public static class EnumerableExtensions
{
    extension<T>(IEnumerable<T> enumerable)
    {
        public T FirstOrDefault(T defaultValue, Func<T, bool> predicate)
        {
            foreach (var value in enumerable)
            {
                if (predicate(value))
                    return value;
            }

            return defaultValue;
        }
    }
}
