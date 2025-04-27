// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Core;
using Itexoft.Threading;

namespace Itexoft.Text.Rewriting.Primitives.Pipeline;

/// <summary>
/// Entry point for executing a rewrite pipeline built from staged kernels.
/// </summary>
public sealed class RewritePipeline : IDisposable, IAsyncDisposable
{
    private readonly IPipelineStage head;
    private Disposed disposed;

    internal RewritePipeline(IPipelineStage head) => this.head = head ?? throw new ArgumentNullException(nameof(head));

    public async ValueTask DisposeAsync()
    {
        if (this.disposed.Enter())
            return;

        await this.head.DisposeAsync().ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (this.disposed.Enter())
            return;

        this.head.Dispose();
    }

    public void Write(ReadOnlySpan<char> span)
    {
        this.disposed.ThrowIf();
        this.head.Write(span);
    }

    public ValueTask WriteAsync(ReadOnlyMemory<char> memory, CancelToken cancelToken = default)
    {
        this.disposed.ThrowIf();

        return this.head.WriteAsync(memory, cancelToken);
    }

    public void Flush()
    {
        this.disposed.ThrowIf();
        this.head.Flush();
    }

    public ValueTask FlushAsync(CancelToken cancelToken = default)
    {
        this.disposed.ThrowIf();

        return this.head.FlushAsync(cancelToken);
    }
}
