// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Text.Rewriting.Primitives.Pipeline;

internal interface IPipelineStage : IDisposable, IAsyncDisposable
{
    void Write(ReadOnlySpan<char> span);

    ValueTask WriteAsync(ReadOnlyMemory<char> memory, CancellationToken cancellationToken);

    void Flush();

    ValueTask FlushAsync(CancellationToken cancellationToken);
}