// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Extensions;

namespace Itexoft.TerminalKit.Rendering;

internal sealed class TerminalDispatcher
{
    private static readonly AsyncLocal<TerminalDispatcher?> currentDispatcher = new();
    private readonly Func<Func<object?>, object?> externalRunner;
    private readonly Action invalidate;

    private TerminalDispatcher(Action invalidate, Func<Func<object?>, object?> externalRunner)
    {
        this.invalidate = invalidate ?? throw new ArgumentNullException(nameof(invalidate));
        this.externalRunner = externalRunner ?? throw new ArgumentNullException(nameof(externalRunner));
    }

    public static TerminalDispatcher? Current => currentDispatcher.Value;

    public void Invalidate() => this.invalidate();

    public void RunExternal(Action action)
    {
        action.Required();

        this.externalRunner(() =>
        {
            action();

            return null;
        });
    }

    public T RunExternal<T>(Func<T> operation)
    {
        operation.Required();
        var result = this.externalRunner(() => operation()!);

        return result is T typed ? typed : default!;
    }

    internal static TerminalDispatcher Install(Action invalidate, Func<Func<object?>, object?> externalRunner)
    {
        var dispatcher = new TerminalDispatcher(invalidate, externalRunner);
        currentDispatcher.Value = dispatcher;

        return dispatcher;
    }

    internal static void Remove(TerminalDispatcher dispatcher)
    {
        if (currentDispatcher.Value == dispatcher)
            currentDispatcher.Value = null;
    }
}
