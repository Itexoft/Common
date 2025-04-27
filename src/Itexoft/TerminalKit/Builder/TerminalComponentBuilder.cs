// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Linq.Expressions;

namespace Itexoft.TerminalKit;

/// <summary>
/// Fluent builder for a single component node.
/// </summary>
internal sealed class TerminalComponentBuilder<TComponent>
    where TComponent : TerminalComponentDefinition
{
    private readonly TerminalBuildContext _context;
    private readonly TerminalNode _node;

    internal TerminalComponentBuilder(TerminalNode node, TerminalBuildContext context)
    {
        this._node = node ?? throw new ArgumentNullException(nameof(node));
        this._context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public TerminalComponentHandle<TComponent> Handle => new(this._node.Id);

    public TerminalComponentBuilder<TComponent> WithId(string id)
    {
        this._node.SetId(id);
        this._context.Register(this._node);

        return this;
    }

    public TerminalComponentBuilder<TComponent> Set<TValue>(Expression<Func<TComponent, TValue>> property, TValue value)
    {
        var propertyName = TerminalComponentPropertyBinder.GetMemberName(property);
        this._node.SetProperty(propertyName, value);

        return this;
    }

    public TerminalComponentBuilder<TComponent> BindEvent(TerminalEventKey eventKey, string handler)
    {
        if (string.IsNullOrWhiteSpace(handler))
            throw new ArgumentException("Handler name cannot be empty.", nameof(handler));

        this._node.AddEvent(new(eventKey, handler));

        return this;
    }

    public TerminalComponentBuilder<TComponent> BindEvent(TerminalEventKey eventKey, TerminalHandlerId handler) =>
        this.BindEvent(eventKey, handler.Name);

    public TerminalComponentBuilder<TComponent> AddChild<TChild>(Action<TerminalComponentBuilder<TChild>> configure)
        where TChild : TerminalComponentDefinition
    {
        ArgumentNullException.ThrowIfNull(configure);
        var childBuilder = this.CreateChildBuilder<TChild>();
        configure(childBuilder);

        return this;
    }

    public TerminalComponentHandle<TChild> AddChildHandle<TChild>(Action<TerminalComponentBuilder<TChild>> configure)
        where TChild : TerminalComponentDefinition
    {
        ArgumentNullException.ThrowIfNull(configure);
        var childBuilder = this.CreateChildBuilder<TChild>();
        configure(childBuilder);

        return childBuilder.Handle;
    }

    private TerminalComponentBuilder<TChild> CreateChildBuilder<TChild>()
        where TChild : TerminalComponentDefinition
    {
        var childNode = this._node.AddChild(typeof(TChild));
        this._context.Register(childNode);

        return new(childNode, this._context);
    }
}