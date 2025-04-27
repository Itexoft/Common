// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.TerminalKit.Presets.Controls;

internal static class TerminalCommandPanelPreset
{
    public static void Build(
        TerminalComponentBuilder<TerminalPanel> builder,
        string layout,
        string counterText,
        IReadOnlyList<TerminalShortcutHintDescriptor> hints)
    {
        builder.Set(p => p.Layout, layout)
            .AddChild<TerminalLabel>(label => label.Set(l => l.Text, counterText));

        foreach (var hint in hints)
            builder.AddChild<TerminalShortcutHint>(shortcut => shortcut.Set(h => h.Text, hint.Text));
    }
}