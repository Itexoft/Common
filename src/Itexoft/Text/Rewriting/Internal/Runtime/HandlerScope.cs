// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Text.Rewriting.Internal.Runtime;

internal sealed class HandlerScope<THandlers>
{
    private readonly AsyncLocal<THandlers?> current = new();

    public THandlers? Current
    {
        get => this.current.Value;
        set => this.current.Value = value;
    }

    public IDisposable Push(THandlers value)
    {
        var previous = this.current.Value;
        this.current.Value = value;

        return new Scope(this, previous);
    }

    private sealed class Scope(HandlerScope<THandlers> owner, THandlers? previous) : IDisposable
    {
        public void Dispose() => owner.current.Value = previous;
    }
}
