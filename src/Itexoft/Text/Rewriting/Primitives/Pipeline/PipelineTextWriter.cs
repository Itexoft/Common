// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Text;
using Itexoft.Extensions;

namespace Itexoft.Text.Rewriting.Primitives.Pipeline;

internal sealed class PipelineTextWriter(IPipelineStage next) : TextWriter
{
    public override Encoding Encoding => Encoding.UTF8;

    public override void Write(char value)
    {
        Span<char> buf = stackalloc char[1];
        buf[0] = value;
        next.Write(buf);
    }

    public override void Write(char[] buffer, int index, int count)
    {
        buffer.Required();
        next.Write(buffer.AsSpan(index, count));
    }

    public override void Write(string? value)
    {
        if (value is null)
            return;

        next.Write(value.AsSpan());
    }

    public override Task WriteAsync(char value)
    {
        this.Write(value);

        return Task.CompletedTask;
    }

    public override Task WriteAsync(char[] buffer, int index, int count)
    {
        this.Write(buffer, index, count);

        return Task.CompletedTask;
    }

    public override Task WriteAsync(string? value)
    {
        if (value is null)
            return Task.CompletedTask;

        return next.WriteAsync(value.AsMemory(), default).AsTask();
    }

    public override void Flush() => next.Flush();

    public override Task FlushAsync() => next.FlushAsync(default).AsTask();
}
