// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Text.Rewriting.Primitives.Pipeline;
using Itexoft.Text.Rewriting.Text.Dsl;

namespace Itexoft.Tests.Text.Rewriting.Text;

public sealed class RewritePipelineTests
{
    [Test]
    public void PipelineChainsTextStages()
    {
        var t1 = TextKernel<object>.Compile(dsl => dsl.Literal("A").Replace("B"));
        var t2 = TextKernel<object>.Compile(dsl => dsl.Literal("B").Replace("C"));
        var output = new StringWriter();

        using var pipeline = new RewritePipelineBuilder().AddText(t1, new()).AddText(t2, new()).Build(output);

        pipeline.Write("A".AsSpan());
        pipeline.Flush();

        Assert.That(output.ToString(), Is.EqualTo("C"));
    }
}
