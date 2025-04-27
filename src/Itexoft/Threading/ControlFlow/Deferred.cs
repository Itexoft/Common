// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Core;

namespace Itexoft.Threading.ControlFlow;

/// <summary>
/// Thread-safe lazy holder with explicit lifetime control and safe disposal.
/// Algorithm originally by Denis Kudelin (2015), refreshed by Denis Kudelin and Codex (2025).
/// </summary>
public sealed class Deferred<T>
{
    [ThreadStatic] private static bool isFactoryCalledInCurrentThread;
    private readonly Action<T>? disposeHandler;

    private readonly Func<T> factory;
    private readonly Lock sync = new();
    private volatile bool creationAttempted;
    private Disposed disposed;
    private volatile bool isValueCreated;

    private T value = default!;

    public Deferred(Func<T> factory, Action<T>? disposeHandler = null)
    {
        this.factory = DeferredUtility.CreateDelegate(() =>
        {
            isFactoryCalledInCurrentThread = true;

            return factory();
        });

        this.disposeHandler = disposeHandler;
    }

    public T Value
    {
        get
        {
            if (this.TryEnsureValue())
                return this.value;

            throw new ObjectDisposedException(nameof(Deferred<T>));
        }
    }

    public bool IsDisposed => this.disposed;
    public bool IsValueCreated => this.isValueCreated;

    private bool TryEnsureValue()
    {
        isFactoryCalledInCurrentThread = false;

        if (this.isValueCreated)
            return !this.disposed;

        if (this.disposed)
            return false;

        lock (this.sync)
        {
            if (this.isValueCreated)
                return true;

            if (this.disposed)
                return false;

            this.creationAttempted = true;
            this.value = this.factory();
            this.isValueCreated = true;

            if (this.disposed && !isFactoryCalledInCurrentThread)
                return false;

            return true;
        }
    }

    /// <summary>
    /// Attempts to get or create the value.
    /// </summary>
    /// <param name="value">Resulting value when available.</param>
    /// <returns>true when value is available and not disposed.</returns>
    public bool TryGetValue(out T value) => this.TryGetValue(out value, out _);

    /// <summary>
    /// Attempts to get or create the value and reports whether it was produced in the current thread.
    /// </summary>
    /// <param name="value">Resulting value when available.</param>
    /// <param name="isCreated">true when the value was created in the current thread during this call.</param>
    /// <returns>true when value is available and not disposed.</returns>
    public bool TryGetValue(out T value, out bool isCreated)
    {
        isFactoryCalledInCurrentThread = false;

        if (this.TryEnsureValue())
        {
            isCreated = isFactoryCalledInCurrentThread;
            value = this.value;

            return true;
        }

        isCreated = false;
        value = default!;

        return false;
    }

    /// <summary>
    /// Returns the value only if it was already created previously.
    /// </summary>
    /// <param name="value">Previously created value, if any.</param>
    /// <returns>true when the value exists and not disposed; otherwise false.</returns>
    public bool TryGetValueIfCreated(out T value)
    {
        if (this.isValueCreated && !this.disposed)
        {
            value = this.value;

            return true;
        }

        value = default!;

        return false;
    }

    /// <summary>
    /// Disposes the deferred value if it was created and returns it to the caller.
    /// </summary>
    /// <param name="value">The created value if disposal occurred; otherwise default.</param>
    /// <returns>true when the value existed and dispose handler was called; otherwise false.</returns>
    public bool Dispose(out T? value)
    {
        if (this.disposed.Enter())
        {
            value = default;

            return false;
        }

        lock (this.sync)
        {
            if (!this.creationAttempted || !this.isValueCreated)
            {
                value = default;

                return false;
            }

            var current = this.value;
            this.value = default!;

            if (this.disposeHandler is null)
                value = current;
            else
            {
                this.disposeHandler(current);
                value = default;
            }

            return true;
        }
    }
}
