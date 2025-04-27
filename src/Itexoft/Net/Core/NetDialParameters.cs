// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Extensions;

namespace Itexoft.Net.Core;

public class NetDialParameters(NetEndpoint endpoint, Func<ValueTask> dispose, INetDialTracker dialTracker)
{
    public delegate ValueTask ChangeStateDelegate(NetConnectionTransitionCause cause, Exception? exception);

    public NetEndpoint Endpoint { get; } = endpoint;
    public Func<ValueTask> Dispose { get; } = dispose.Required();
    public INetDialTracker Tracker { get; } = dialTracker.Required();
    public required ChangeStateDelegate ChangeState { get; init; }
}
