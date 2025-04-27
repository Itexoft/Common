// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Collections;
using Itexoft.Collections;

namespace Itexoft.Net.Core;

public class NetDialTrackerCollection : IEnumerable<KeyValuePair<NetEndpoint, INetDialTracker>>
{
    private readonly AtomicDictionary<NetEndpoint, INetDialTracker> dialTrackers = [];

    public int Count => this.dialTrackers.Count;

    public bool IsEmpty => this.dialTrackers.IsEmpty;

    public IEnumerator<KeyValuePair<NetEndpoint, INetDialTracker>> GetEnumerator() => this.dialTrackers.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();

    public INetDialTracker GetOrAdd(NetEndpoint endpointKey, Func<NetEndpoint, INetDialTracker> func) =>
        this.dialTrackers.GetOrAdd(endpointKey, func);
}
