// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Text.Rewriting.Text.Dsl;
using Itexoft.Threading;

namespace Itexoft.Text.Rewriting.Primitives.Pipeline;

internal sealed class TextPipelineStage<THandlers>(TextSession<THandlers> session) : IPipelineStage
{
    private readonly TextSession<THandlers> session = session ?? throw new ArgumentNullException(nameof(session));

    public void Write(ReadOnlySpan<char> span) => this.session.Write(span);

    public ValueTask WriteAsync(ReadOnlyMemory<char> memory, CancelToken cancelToken) => this.session.WriteAsync(memory, cancelToken);

    public void Flush() => this.session.Flush();

    public ValueTask FlushAsync(CancelToken cancelToken) => this.session.FlushAsync(cancelToken);

    public void Dispose() => this.session.Dispose();

    public ValueTask DisposeAsync() => this.session.DisposeAsync();
}
