// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Globalization;
using Itexoft.Text.Rewriting.Json;

namespace Itexoft.Tests.Text.Rewriting.Json;

public sealed class JsonProjectionBuilderTests
{
    [Test]
    public void MapWithSourceAssignsTopLevelProperty()
    {
        var plan = new JsonProjectionBuilder<Simple>(new() { PropertyNameStyle = JsonPropertyNameStyle.SnakeCase }).Map(x => x.Name, x => x.Name)
            .Build();

        var model = plan.Project("""{"name":"value"}""");

        Assert.That(model.Name, Is.EqualTo("value"));
    }

    [Test]
    public void MapWithSourceReadsNestedProperty()
    {
        var plan = new JsonProjectionBuilder<NestedParent>(new() { PropertyNameStyle = JsonPropertyNameStyle.SnakeCase }).Map(
            x => x.Alias,
            x => x.Child.Value,
            s => int.Parse(s, CultureInfo.InvariantCulture)).Build();

        var model = plan.Project("""{"child":{"value":42}}""");

        Assert.That(model.Alias, Is.EqualTo(42));
    }

    [TestCase(JsonPropertyNameStyle.SnakeCase, "/child/value"), TestCase(JsonPropertyNameStyle.CamelCase, "/child/value"),
     TestCase(JsonPropertyNameStyle.Exact, "/Child/Value")]
    public void MapWithSourceRespectsPropertyNameStyle(JsonPropertyNameStyle style, string expectedPointer)
    {
        var plan = new JsonProjectionBuilder<NestedParent>(new() { PropertyNameStyle = style }).Map(x => x.Alias, x => x.Child.Value).Build();

        Assert.That(plan.BindingArray.Single().Pointer, Is.EqualTo(expectedPointer));
    }

    [Test]
    public void MapAllowsMultipleSourcesForSameTarget()
    {
        var plan = new JsonProjectionBuilder<ItemHolder>().Map(x => x.ItemId, x => x.ItemId).Map(x => x.ItemId, x => x.Item.Id).Build();

        var fromRoot = plan.Project("""{"item_id":"root"}""");
        Assert.That(fromRoot.ItemId, Is.EqualTo("root"));

        var fromNested = plan.Project("""{"item":{"id":"nested"}}""");
        Assert.That(fromNested.ItemId, Is.EqualTo("nested"));

        var both = plan.Project("""{"item_id":"root","item":{"id":"nested"}}""");
        Assert.That(both.ItemId, Is.EqualTo("nested"));
    }

    [Test]
    public void MapThrowsOnInvalidSourceExpression()
    {
        var builder = new JsonProjectionBuilder<Simple>();

        Assert.Throws<InvalidOperationException>(() => builder.Map(x => x.Name, x => x.ToString()));
    }

    [Test]
    public void MapAndMapObjectApplyCustomConverter()
    {
        var childPlan = new JsonProjectionBuilder<Nested>().Map(x => x.Value, "/value").Build();

        var parentPlan = new JsonProjectionBuilder<Parent>().Map(x => x.Count, "/count", s => int.Parse(s, CultureInfo.InvariantCulture) * 2)
            .MapObject(x => x.Child, childPlan).Build();

        var model = parentPlan.Project("""{"count":"2","child":{"value":5}}""");

        Assert.That(model.Count, Is.EqualTo(4));
        Assert.That(model.Child.Value, Is.EqualTo(5));
    }

    [Test]
    public void DuplicatePointersThrow()
    {
        var builder = new JsonProjectionBuilder<Parent>().Map(x => x.Count, "/value");

        Assert.Throws<InvalidOperationException>(() => builder.Map(x => x.Name, "/value"));
    }

    [Test]
    public void InvalidExpressionThrows()
    {
        var builder = new JsonProjectionBuilder<Parent>();

        Assert.Throws<InvalidOperationException>(() => builder.Map(x => x.ToString(), "/value"));
    }

    private sealed class Parent
    {
        public string Name { get; set; } = string.Empty;

        public int Count { get; set; }

        public Nested Child { get; set; } = new();
    }

    private sealed class Simple
    {
        public string Name { get; set; } = string.Empty;
    }

    private sealed class NestedParent
    {
        public NestedChild Child { get; set; } = new();

        public int Alias { get; set; }
    }

    private sealed class NestedChild
    {
        public int Value { get; set; }
    }

    private sealed class ItemHolder
    {
        public string ItemId { get; set; } = string.Empty;

        public Item Item { get; set; } = new();
    }

    private sealed class Item
    {
        public string Id { get; set; } = string.Empty;
    }

    private sealed class Nested
    {
        public int Value { get; set; }
    }
}
