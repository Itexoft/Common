// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.TerminalKit.Dsl;

/// <summary>
/// Fluent helper for wiring key gestures to logical actions.
/// </summary>
public sealed class TerminalInputComposer<TScreen> where TScreen : TerminalComponentDefinition
{
    private readonly TerminalUiBuilder<TScreen> builder;

    internal TerminalInputComposer(TerminalUiBuilder<TScreen> builder) => this.builder = builder;

    /// <summary>
    /// Maps a free-form accelerator gesture (e.g., CTRL+S) to an action.
    /// </summary>
    public TerminalInputComposer<TScreen> Accelerator(string gesture, TerminalActionId action)
    {
        this.builder.BindInput(TerminalNavigationMode.Accelerator, gesture, action);

        return this;
    }

    /// <summary>
    /// Maps numeric input (e.g., hotkeys 1-9) to an action.
    /// </summary>
    public TerminalInputComposer<TScreen> Numeric(string gesture, TerminalActionId action)
    {
        this.builder.BindInput(TerminalNavigationMode.Numeric, gesture, action);

        return this;
    }

    /// <summary>
    /// Maps arrow/navigation keys to actions.
    /// </summary>
    public TerminalInputComposer<TScreen> Arrow(string gesture, TerminalActionId action)
    {
        this.builder.BindInput(TerminalNavigationMode.Arrow, gesture, action);

        return this;
    }
}
