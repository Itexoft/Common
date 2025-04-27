// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Extensions;

namespace Itexoft.Threading.ControlFlow;

/// <summary>
/// Lazy value helper that runs the factory once, caches result/exception, and supports reset.
/// Algorithm originally by Denis Kudelin (2015), refreshed by Denis Kudelin and Codex (2025).
/// </summary>
public static class DeferredUtility
{
    /// <summary>
    /// Wraps a factory so it is executed once.
    /// </summary>
    /// <typeparam name="TResult">Type of the produced value.</typeparam>
    /// <param name="factory">Factory executed on first invocation.</param>
    /// <returns>Delegate that returns the cached value or executes the factory once.</returns>
    public static Func<TResult> CreateDelegate<TResult>(Func<TResult> factory) => CreateDelegate(factory, out _, out _);

    /// <summary>
    /// Wraps a factory so it is executed once; exposes a setter to publish a value externally.
    /// </summary>
    /// <typeparam name="TResult">Type of the produced value.</typeparam>
    /// <param name="factory">Factory executed on first invocation.</param>
    /// <param name="setValue">Setter that stores a value and completes the deferred.</param>
    /// <returns>Delegate that returns the cached value or executes the factory once.</returns>
    public static Func<TResult> CreateDelegate<TResult>(Func<TResult> factory, out Action<TResult>? setValue) =>
        CreateDelegate(factory, out _, out setValue);

    /// <summary>
    /// Wraps a factory so it is executed once; exposes a reset action to rerun the factory.
    /// </summary>
    /// <typeparam name="TResult">Type of the produced value.</typeparam>
    /// <param name="factory">Factory executed on first invocation.</param>
    /// <param name="reset">Reset action that clears cached state.</param>
    /// <returns>Delegate that returns the cached value or executes the factory once.</returns>
    public static Func<TResult> CreateDelegate<TResult>(Func<TResult> factory, out Action? reset) => CreateDelegate(factory, out reset, out _);

    /// <summary>
    /// Wraps a factory so it is executed once; exposes reset and external setter.
    /// </summary>
    /// <typeparam name="TResult">Type of the produced value.</typeparam>
    /// <param name="factory">Factory executed on first invocation.</param>
    /// <param name="reset">Reset action that clears cached state.</param>
    /// <param name="setValue">Setter that stores a value and completes the deferred.</param>
    /// <returns>Delegate that returns the cached value or executes the factory once.</returns>
    public static Func<TResult> CreateDelegate<TResult>(Func<TResult> factory, out Action? reset, out Action<TResult>? setValue)
    {
        var state = new StateHolder<TResult>(factory.Required());
        var gate = new Lock();

        reset = () => Interlocked.Exchange(ref state, new(factory));
        setValue = value => Volatile.Read(ref state).TrySetResult(value);

        return () =>
        {
            var snapshot = Volatile.Read(ref state);

            if (snapshot.TryGet(out var result))
                return result;

            lock (gate)
            {
                snapshot = Volatile.Read(ref state);

                if (snapshot.TryGet(out result))
                    return result;

                try
                {
                    result = snapshot.Factory();
                    snapshot.TrySetResult(result);

                    return result;
                }
                catch (Exception ex)
                {
                    snapshot.TrySetException(ex);

                    throw;
                }
            }
        };
    }

    private sealed class StateHolder<TResult>(Func<TResult> factory)
    {
        private static readonly object pending = new();
        private static readonly object nullResult = new();
        private object? completion = pending; // pending | Exception | TResult | nullResult

        /// <summary>
        /// Original factory used to produce the value.
        /// </summary>
        public Func<TResult> Factory { get; } = factory;

        /// <summary>
        /// Attempts to read the cached value; rethrows cached exception if faulted.
        /// </summary>
        /// <param name="result">Cached value when available.</param>
        /// <returns>true when value is available; false when not computed yet.</returns>
        public bool TryGet(out TResult result)
        {
            var snapshot = Volatile.Read(ref this.completion);

            if (ReferenceEquals(snapshot, pending))
            {
                result = default!;

                return false;
            }

            if (ReferenceEquals(snapshot, nullResult))
            {
                result = default!;

                return true;
            }

            if (snapshot is Exception ex)
                throw ex.Rethrow();

            result = (TResult)snapshot!;

            return true;
        }

        /// <summary>
        /// Publishes a successful value if state is still pending.
        /// </summary>
        public void TrySetResult(TResult value)
        {
            var boxed = value is null ? nullResult : value;
            Interlocked.CompareExchange(ref this.completion, boxed, pending);
        }

        /// <summary>
        /// Publishes a faulted state if still pending.
        /// </summary>
        public void TrySetException(Exception exception) => Interlocked.CompareExchange(ref this.completion, exception, pending);
    }
}
