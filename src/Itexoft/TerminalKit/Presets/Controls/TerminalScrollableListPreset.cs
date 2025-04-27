// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.TerminalKit.Presets.Controls;

internal static class TerminalScrollableListPreset
{
    public static void Build(
        TerminalComponentBuilder<TerminalListView> builder,
        TerminalCrudOptions options,
        TerminalCrudHandlers handlers,
        StateHandle<object> itemsState,
        StateHandle<object> viewportState,
        TerminalEventKey activatedEvent,
        TerminalEventKey deletedEvent)
    {
        builder
            .BindState(l => l.DataSource, itemsState)
            .BindState(l => l.ViewportState, viewportState)
            .Set(l => l.ItemTemplate, options.ListItemTemplate)
            .Set(l => l.EmptyStateText, options.EmptyStateText)
            .BindEvent(activatedEvent, handlers.OpenEditor)
            .BindEvent(deletedEvent, handlers.RemoveItem);
    }
}