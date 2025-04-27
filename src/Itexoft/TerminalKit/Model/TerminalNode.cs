// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Text.Json.Serialization;

namespace Itexoft.TerminalKit;

/// <summary>
/// Serializable representation of a component instance inside the UI tree.
/// </summary>
public sealed class TerminalNode
{
    private readonly List<TerminalNode> _children = [];
    private readonly List<TerminalEventBinding> _events = [];
    private readonly Dictionary<string, object?> _properties = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a node with explicit component metadata, children and properties.
    /// </summary>
    /// <param name="id">Identifier assigned to the node.</param>
    /// <param name="component">Declarative component name.</param>
    /// <param name="componentType">Fully qualified CLR type name.</param>
    /// <param name="properties">Serialized property bag.</param>
    /// <param name="children">Child nodes.</param>
    /// <param name="events">Event bindings declared on the node.</param>
    [JsonConstructor]
    public TerminalNode(
        string id,
        string component,
        string componentType,
        IReadOnlyDictionary<string, object?>? properties,
        IReadOnlyList<TerminalNode>? children,
        IReadOnlyList<TerminalEventBinding>? events)
    {
        if (string.IsNullOrWhiteSpace(component))
            throw new ArgumentException("Component name cannot be empty.", nameof(component));

        if (string.IsNullOrWhiteSpace(componentType))
            throw new ArgumentException("Component type cannot be empty.", nameof(componentType));

        this.Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id;
        this.Component = component;
        this.ComponentType = componentType;

        if (properties != null)
            foreach (var pair in properties)
                this._properties[pair.Key] = pair.Value;

        if (children != null)
            this._children.AddRange(children);

        if (events != null)
            this._events.AddRange(events);
    }

    private TerminalNode(string component, string componentType)
    {
        this.Component = component;
        this.ComponentType = componentType;
        this.Id = Guid.NewGuid().ToString("N");
    }

    /// <summary>
    /// Gets the unique identifier assigned to the node.
    /// </summary>
    public string Id { get; private set; }

    /// <summary>
    /// Gets the declarative component name (e.g., listView).
    /// </summary>
    public string Component { get; }

    /// <summary>
    /// Gets the fully qualified CLR type name.
    /// </summary>
    public string ComponentType { get; }

    /// <summary>
    /// Gets the serialized property bag.
    /// </summary>
    public IReadOnlyDictionary<string, object?> Properties => this._properties;

    /// <summary>
    /// Gets the child nodes that make up the component tree.
    /// </summary>
    public IReadOnlyList<TerminalNode> Children => this._children;

    /// <summary>
    /// Gets the event bindings attached to this node.
    /// </summary>
    public IReadOnlyList<TerminalEventBinding> Events => this._events;

    internal static TerminalNode Create(Type componentType)
    {
        ArgumentNullException.ThrowIfNull(componentType);
        var componentName = TerminalComponentRegistry.GetComponentName(componentType);

        return new(componentName, componentType.FullName ?? componentType.Name);
    }

    internal void SetId(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Id cannot be empty.", nameof(id));

        this.Id = id;
    }

    internal void SetProperty(string name, object? value)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Property name cannot be empty.", nameof(name));

        this._properties[name] = value;
    }

    internal TerminalNode AddChild(Type componentType)
    {
        var child = Create(componentType);
        this._children.Add(child);

        return child;
    }

    internal void AddEvent(TerminalEventBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        this._events.Add(binding);
    }
}