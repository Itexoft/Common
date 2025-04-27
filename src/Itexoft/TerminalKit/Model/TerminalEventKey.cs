// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.TerminalKit;

/// <summary>
/// Identifies an event exposed by a specific component type.
/// </summary>
public readonly record struct TerminalEventKey
{
    /// <summary>
    /// Initializes the key with the component and event names.
    /// </summary>
    /// <param name="component">The component identifier produced by <see cref="TerminalComponentRegistry" />.</param>
    /// <param name="event">The event name exposed by the component.</param>
    public TerminalEventKey(string component, string @event)
    {
        if (string.IsNullOrWhiteSpace(component))
            throw new ArgumentException("Component name cannot be empty.", nameof(component));

        if (string.IsNullOrWhiteSpace(@event))
            throw new ArgumentException("Event name cannot be empty.", nameof(@event));

        this.Component = component;
        this.Event = @event;
    }

    /// <summary>
    /// Gets the declarative component name.
    /// </summary>
    public string Component { get; }

    /// <summary>
    /// Gets the component event name.
    /// </summary>
    public string Event { get; }

    /// <summary>
    /// Creates a key for the specified component and event using the component attribute metadata.
    /// </summary>
    public static TerminalEventKey Create<TComponent>(string eventName)
        where TComponent : TerminalComponentDefinition
    {
        if (string.IsNullOrWhiteSpace(eventName))
            throw new ArgumentException("Event name cannot be empty.", nameof(eventName));

        var componentName = TerminalComponentRegistry.GetComponentName(typeof(TComponent));

        return new(componentName, eventName);
    }
}