// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Collections;

namespace Itexoft.Formats.Configuration.Ini;

public sealed class IniValueCollection : IReadOnlyList<IniValue>
{
    private static readonly IniValue[] EmptyValues = Array.Empty<IniValue>();

    private readonly IniValue[] values;

    internal IniValueCollection(IniValue[] values) =>
        this.values = values.Length == 0 ? EmptyValues : values;

    internal static IniValueCollection Empty { get; } = new(EmptyValues);

    public IniValue FirstOrDefault => this.values.Length > 0 ? this.values[0] : default;

    public int Count => this.values.Length;

    public IniValue this[int index] => this.values[index];

    public IEnumerator<IniValue> GetEnumerator() => ((IEnumerable<IniValue>)this.values).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => this.values.GetEnumerator();
}
