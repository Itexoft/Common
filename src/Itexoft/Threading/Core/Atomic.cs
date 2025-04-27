// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.CompilerServices;
using Itexoft.Threading.Core;

namespace Itexoft.Threading;

/// <summary>
/// Small set of atomic helpers around <see cref="Interlocked" />/<see cref="Volatile" />.
/// </summary>
public static class Atomic
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void InvokeOnce(ref Action? action) => Interlocked.Exchange(ref action, null)?.Invoke();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void InvokeOnce<T1>(ref Action<T1>? action, T1 p1) => Interlocked.Exchange(ref action, null)?.Invoke(p1);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Action InvokeOnce(Action? action)
    {
        var invoke = action;

        return invokeOnce;

        void invokeOnce() => Interlocked.Exchange(ref invoke, null)?.Invoke();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Action<T1> InvokeOnce<T1>(Action<T1> action)
    {
        var invoke = action;

        return invokeOnce;

        void invokeOnce(T1 p1) => Interlocked.Exchange(ref invoke, null)?.Invoke(p1);
    }

    /// <summary>
    /// Exchanges the location with the provided value when the current value is greater than the comparison.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool ExchangeIfGreaterThan(ref long location, long value, long comparison, out long originalValue, out long newValue)
    {
        do
        {
            originalValue = Volatile.Read(ref location);

            if (originalValue <= comparison)
            {
                newValue = originalValue;

                return false;
            }

            newValue = value;
        }
        while (Interlocked.CompareExchange(ref location, newValue, originalValue) != originalValue);

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool ExchangeIfGreaterThan(ref long location, long value, long comparison, out long originalValue) =>
        ExchangeIfGreaterThan(ref location, value, comparison, out originalValue, out _);

    /// <summary>
    /// Exchanges the location with the provided value when the current value is less than the comparison.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool ExchangeIfLesserThan(ref long location, long value, long comparison, out long originalValue, out long newValue)
    {
        do
        {
            originalValue = Volatile.Read(ref location);

            if (originalValue >= comparison)
            {
                newValue = originalValue;

                return false;
            }

            newValue = value;
        }
        while (Interlocked.CompareExchange(ref location, newValue, originalValue) != originalValue);

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool ExchangeIfLesserThan(ref long location, long value, long comparison, out long originalValue) =>
        ExchangeIfLesserThan(ref location, value, comparison, out originalValue, out _);

    /// <summary>
    /// Adds the specified delta when the current value is less than the comparison.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool AddIfLesserThan(ref long location, long delta, long comparison, out long newValue)
    {
        long originalValue;

        do
        {
            originalValue = Volatile.Read(ref location);

            if (originalValue >= comparison)
            {
                newValue = originalValue;

                return false;
            }

            newValue = unchecked(originalValue + delta);
        }
        while (Interlocked.CompareExchange(ref location, newValue, originalValue) != originalValue);

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long Multiply(ref long location, long value)
    {
        long originalValue;
        long newValue;

        do
        {
            originalValue = Volatile.Read(ref location);
            newValue = unchecked(originalValue * value);
        }
        while (Interlocked.CompareExchange(ref location, newValue, originalValue) != originalValue);

        return newValue;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long Add(ref long location, long value) => Interlocked.Add(ref location, value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Add(ref double location, double value)
    {
        double originalValue;
        double newValue;

        do
        {
            originalValue = Volatile.Read(ref location);
            newValue = unchecked(originalValue + value);
        }
        while (Interlocked.CompareExchange(ref location, newValue, originalValue) != originalValue);

        return newValue;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long Max(ref long location, long value)
    {
        long originalValue;

        do
        {
            originalValue = Volatile.Read(ref location);

            if (originalValue >= value)
                return originalValue;
        }
        while (Interlocked.CompareExchange(ref location, value, originalValue) != originalValue);

        return value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long Min(ref long location, long value)
    {
        long originalValue;

        do
        {
            originalValue = Volatile.Read(ref location);

            if (originalValue <= value)
                return originalValue;
        }
        while (Interlocked.CompareExchange(ref location, value, originalValue) != originalValue);

        return value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TrySetFlag(ref int location, int flag) => (Interlocked.Or(ref location, flag) & flag) == 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryClearFlag(ref int location, int flag) => (Interlocked.And(ref location, ~flag) & flag) != 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TrySetFlag(ref long location, long flag) => (Interlocked.Or(ref location, flag) & flag) == 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryClearFlag(ref long location, long flag) => (Interlocked.And(ref location, ~flag) & flag) != 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool ExchangeAsBoolean(ref int location, bool value) => Interlocked.Exchange(ref location, value ? 1 : 0) == 1;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void Exchange(
        ref long location,
        void* state,
        delegate*<long, void*, long> getValue,
        out long originalValue,
        out long newValue)
    {
        if (getValue == null)
            throw new ArgumentNullException(nameof(getValue));

        do
        {
            originalValue = Volatile.Read(ref location);
            newValue = getValue(originalValue, state);
        }
        while (Interlocked.CompareExchange(ref location, newValue, originalValue) != originalValue);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void Exchange(
        ref double location,
        void* state,
        delegate*<double, void*, double> getValue,
        out double originalValue,
        out double newValue)
    {
        if (getValue == null)
            throw new ArgumentNullException(nameof(getValue));

        do
        {
            originalValue = Volatile.Read(ref location);
            newValue = getValue(originalValue, state);
        }
        while (Interlocked.CompareExchange(ref location, newValue, originalValue) != originalValue);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe bool ExchangeIf(
        ref long location,
        void* state,
        delegate*<long, void*, bool> predicate,
        delegate*<long, void*, long> getValue,
        out long originalValue,
        out long newValue)
    {
        if (predicate == null)
            throw new ArgumentNullException(nameof(predicate));

        if (getValue == null)
            throw new ArgumentNullException(nameof(getValue));

        do
        {
            originalValue = Volatile.Read(ref location);

            if (!predicate(originalValue, state))
            {
                newValue = originalValue;

                return false;
            }

            newValue = getValue(originalValue, state);
        }
        while (Interlocked.CompareExchange(ref location, newValue, originalValue) != originalValue);

        return true;
    }

    public static void Exchange(ref double location, Func<double, double> getValue, out double originalValue, out double newValue)
    {
        ArgumentNullException.ThrowIfNull(getValue);

        do
        {
            originalValue = Volatile.Read(ref location);
            newValue = getValue(originalValue);
        }
        while (Interlocked.CompareExchange(ref location, newValue, originalValue) != originalValue);
    }

    public static void Exchange(ref long location, Func<long, long> getValue, out long originalValue, out long newValue)
    {
        ArgumentNullException.ThrowIfNull(getValue);

        do
        {
            originalValue = Volatile.Read(ref location);
            newValue = getValue(originalValue);
        }
        while (Interlocked.CompareExchange(ref location, newValue, originalValue) != originalValue);
    }

    /// <summary>
    /// Attempts to apply the update only when the predicate passes; returns true when the exchange happens.
    /// </summary>
    public static bool ExchangeIf(ref long location, Func<long, bool> predicate, Func<long, long> getValue, out long originalValue, out long newValue)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(getValue);

        do
        {
            originalValue = Volatile.Read(ref location);

            if (!predicate(originalValue))
            {
                newValue = originalValue;

                return false;
            }

            newValue = getValue(originalValue);
        }
        while (Interlocked.CompareExchange(ref location, newValue, originalValue) != originalValue);

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T CreateIfNull<T>(ref T? location) where T : class, new()
    {
        lock (TypeShared<T>.Lock)
            return location ??= new T();
    }
}
