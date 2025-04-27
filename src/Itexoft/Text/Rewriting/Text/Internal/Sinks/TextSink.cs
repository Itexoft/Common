// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Threading;

namespace Itexoft.Text.Rewriting.Text.Internal.Sinks;

internal interface ITextSink
{
    void Write(ReadOnlySpan<char> buffer);

    ValueTask WriteAsync(ReadOnlyMemory<char> buffer, CancelToken cancelToken);
}

internal interface IFlushableTextSink : ITextSink
{
    void FlushPending();

    ValueTask FlushPendingAsync(CancelToken cancelToken);
}
