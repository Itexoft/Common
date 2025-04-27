// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Text.Json;
using Itexoft.Text.Rewriting.Json;

namespace Itexoft.Tests.Text.Rewriting.Json;

public sealed class JsonRewriteReaderTests
{
    [Test]
    public void ReaderAppliesPlanOnConstruction()
    {
        const string source = "{\"user\":{\"name\":\"John\",\"token\":\"abc\"}}";
        var plan = new JsonRewritePlanBuilder().ReplaceValue("/user/token", "***").Build();

        using var reader = new JsonRewriteReader(new StringReader(source), plan);
        var result = reader.ReadToEnd();

        using var doc = JsonDocument.Parse(result);
        var user = doc.RootElement.GetProperty("user");
        Assert.That(user.GetProperty("token").GetString(), Is.EqualTo("***"));
        Assert.That(user.GetProperty("name").GetString(), Is.EqualTo("John"));
    }
}
