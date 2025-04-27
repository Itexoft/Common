// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Text.Rewriting.Json;

namespace Itexoft.Tests.Text.Rewriting.Json;

public sealed class JsonProjectionIntegrationTests
{
    [Test]
    public void ProjectionCapturesDoNotBlockStringCaptures()
    {
        ResponseMeta? rootMeta = null;
        ResponseMeta? responseMeta = null;
        var outputs = new List<string>();

        var plan = new JsonRewritePlanBuilder().CaptureObject("/", JsonProjectionPlan<ResponseMeta>.FromConventions(), meta => rootMeta ??= meta)
            .CaptureObject("/response", JsonProjectionPlan<ResponseMeta>.FromConventions(), meta => responseMeta ??= meta)
            .CaptureValue<string>("/output/0", outputs.Add).CaptureValue<string>("/output/1", outputs.Add).Build();

        Assert.That(plan.RuleCount, Is.EqualTo(4));
        Assert.That(plan.CaptureRules.Length, Is.EqualTo(2));
        Assert.That(plan.RuleTargets, Does.Contain("/output/0"));
        Assert.That(plan.RuleTargets, Does.Contain("/output/1"));

        const string json =
            """{"usage":{"input_tokens":1,"output_tokens":2},"response":{"usage":{"input_tokens":3,"output_tokens":4}},"output":["first","second"]}""";

        using var sink = new StringWriter();
        using var writer = new JsonRewriteWriter(sink, plan);

        writer.Write(json);
        writer.Flush();

        Assert.That(rootMeta, Is.Not.Null);
        Assert.That(rootMeta!.Usage.InputTokens, Is.EqualTo(1));
        Assert.That(rootMeta.Usage.OutputTokens, Is.EqualTo(2));

        Assert.That(responseMeta, Is.Not.Null);
        Assert.That(responseMeta!.Usage.InputTokens, Is.EqualTo(3));
        Assert.That(responseMeta.Usage.OutputTokens, Is.EqualTo(4));

        Assert.That(outputs, Is.EqualTo((string[])["first", "second"]));
        Assert.That(sink.ToString(), Is.EqualTo(json));
    }

    private sealed class ResponseMeta
    {
        public UsageInfo Usage { get; set; } = new();
    }

    private sealed class UsageInfo
    {
        public int InputTokens { get; set; }

        public int OutputTokens { get; set; }
    }
}
