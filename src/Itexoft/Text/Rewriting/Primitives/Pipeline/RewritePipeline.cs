// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Text.Rewriting.Primitives.Pipeline;

/// <summary>
/// Entry point for executing a rewrite pipeline built from staged kernels.
/// </summary>
public sealed class RewritePipeline : IDisposable, IAsyncDisposable
{
    private readonly IPipelineStage head;
    private bool disposed;

    internal RewritePipeline(IPipelineStage head) => this.head = head ?? throw new ArgumentNullException(nameof(head));

    public async ValueTask DisposeAsync()
    {
        if (this.disposed)
            return;

        await this.head.DisposeAsync().ConfigureAwait(false);
        this.disposed = true;
    }

    public void Dispose()
    {
        if (this.disposed)
            return;

        this.head.Dispose();
        this.disposed = true;
    }

    public void Write(ReadOnlySpan<char> span)
    {
        this.ThrowIfDisposed();
        this.head.Write(span);
    }

    public ValueTask WriteAsync(ReadOnlyMemory<char> memory, CancellationToken cancellationToken = default)
    {
        this.ThrowIfDisposed();

        return this.head.WriteAsync(memory, cancellationToken);
    }

    public void Flush()
    {
        this.ThrowIfDisposed();
        this.head.Flush();
    }

    public ValueTask FlushAsync(CancellationToken cancellationToken = default)
    {
        this.ThrowIfDisposed();

        return this.head.FlushAsync(cancellationToken);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(this.disposed, "This pipeline has been disposed.");
    }
}