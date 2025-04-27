// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Text;
using Itexoft.Text.Rewriting.Text.Dsl;

namespace Itexoft.Tests.Text.Rewriting.Text;

/// <summary>
/// Repro for "Destination is too short" thrown by TextRewriteEngine when flushing after replacements.
/// </summary>
public sealed class TextRewriteEngineRegressionTests
{
    [Test]
    public void Flush_WithLargeReplacement_DoesNotThrow()
    {
        // Arrange: regex that replaces tokens with a shorter literal, repeated many times.
        var kernel = TextKernel<object>.Compile(rules => { rules.Regex(@"token=[^\s]+", 256, name: "strip-token").Replace("token=[hidden]"); });

        var sb = new StringBuilder();

        for (var i = 0; i < 10_000; i++)
        {
            sb.Append("token=");
            sb.Append(Guid.NewGuid().ToString("N"));
            sb.Append(' ');
        }

        var input = sb.ToString();
        using var writer = new StringWriter();
        using var session = kernel.CreateSession(writer, new(), new() { RightWriteBlockSize = 0 });

        // Act + Assert: no exception on flush/dispose.
        session.Write(input);
        session.Flush();

        // ReSharper disable once DisposeOnUsingVariable
        session.Dispose();
    }
}
