// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.CompilerServices;

namespace Itexoft.Core;

[method: MethodImpl(MethodImplOptions.AggressiveInlining)]
public sealed class Dispose(Action? action) : IDisposable
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    void IDisposable.Dispose() => action?.Invoke();

    public static implicit operator Dispose(Action? action) => new(action);
}

public sealed class DisposeAsync(Func<ValueTask>? action) : IAsyncDisposable
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask IAsyncDisposable.DisposeAsync() => action?.Invoke() ?? ValueTask.CompletedTask;

    public static implicit operator DisposeAsync(Func<ValueTask>? action) => new(action);
}

[method: MethodImpl(MethodImplOptions.AggressiveInlining)]
public sealed class Dispose<T>(Action<T>? action, T context) : IDisposable
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    void IDisposable.Dispose() => action?.Invoke(context);
}

[method: MethodImpl(MethodImplOptions.AggressiveInlining)]
public sealed class Dispose<T1, T2>(Action<T1, T2>? action, T1 context1, T2 context2) : IDisposable
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    void IDisposable.Dispose() => action?.Invoke(context1, context2);
}
