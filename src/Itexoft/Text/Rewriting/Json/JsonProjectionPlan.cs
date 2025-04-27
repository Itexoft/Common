// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Extensions;

namespace Itexoft.Text.Rewriting.Json;

public sealed class JsonProjectionPlan<T> where T : class
{
    private readonly Dictionary<string, JsonProjectionBinding<T>[]> bindings;
    private readonly Func<T> factory;

    internal JsonProjectionPlan(JsonProjectionBinding<T>[] bindings, Func<T> factory)
    {
        this.BindingArray = bindings ?? throw new ArgumentNullException(nameof(bindings));

        this.bindings = this.BindingArray.GroupBy(b => b.Pointer, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);

        this.factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    internal JsonProjectionBinding<T>[] BindingArray { get; }

    public static JsonProjectionPlan<T> FromConventions(JsonProjectionOptions? options = null)
    {
        var visited = new HashSet<Type>();

        return FromConventions(options, visited);
    }

    internal static JsonProjectionPlan<T> FromConventions(JsonProjectionOptions? options, HashSet<Type> visited)
    {
        visited.Required();

        var builder = new JsonProjectionBuilder<T>(options);
        builder.MapFromConventions(visited);

        return builder.Build();
    }

    internal T CreateInstance() => this.factory();

    internal bool TryAssign(T target, string pointer, in JsonProjectionValue value)
    {
        if (!this.bindings.TryGetValue(pointer, out var list))
            return false;

        for (var i = 0; i < list.Length; i++)
            list[i].Apply(target, value);

        return true;
    }

    internal T Project(string json) => JsonProjectionApplier.Project(json, this);

    internal IEnumerable<T> ProjectMany(string json) => JsonProjectionApplier.ProjectMany(json, this);
}

internal readonly struct JsonProjectionBinding<T>(string pointer, Action<T, JsonProjectionValue> apply)
{
    public string Pointer { get; } = pointer;

    public Action<T, JsonProjectionValue> Apply { get; } = apply;
}
