using Itexoft.Common.Collections;

namespace Itexoft.Common.Threading;

public sealed class KeyLocks<TKey> where TKey : notnull
{
    private readonly ConcurrentDictionaryLazy<TKey, LockItem> items = new();

    public async Task<IDisposable> Lock(TKey key)
    {
        var result = this.items.GetOrAdd(key, k => new(k, this.Remove));
        await result.WaitAsync();
        return result;
    }

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