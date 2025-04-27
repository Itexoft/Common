// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.TerminalKit.Rendering;

internal sealed class TerminalDispatcher
{
    private static readonly AsyncLocal<TerminalDispatcher?> CurrentDispatcher = new();
    private readonly Func<Func<object?>, object?> _externalRunner;
    private readonly Action _invalidate;

    private TerminalDispatcher(Action invalidate, Func<Func<object?>, object?> externalRunner)
    {
        this._invalidate = invalidate ?? throw new ArgumentNullException(nameof(invalidate));
        this._externalRunner = externalRunner ?? throw new ArgumentNullException(nameof(externalRunner));
    }

    public static TerminalDispatcher? Current => CurrentDispatcher.Value;

    public void Invalidate() => this._invalidate();

    public void RunExternal(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        this._externalRunner(() =>
        {
            action();

            return null;
        });
    }

    public T RunExternal<T>(Func<T> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var result = this._externalRunner(() => operation()!);

        return result is T typed ? typed : default!;
    }

    internal static TerminalDispatcher Install(Action invalidate, Func<Func<object?>, object?> externalRunner)
    {
        var dispatcher = new TerminalDispatcher(invalidate, externalRunner);
        CurrentDispatcher.Value = dispatcher;

        return dispatcher;
    }

    internal static void Remove(TerminalDispatcher dispatcher)
    {
        if (CurrentDispatcher.Value == dispatcher)
            CurrentDispatcher.Value = null;
    }
}