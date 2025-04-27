// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Text.Rewriting.Json;

namespace Itexoft.Tests.Text.Rewriting.Json;

public sealed class JsonPropertyNameStyleTests
{
    [Test]
    public void SnakeCaseProducesUnderscoredNames()
    {
        Assert.That(JsonPropertyNameHelper.Convert("Test", JsonPropertyNameStyle.SnakeCase), Is.EqualTo("test"));
        Assert.That(JsonPropertyNameHelper.Convert("TestCase", JsonPropertyNameStyle.SnakeCase), Is.EqualTo("test_case"));
    }

    [Test]
    public void CamelCaseLowercasesFirstLetter() => Assert.That(
        JsonPropertyNameHelper.Convert("TestCase", JsonPropertyNameStyle.CamelCase),
        Is.EqualTo("testCase"));

    [Test]
    public void ExactKeepsOriginalName() => Assert.That(
        JsonPropertyNameHelper.Convert("TestCase", JsonPropertyNameStyle.Exact),
        Is.EqualTo("TestCase"));
}
