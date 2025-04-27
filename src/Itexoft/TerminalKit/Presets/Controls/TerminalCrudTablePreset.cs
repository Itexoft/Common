// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.TerminalKit.Presets.Controls;

internal static class TerminalCrudTablePreset
{
    public static void Build(
        TerminalComponentBuilder<TerminalTableView> builder,
        TerminalCrudOptions options,
        IReadOnlyList<TerminalTableColumn> columns,
        TerminalCrudHandlers handlers,
        StateHandle<object> itemsState,
        StateHandle<object> viewportState,
        TerminalEventKey cellEditedEvent) => builder.BindState(t => t.DataSource, itemsState).BindState(t => t.ViewportState, viewportState)
        .Set(t => t.Columns, columns).BindEvent(cellEditedEvent, handlers.UpdateField);
}
