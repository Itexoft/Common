// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Extensions;
using Itexoft.Text.Rewriting.Json.Dsl;
using Itexoft.Text.Rewriting.Text.Dsl;

namespace Itexoft.Text.Rewriting.Primitives.Pipeline;

/// <summary>
/// Builds streaming rewrite pipelines composed of Text and Json stages.
/// </summary>
public sealed class RewritePipelineBuilder
{
    private readonly List<Func<IPipelineStage, IPipelineStage>> stages = [];

    public RewritePipelineBuilder AddText<THandlers>(TextKernel<THandlers> kernel, THandlers handlers, TextRuntimeOptions? options = null)
        where THandlers : class
    {
        kernel.Required();

        this.stages.Add(next =>
        {
            var writer = new PipelineTextWriter(next);
            var session = kernel.CreateSession(writer, handlers, options);

            return new TextPipelineStage<THandlers>(session);
        });

        return this;
    }

    public RewritePipelineBuilder AddJson<THandlers>(JsonKernel<THandlers> kernel, THandlers handlers, JsonKernelOptions? options = null)
        where THandlers : class
    {
        kernel.Required();

        this.stages.Add(next =>
        {
            var writer = new PipelineTextWriter(next);
            var session = kernel.CreateSession(writer, handlers, options);

            return new JsonPipelineStage<THandlers>(session);
        });

        return this;
    }

    /// <summary>
    /// Finalizes the pipeline and returns a reusable entry point.
    /// </summary>
    public RewritePipeline Build(TextWriter output)
    {
        output.Required();

        IPipelineStage current = new TextWriterStage(output);

        for (var i = this.stages.Count - 1; i >= 0; i--)
            current = this.stages[i](current);

        return new(current);
    }
}
