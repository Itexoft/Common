// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Threading;

public static class TaskExtensions
{
    extension(Task task)
    {
        public async Task WaitAsync(CancelToken cancelToken)
        {
            using (cancelToken.Bridge(out var token))
                await task.WaitAsync(token);
        }
    }

    extension<T>(Task<T> task)
    {
        public async Task<T> WaitAsync(CancelToken cancelToken)
        {
            using (cancelToken.Bridge(out var token))
                return await task.WaitAsync(token);
        }
    }
}
