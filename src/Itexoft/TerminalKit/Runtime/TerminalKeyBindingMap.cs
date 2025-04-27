// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.TerminalKit;

/// <summary>
/// Declarative mapping between console keys and handlers.
/// </summary>
internal sealed class TerminalKeyBindingMap
{
    private readonly Dictionary<ConsoleKey, Func<TerminalKeyBindingContext, bool>> _handlers = new();
    private Action? _afterInvoke;

    public TerminalKeyBindingMap WithScrollableWindow(TerminalScrollableWindowController controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        this.On(
            ConsoleKey.UpArrow,
            _ =>
            {
                controller.MovePrevious();

                return false;
            });
        this.On(
            ConsoleKey.DownArrow,
            _ =>
            {
                controller.MoveNext();

                return false;
            });
        this.On(
            ConsoleKey.PageUp,
            _ =>
            {
                controller.PageUp();

                return false;
            });
        this.On(
            ConsoleKey.PageDown,
            _ =>
            {
                controller.PageDown();

                return false;
            });

        return this;
    }

    public TerminalKeyBindingMap On(ConsoleKey key, Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        this._handlers[key] = _ =>
        {
            action();
            this._afterInvoke?.Invoke();

            return false;
        };

        return this;
    }

    public TerminalKeyBindingMap On(ConsoleKey key, Func<bool> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        this._handlers[key] = _ =>
        {
            var exit = handler();
            this._afterInvoke?.Invoke();

            return exit;
        };

        return this;
    }

    public TerminalKeyBindingMap On(ConsoleKey key, Func<TerminalKeyBindingContext, bool> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        this._handlers[key] = ctx =>
        {
            var exit = handler(ctx);
            this._afterInvoke?.Invoke();

            return exit;
        };

        return this;
    }

    internal void SetAfterInvoke(Action invalidate)
    {
        this._afterInvoke = invalidate;
    }

    public bool TryHandle(ConsoleKeyInfo keyInfo)
    {
        if (!this._handlers.TryGetValue(keyInfo.Key, out var handler))
            return false;

        return handler(new(keyInfo));
    }
}

public readonly struct TerminalKeyBindingContext
{
    public TerminalKeyBindingContext(ConsoleKeyInfo keyInfo) => this.KeyInfo = keyInfo;

    public ConsoleKeyInfo KeyInfo { get; }

    public ConsoleKey Key => this.KeyInfo.Key;

    public ConsoleModifiers Modifiers => this.KeyInfo.Modifiers;
}