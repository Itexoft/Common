// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.CompilerServices;
using Itexoft.Extensions;

namespace Itexoft.Threading.ControlFlow;

public ref struct InvokeGateDrop<TResult>
{
    private int state;
    private readonly int limit;
    private readonly Func<TResult?> callback;

    internal InvokeGateDrop(Func<TResult?> callback, int limit)
    {
        this.callback = callback.Required();
        this.limit = limit.RequiredPositive();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryInvoke(out TResult? result)
    {
        var n = Interlocked.Increment(ref this.state);

        if ((uint)n > (uint)this.limit)
        {
            Interlocked.Decrement(ref this.state);
            result = default!;

            return false;
        }

        try
        {
            result = this.callback();

            return true;
        }
        finally
        {
            Interlocked.Decrement(ref this.state);
        }
    }
}
