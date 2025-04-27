// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Core;

public abstract class Accessor
{
    private protected Accessor() { }
}

public sealed class Accessor<TValue>(Func<TValue?>? getter, Action<TValue?>? setter) : Accessor
{
    public Accessor(Func<TValue?>? getter) : this(getter, null) { }

    public Accessor(Action<TValue?>? setter) : this(null, setter) { }

    public TValue? Value
    {
        get => getter is null ? default : getter.Invoke();
        set => setter?.Invoke(value);
    }

    public static implicit operator Accessor<TValue>(Action<TValue?>? getter) => new(getter);
    public static implicit operator Accessor<TValue>(Func<TValue?>? setter) => new(setter);
}
