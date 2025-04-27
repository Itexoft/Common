// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.TerminalKit.Dsl;

/// <summary>
/// Builds metadata-driven forms bound to a selected entity.
/// </summary>
public sealed class TerminalFormComposer
{
    private readonly TerminalComponentBuilder<TerminalMetadataForm> builder;
    private readonly List<TerminalFormFieldDefinition> fields = [];
    private readonly StateHandle<object> selectionHandle;

    internal TerminalFormComposer(TerminalComponentBuilder<TerminalMetadataForm> builder, StateHandle<object> selectionHandle)
    {
        this.builder = builder;
        this.selectionHandle = selectionHandle;
    }

    /// <summary>
    /// Gets the handle of the underlying form component.
    /// </summary>
    public TerminalComponentHandle<TerminalMetadataForm> Handle => this.builder.Handle;

    /// <summary>
    /// Adds a form field bound to the specified property.
    /// </summary>
    /// <param name="key">Binding key pointing to the property on the bound entity.</param>
    /// <param name="editor">Editor type used to capture user input.</param>
    /// <param name="configure">Optional callback for advanced field customization.</param>
    public TerminalFormComposer Field(DataBindingKey key, TerminalFormFieldEditor editor, Action<TerminalFormFieldBuilder>? configure = null)
    {
        var fieldBuilder = new TerminalFormFieldBuilder(key, editor);
        configure?.Invoke(fieldBuilder);
        this.fields.Add(fieldBuilder.Build());

        return this;
    }

    /// <summary>
    /// Binds a handler invoked when the form is submitted.
    /// </summary>
    public TerminalFormComposer OnSubmit(TerminalHandlerId handler)
    {
        this.builder.BindEvent(TerminalFormEvents.Submit, handler);

        return this;
    }

    /// <summary>
    /// Binds a handler invoked when the form is canceled.
    /// </summary>
    public TerminalFormComposer OnCancel(TerminalHandlerId handler)
    {
        this.builder.BindEvent(TerminalFormEvents.Cancel, handler);

        return this;
    }

    internal void Apply() => this.builder.Set(f => f.Fields, this.fields.ToArray()).BindState(f => f.BoundItem, this.selectionHandle);
}
