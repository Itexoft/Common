// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Diagnostics;

namespace System.Collections.Generic;

/// <summary>
/// Defines a key/value pair for displaying an item of a dictionary by a debugger.
/// </summary>
[DebuggerDisplay("{Value}", Name = "[{Key}]")]
internal readonly struct DebugViewDictionaryItem<TKey, TValue>(TKey key, TValue value)
{
    public DebugViewDictionaryItem(KeyValuePair<TKey, TValue> keyValue) : this(keyValue.Key, keyValue.Value) { }

    [DebuggerBrowsable(DebuggerBrowsableState.Collapsed)]
    public TKey Key { get; } = key;

    [DebuggerBrowsable(DebuggerBrowsableState.Collapsed)]
    public TValue Value { get; } = value;
}
