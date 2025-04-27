// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Collections;

namespace Itexoft.Threading;

public sealed class KeyLocks<TKey> where TKey : notnull
{
    private readonly ConcurrentDictionaryLazy<TKey, LockItem> items = new();

    public async Task<IDisposable> Lock(TKey key)
    {
        var result = this.items.GetOrAdd(key, k => new(k, this.Remove));
        await result.WaitAsync();

        return result;
    }

    public bool IsLocked(TKey key) => this.items.ContainsKey(key);

    private bool Remove(TKey item) => this.items.TryRemove(item, (k, v) => v.Release() == 1, out _);

    private sealed class LockItem(TKey key, Func<TKey, bool> onRemove) : IDisposable
    {
        private readonly SemaphoreSlim semaphore = new(1, 1);

        public void Dispose()
        {
            if (onRemove(key))
                this.semaphore.Dispose();
        }

        public async Task WaitAsync() => await this.semaphore.WaitAsync();

        public int Release() => this.semaphore.Release();
    }
}