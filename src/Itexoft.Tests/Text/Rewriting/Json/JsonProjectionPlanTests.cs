// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Text.Rewriting.Json;

namespace Itexoft.Tests.Text.Rewriting.Json;

public sealed class JsonProjectionPlanTests
{
    [Test]
    public void FromConventionsMapsScalars()
    {
        var plan = JsonProjectionPlan<SimpleModel>.FromConventions(
            new()
            {
                PropertyNameStyle = JsonPropertyNameStyle.CamelCase,
            });

        var model = plan.Project("""{"name":"demo","count":5}""");

        Assert.That(model.Name, Is.EqualTo("demo"));
        Assert.That(model.Count, Is.EqualTo(5));
    }

    [Test]
    public void FromConventionsBuildsNestedPointers()
    {
        var plan = JsonProjectionPlan<ResponseMeta>.FromConventions();
        var model = plan.Project("""{"usage":{"input_tokens":3,"output_tokens":7}}""");

        Assert.That(model.Usage.InputTokens, Is.EqualTo(3));
        Assert.That(model.Usage.OutputTokens, Is.EqualTo(7));
    }

    [Test]
    public void FromConventionsThrowsForUnsupportedCollections() =>
        Assert.Throws<InvalidOperationException>(() => JsonProjectionPlan<UnsupportedModel>.FromConventions());

    private sealed class SimpleModel
    {
        public string Name { get; set; } = string.Empty;

        public int Count { get; set; }
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

    private sealed class UnsupportedModel
    {
        public List<string> Items { get; set; } = [];
    }
}
