// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Text;
using Itexoft.Threading;

namespace Itexoft.IO;

public class TextWriterCache(TextWriter writer, bool dispose) : TextWriter
{
    private readonly StringBuilder sb = new();

    public override Encoding Encoding => writer.Encoding;

    public override void Write(char value)
    {
        this.sb.Append(value);
        writer.Write(value);
    }

    public override void Write(char[] buffer, int index, int count)
    {
        this.sb.Append(buffer, index, count);
        writer.Write(buffer, index, count);
    }

    public override void Write(string? value)
    {
        if (value is null)
            return;

        this.sb.Append(value);
        writer.Write(value);
    }

    public override void Write(ReadOnlySpan<char> buffer)
    {
        this.sb.Append(buffer);
        writer.Write(buffer);
    }

    public override void WriteLine()
    {
        this.sb.AppendLine();
        writer.WriteLine();
    }

    public override void WriteLine(char value)
    {
        this.sb.Append(value);
        this.sb.AppendLine();
        writer.WriteLine(value);
    }

    public override void WriteLine(string? value)
    {
        if (value is null)
        {
            this.sb.AppendLine();
            writer.WriteLine();

            return;
        }

        this.sb.AppendLine(value);
        writer.WriteLine(value);
    }

    public override Task WriteAsync(char value)
    {
        this.sb.Append(value);

        return writer.WriteAsync(value);
    }

    public override Task WriteAsync(char[] buffer, int index, int count)
    {
        this.sb.Append(buffer, index, count);

        return writer.WriteAsync(buffer, index, count);
    }

    public override Task WriteAsync(string? value)
    {
        if (value != null)
            this.sb.Append(value);

        return writer.WriteAsync(value);
    }

    public Task WriteAsync(ReadOnlyMemory<char> buffer, CancelToken cancelToken = default)
    {
        this.sb.Append(buffer.Span);

        using (cancelToken.Bridge(out var token))
            return writer.WriteAsync(buffer, token);
    }

    public override Task WriteLineAsync()
    {
        this.sb.AppendLine();

        return writer.WriteLineAsync();
    }

    public override Task WriteLineAsync(char value)
    {
        this.sb.Append(value);
        this.sb.AppendLine();

        return writer.WriteLineAsync(value);
    }

    public override Task WriteLineAsync(string? value)
    {
        if (value != null)
            this.sb.AppendLine(value);
        else
            this.sb.AppendLine();

        return writer.WriteLineAsync(value);
    }

    public Task WriteLineAsync(ReadOnlyMemory<char> buffer, CancelToken cancelToken = default)
    {
        this.sb.Append(buffer.Span);
        this.sb.AppendLine();

        using (cancelToken.Bridge(out var token))
            return writer.WriteLineAsync(buffer, token);
    }

    public override Task FlushAsync() => writer.FlushAsync();

    public override void Flush() => writer.Flush();

    protected override void Dispose(bool disposing)
    {
        if (dispose)
            writer.Dispose();

        base.Dispose(disposing);
    }

    public override string ToString() => this.sb.ToString();
}
