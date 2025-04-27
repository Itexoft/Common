// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Formats.Configuration.Ini;

public sealed class IniSection
{
    private readonly Dictionary<string, IniValueCollection> valueIndex;

    internal IniSection(string name, ReadOnlyMemory<char> nameMemory, bool isGlobal, List<IniEntry> entries, StringComparer keyComparer)
    {
        this.Name = name;
        this.NameMemory = nameMemory;
        this.IsGlobal = isGlobal;
        this.Entries = new IniEntryCollection(entries);
        this.valueIndex = BuildValueIndex(entries, keyComparer);
    }

    public string Name { get; }

    public ReadOnlyMemory<char> NameMemory { get; }

    public bool IsGlobal { get; }

    public IniEntryCollection Entries { get; }

    public IEnumerable<IniKeyValueEntry> KeyValues => this.Entries.KeyValues;

    public IEnumerable<IniValueEntry> Values => this.Entries.Values;

    public bool TryGetValues(string key, out IniValueCollection values) =>
        this.valueIndex.TryGetValue(key, out values!);

    public IniValueCollection GetValues(string key) =>
        this.valueIndex.TryGetValue(key, out var values) ? values : IniValueCollection.Empty;

    public bool TryGetValue(string key, out IniValue value)
    {
        if (this.valueIndex.TryGetValue(key, out var values) && values.Count > 0)
        {
            value = values[0];

            return true;
        }

        value = default;

        return false;
    }

    private static Dictionary<string, IniValueCollection> BuildValueIndex(IEnumerable<IniEntry> entries, StringComparer keyComparer)
    {
        var map = new Dictionary<string, List<IniValue>>(keyComparer);

        foreach (var entry in entries)
        {
            if (entry is not IniKeyValueEntry keyValue)
                continue;

            var key = keyValue.Key.Text;

            if (!map.TryGetValue(key, out var list))
            {
                list = new List<IniValue>();
                map[key] = list;
            }

            for (var i = 0; i < keyValue.Values.Count; i++)
                list.Add(keyValue.Values[i]);
        }

        var index = new Dictionary<string, IniValueCollection>(keyComparer);

        foreach (var pair in map)
            index[pair.Key] = new IniValueCollection(pair.Value.ToArray());

        return index;
    }
}
