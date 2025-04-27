// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.CompilerServices;

namespace Itexoft.Threading;

/// <summary>
/// Lightweight helper that lazily computes a value with either sync or async factory without adding async overhead to the sync path.
/// </summary>
/// <typeparam name="T">Type of value to compute.</typeparam>
internal struct SyncAsyncLazy<T>
{
    private readonly Func<T>? syncFactory;
    private readonly Func<CancelToken, ValueTask<T>>? asyncFactory;
    private T value;
    private bool hasValue;

    private SyncAsyncLazy(T value)
    {
        this.syncFactory = null;
        this.asyncFactory = null;
        this.value = value;
        this.hasValue = true;
    }

    private SyncAsyncLazy(Func<T>? syncFactory, Func<CancelToken, ValueTask<T>>? asyncFactory)
    {
        if (syncFactory is null && asyncFactory is null)
            throw new ArgumentException("At least one factory must be provided.", nameof(syncFactory));

        this.syncFactory = syncFactory;
        this.asyncFactory = asyncFactory;
        this.value = default!;
        this.hasValue = false;
    }

    public static SyncAsyncLazy<T> FromValue(T value) => new(value);

    public static SyncAsyncLazy<T> Create(Func<T>? syncFactory, Func<CancelToken, ValueTask<T>>? asyncFactory = null) =>
        new(syncFactory, asyncFactory);

    /// <summary>
    /// Creates a lazy value from provided factories, or falls back to a precomputed value when both factories are null.
    /// </summary>
    public static SyncAsyncLazy<T> CreateOrDefault(Func<T>? syncFactory, Func<CancelToken, ValueTask<T>>? asyncFactory, T defaultValue)
    {
        if (syncFactory is null && asyncFactory is null)
            return FromValue(defaultValue);

        return new(syncFactory, asyncFactory);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T GetOrCreate()
    {
        if (this.hasValue)
            return this.value;

        if (this.syncFactory is null)
            throw new InvalidOperationException("Synchronous factory is not provided.");

        this.value = this.syncFactory();
        this.hasValue = true;

        return this.value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<T> GetOrCreateAsync(CancelToken cancelToken = default)
    {
        if (this.hasValue)
            return new(this.value);

        if (this.asyncFactory is null)
            return new(this.GetOrCreate());

        return this.GetOrCreateAsyncCore(cancelToken);
    }

    private async ValueTask<T> GetOrCreateAsyncCore(CancelToken cancelToken)
    {
        cancelToken.ThrowIf();

        var result = await this.asyncFactory!(cancelToken).ConfigureAwait(false);
        this.value = result;
        this.hasValue = true;

        return result;
    }
}
