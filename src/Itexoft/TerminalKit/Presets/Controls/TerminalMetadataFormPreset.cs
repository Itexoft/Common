// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.TerminalKit.Presets.Controls;

internal static class TerminalMetadataFormPreset
{
    public static void Build(
        TerminalComponentBuilder<TerminalMetadataForm> builder,
        TerminalCrudOptions options,
        IReadOnlyList<TerminalFormFieldDefinition> fields,
        StateHandle<object> selectionHandle,
        TerminalCrudHandlers handlers,
        TerminalEventKey submitEvent,
        TerminalEventKey cancelEvent)
    {
        builder
            .Set(f => f.Fields, fields)
            .BindState(f => f.BoundItem, selectionHandle)
            .BindEvent(submitEvent, handlers.SaveMetadata)
            .BindEvent(cancelEvent, handlers.CloseDialog);
    }
}