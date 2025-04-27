// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Linq.Expressions;
using Itexoft.Extensions;

namespace Itexoft.TerminalKit.Dsl;

/// <summary>
/// Public wrapper over the internal component builder for advanced customization scenarios.
/// </summary>
public sealed class TerminalComponentComposer<TComponent> where TComponent : TerminalComponentDefinition
{
    private readonly TerminalComponentBuilder<TComponent> builder;

    internal TerminalComponentComposer(TerminalComponentBuilder<TComponent> builder) =>
        this.builder = builder ?? throw new ArgumentNullException(nameof(builder));

    /// <summary>
    /// Gets the handle associated with the current component node.
    /// </summary>
    public TerminalComponentHandle<TComponent> Handle => this.builder.Handle;

    /// <summary>
    /// Overrides the auto-generated component identifier.
    /// </summary>
    /// <param name="id">Identifier that can be referenced from bindings.</param>
    public TerminalComponentComposer<TComponent> WithId(string id)
    {
        this.builder.WithId(id);

        return this;
    }

    /// <summary>
    /// Assigns a strongly typed property on the component.
    /// </summary>
    /// <param name="property">Expression pointing to the property to configure.</param>
    /// <param name="value">Value that should be serialized into the snapshot.</param>
    public TerminalComponentComposer<TComponent> Set<TValue>(Expression<Func<TComponent, TValue>> property, TValue value)
    {
        this.builder.Set(property, value);

        return this;
    }

    /// <summary>
    /// Binds a typed handler identifier to a component event.
    /// </summary>
    /// <param name="eventKey">Event exposed by the component.</param>
    /// <param name="handler">Handler identifier defined by the host.</param>
    public TerminalComponentComposer<TComponent> On(TerminalEventKey eventKey, TerminalHandlerId handler)
    {
        this.builder.BindEvent(eventKey, handler);

        return this;
    }

    /// <summary>
    /// Binds a raw handler name to a component event.
    /// </summary>
    public TerminalComponentComposer<TComponent> On(TerminalEventKey eventKey, string handler)
    {
        this.builder.BindEvent(eventKey, handler);

        return this;
    }

    /// <summary>
    /// Adds a child component configured by the supplied callback.
    /// </summary>
    public TerminalComponentComposer<TComponent> AddChild<TChild>(Action<TerminalComponentComposer<TChild>> configure)
        where TChild : TerminalComponentDefinition
    {
        configure.Required();
        this.builder.AddChild<TChild>(child => configure(new(child)));

        return this;
    }

    /// <summary>
    /// Adds a child component and returns its handle for later references.
    /// </summary>
    public TerminalComponentHandle<TChild> AddChildHandle<TChild>(Action<TerminalComponentComposer<TChild>> configure)
        where TChild : TerminalComponentDefinition
    {
        configure.Required();

        return this.builder.AddChildHandle<TChild>(child => { configure(new(child)); });
    }
}
