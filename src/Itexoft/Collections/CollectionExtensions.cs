// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Collections;
using Itexoft.Extensions;

namespace Itexoft.Collections;

public static class CollectionExtensions
{
    extension<T>(T[] array)
    {
        public T[] Copy()
        {
            var result = new T[array.Length];
            Array.Copy(array, result, array.Length);

            return result;
        }
    }

    extension<TSource, TResult>(IReadOnlyList<TSource> source)
    {
        public IReadOnlyList<TResult> Map(Func<TSource, TResult> map) => new MappedReadOnlyList<TSource, TResult>(source, map);
    }

    private sealed class MappedReadOnlyList<TSource, TResult>(IReadOnlyList<TSource> source, Func<TSource, TResult> map) : IReadOnlyList<TResult>
    {
        private readonly Func<TSource, TResult> map = map.Required();
        private readonly IReadOnlyList<TSource> source = source.Required();

        public int Count => this.source.Count;

        public TResult this[int index] => this.map(this.source[index]);

        public IEnumerator<TResult> GetEnumerator()
        {
            foreach (var t in this.source)
                yield return this.map(t);
        }

        IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
    }
}
