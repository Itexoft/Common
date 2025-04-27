// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Itexoft.TerminalKit;

/// <summary>
/// Strongly typed alias for data-binding paths (e.g., metadata.owner).
/// </summary>
[JsonConverter(typeof(BindingKeyJsonConverter))]
public readonly struct DataBindingKey : IEquatable<DataBindingKey>
{
    private readonly string? _path;

    private DataBindingKey(string path) => this._path = Normalize(path);

    /// <summary>
    /// Gets the normalized textual representation of the binding path.
    /// </summary>
    public string Path => this._path ?? string.Empty;

    /// <summary>
    /// Gets an empty binding key that never points to a value.
    /// </summary>
    public static DataBindingKey Empty { get; } = new(string.Empty);

    /// <summary>
    /// Creates a binding key from the provided string literal.
    /// </summary>
    public static DataBindingKey From(string path) => new(path);

    /// <summary>
    /// Creates a binding key by analyzing the specified expression tree.
    /// </summary>
    public static DataBindingKey For<TModel>(Expression<Func<TModel, object?>> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        var segments = new Stack<string>();
        CollectSegments(selector.Body, segments);

        if (segments.Count == 0)
            throw new ArgumentException("Selector must access at least one property.", nameof(selector));

        return new(string.Join(".", segments));
    }

    /// <inheritdoc />
    public override string ToString() => this.Path;

    /// <inheritdoc />
    public bool Equals(DataBindingKey other) => string.Equals(this.Path, other.Path, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is DataBindingKey other && this.Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(this.Path);

    private static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        return path.Trim();
    }

    private static void CollectSegments(Expression expression, Stack<string> segments)
    {
        switch (expression)
        {
            case MemberExpression member:
                segments.Push(ToSegment(member.Member.Name));
                if (member.Expression != null)
                    CollectSegments(member.Expression, segments);

                break;
            case UnaryExpression unary when unary.NodeType is ExpressionType.Convert or ExpressionType.ConvertChecked:
                CollectSegments(unary.Operand, segments);

                break;
            case ParameterExpression:
                break;
            default:
                throw new ArgumentException("Only simple member access expressions are supported.", nameof(expression));
        }
    }

    private static string ToSegment(string name)
    {
        if (string.IsNullOrEmpty(name))
            return string.Empty;

        if (name.Length == 1)
            return name.ToLowerInvariant();

        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    private sealed class BindingKeyJsonConverter : JsonConverter<DataBindingKey>
    {
        public override DataBindingKey Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String)
                throw new JsonException("DataBindingKey must be represented as a string.");

            var value = reader.GetString() ?? string.Empty;

            return new(value);
        }

        public override void Write(Utf8JsonWriter writer, DataBindingKey value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.Path);
    }
}