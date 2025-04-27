// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Net.NetworkInformation;

namespace Itexoft.Net.Core;

internal sealed class NetChangeEventSource : INetEventSource
{
    public NetChangeEventSource()
    {
        NetworkChange.NetworkAvailabilityChanged += this.OnAvailabilityChanged;
        NetworkChange.NetworkAddressChanged += this.OnAddressChanged;
    }

    public event EventHandler? NetworkAvailabilityLost;
    public event EventHandler? NetworkAddressChanged;

    public void Dispose()
    {
        NetworkChange.NetworkAvailabilityChanged -= this.OnAvailabilityChanged;
        NetworkChange.NetworkAddressChanged -= this.OnAddressChanged;
    }

    private void OnAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
    {
        if (!e.IsAvailable)
            this.NetworkAvailabilityLost?.Invoke(this, EventArgs.Empty);
    }

    private void OnAddressChanged(object? sender, EventArgs e) => this.NetworkAddressChanged?.Invoke(this, EventArgs.Empty);
}
