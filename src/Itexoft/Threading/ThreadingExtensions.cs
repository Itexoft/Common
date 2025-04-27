// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Threading;

public static class ThreadingExtensions
{
    extension(SemaphoreSlim semaphoreSlim)
    {
        public async Task WaitAsync(CancelToken cancelToken)
        {
            using (cancelToken.Bridge(out var token))
                await semaphoreSlim.WaitAsync(token);
        }
    }
}
