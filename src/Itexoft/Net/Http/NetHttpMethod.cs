// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Core;

namespace Itexoft.Net.Http;

public readonly record struct NetHttpMethod(LString Value)
{
    public static readonly NetHttpMethod Get = new("GET");
    public static readonly NetHttpMethod Post = new("POST");
    public override string ToString() => this.Value.ToString();
}
