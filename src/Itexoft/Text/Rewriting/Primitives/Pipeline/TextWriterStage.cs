// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Text.Rewriting.Primitives.Pipeline;

internal sealed class TextWriterStage(TextWriter writer) : IPipelineStage
{
    private readonly TextWriter writer = writer ?? throw new ArgumentNullException(nameof(writer));

    public void Write(ReadOnlySpan<char> span) => this.writer.Write(span);

    public ValueTask WriteAsync(ReadOnlyMemory<char> memory, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return new(this.writer.WriteAsync(memory, cancellationToken));
    }

    public void Flush() => this.writer.Flush();

    public ValueTask FlushAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return new(this.writer.FlushAsync());
    }

    public void Dispose() => this.writer.Dispose();

    public ValueTask DisposeAsync()
    {
        this.writer.Dispose();

        return ValueTask.CompletedTask;
    }
}