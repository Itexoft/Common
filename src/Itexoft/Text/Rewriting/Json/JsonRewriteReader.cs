// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Text.Rewriting.Json;

/// <summary>
/// <see cref="TextReader" /> that consumes JSON, applies a <see cref="JsonRewritePlan" />, and exposes the rewritten
/// JSON. The entire source is read and rewritten during construction before being exposed for consumption.
/// </summary>
public sealed class JsonRewriteReader : TextReader
{
    private readonly string processed;
    private int position;

    public JsonRewriteReader(TextReader underlying, JsonRewritePlan plan, JsonRewriteOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(underlying);
        ArgumentNullException.ThrowIfNull(plan);

        options ??= new();

        var sourceText = underlying.ReadToEnd();
        var writer = new StringWriter();

        var processor = new JsonRewriteWriter(writer, plan, options);
        processor.Write(sourceText);
        processor.Flush();
        this.processed = writer.ToString();
        this.position = 0;
    }

    public override int Read()
    {
        if (this.position >= this.processed.Length)
            return -1;

        return this.processed[this.position++];
    }

    public override int Read(char[] buffer, int index, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        var remaining = this.processed.Length - this.position;

        if (remaining <= 0)
            return 0;

        var toCopy = Math.Min(count, remaining);
        this.processed.CopyTo(this.position, buffer, index, toCopy);
        this.position += toCopy;

        return toCopy;
    }
}