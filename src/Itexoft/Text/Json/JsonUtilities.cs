// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Itexoft.Text;

public static class JsonUtilities
{
    private static readonly JsonSerializerOptions defaultOptions = CreateOptions(writeIndented: false);
    private static readonly JsonSerializerOptions indentedOptions = CreateOptions(writeIndented: true);
    private static readonly JsonSerializerOptions camelCaseOptions = CreateOptions(false, JsonNamingPolicy.CamelCase);
    private static readonly JsonSerializerOptions camelCaseIndentedOptions = CreateOptions(true, JsonNamingPolicy.CamelCase);

    public static string Serialize(object? value) => JsonSerializer.Serialize(value, defaultOptions);

    public static string SerializeIndented(object? value) => JsonSerializer.Serialize(value, indentedOptions);

    public static string SerializeCamelCase(object? value) => JsonSerializer.Serialize(value, camelCaseOptions);

    public static string SerializeCamelCaseIndented(object? value) => JsonSerializer.Serialize(value, camelCaseIndentedOptions);

    public static JsonElement SerializeToElement<T>(T value) => JsonSerializer.SerializeToElement(value, defaultOptions);

    public static JsonElement SerializeToCamelCaseElement<T>(T value) => JsonSerializer.SerializeToElement(value, camelCaseOptions);

    public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, defaultOptions);

    public static T? DeserializeCamelCase<T>(string json) => JsonSerializer.Deserialize<T>(json, camelCaseOptions);

    public static T? Deserialize<T>(JsonNode node) => Deserialize<T>(node.ToJsonString(defaultOptions));

    public static T? DeserializeCamelCase<T>(JsonNode node) => DeserializeCamelCase<T>(node.ToJsonString(camelCaseOptions));

    public static JsonElement? ToElement(JsonNode? node)
    {
        if (node is null)
            return null;

        using var document = JsonDocument.Parse(node.ToJsonString(defaultOptions));

        return document.RootElement.Clone();
    }

    public static string? ToJsonString(JsonNode? node, bool indented = false)
    {
        if (node?.GetValueKind() == JsonValueKind.String)
            return node.GetValue<string>();
        else
            return node?.ToJsonString(indented ? indentedOptions : defaultOptions) ?? null;
    }

    private static JsonSerializerOptions CreateOptions(bool writeIndented, JsonNamingPolicy? namingPolicy = null)
    {
        var options = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = namingPolicy,
            WriteIndented = writeIndented,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        options.TypeInfoResolverChain.Add(new DefaultJsonTypeInfoResolver());

        return options;
    }
}
