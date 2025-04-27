// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Collections;

namespace Itexoft.Net.Http;

public readonly struct NetHttpContentTypeList : IEnumerable<NetHttpContentType>
{
    private readonly NetHttpHeaders headers;
    private readonly string name;

    internal NetHttpContentTypeList(NetHttpHeaders headers, string name)
    {
        this.headers = headers;
        this.name = name;
    }

    public bool IsEmpty => this.headers.GetValues(this.name).Count == 0;

    public IReadOnlyList<string> RawValues => this.headers.GetValues(this.name);

    public NetHttpContentType[] ToArray() => NetHttpContentType.ParseList(this.headers.GetValues(this.name));

    public void Clear() => this.headers.Remove(this.name);

    public void Add(NetHttpContentType value) => this.headers.Add(this.name, value.ToString());

    public void AddRange(IEnumerable<NetHttpContentType> values)
    {
        if (values is null)
            return;

        foreach (var value in values)
            this.headers.Add(this.name, value.ToString());
    }

    public void Set(params NetHttpContentType[] values) => this.Set((IEnumerable<NetHttpContentType>)values);

    public void Set(IEnumerable<NetHttpContentType> values)
    {
        this.headers.Remove(this.name);

        if (values is null)
            return;

        foreach (var value in values)
            this.headers.Add(this.name, value.ToString());
    }

    public IEnumerator<NetHttpContentType> GetEnumerator() => new ContentTypeEnumerator(this);

    IEnumerator<NetHttpContentType> IEnumerable<NetHttpContentType>.GetEnumerator() => this.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();

    public override string ToString()
    {
        var values = this.headers.GetValues(this.name);

        if (values.Count == 0)
            return string.Empty;

        if (values.Count == 1)
            return values[0];

        return string.Join(", ", values);
    }

    private sealed class ContentTypeEnumerator : IEnumerator<NetHttpContentType>
    {
        private readonly NetHttpContentType[] values;
        private int index;

        internal ContentTypeEnumerator(NetHttpContentTypeList list)
        {
            this.values = NetHttpContentType.ParseList(list.headers.GetValues(list.name));
            this.index = -1;
        }

        public NetHttpContentType Current => this.values[this.index];

        object IEnumerator.Current => this.Current;

        public bool MoveNext()
        {
            var next = this.index + 1;

            if (next >= this.values.Length)
                return false;

            this.index = next;

            return true;
        }

        public void Reset() => this.index = -1;

        public void Dispose() { }
    }
}
