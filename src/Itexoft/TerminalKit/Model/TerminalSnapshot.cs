// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Itexoft.TerminalKit;

/// <summary>
/// Complete serialized description of a console UI surface.
/// </summary>
public sealed class TerminalSnapshot
{
    /// <summary>
    /// Initializes the snapshot with the root node, state slices and input bindings.
    /// </summary>
    /// <param name="root">Root component node.</param>
    /// <param name="state">State slices consumed by the UI.</param>
    /// <param name="input">Key bindings recognized by the UI.</param>
    [JsonConstructor]
    public TerminalSnapshot(TerminalNode root, IReadOnlyList<TerminalStateSlice> state, IReadOnlyList<TerminalInputBinding> input)
    {
        this.Root = root ?? throw new ArgumentNullException(nameof(root));
        this.State = state ?? throw new ArgumentNullException(nameof(state));
        this.Input = input ?? throw new ArgumentNullException(nameof(input));
    }

    /// <summary>
    /// Gets the root component node.
    /// </summary>
    public TerminalNode Root { get; }

    /// <summary>
    /// Gets the state slices serialized alongside the tree.
    /// </summary>
    public IReadOnlyList<TerminalStateSlice> State { get; }

    /// <summary>
    /// Gets the logical input bindings.
    /// </summary>
    public IReadOnlyList<TerminalInputBinding> Input { get; }

    /// <summary>
    /// Serializes the snapshot to JSON.
    /// </summary>
    public string ToJson(JsonSerializerOptions? options = null) => JsonSerializer.Serialize(this, options ?? TerminalJsonOptions.Default);

    /// <summary>
    /// Deserializes a snapshot from JSON.
    /// </summary>
    /// <param name="json">JSON payload produced by <see cref="ToJson" />.</param>
    /// <param name="options">Optional serializer options.</param>
    public static TerminalSnapshot FromJson(string json, JsonSerializerOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new ArgumentException("JSON payload cannot be empty.", nameof(json));

        var snapshot = JsonSerializer.Deserialize<TerminalSnapshot>(json, options ?? TerminalJsonOptions.Default);

        if (snapshot == null)
            throw new InvalidOperationException("Unable to deserialize console UI snapshot.");

        return snapshot;
    }
}
