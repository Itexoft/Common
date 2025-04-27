// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Linq.Expressions;

namespace Itexoft.TerminalKit;

/// <summary>
/// Shared extensions that simplify common binding patterns for component builders.
/// </summary>
internal static class TerminalComponentBuilderExtensions
{
    public static TerminalComponentBuilder<TComponent> BindState<TComponent, TState>(
        this TerminalComponentBuilder<TComponent> builder,
        Expression<Func<TComponent, string?>> property,
        StateHandle<TState> state)
        where TComponent : TerminalComponentDefinition => builder.Set(property, TerminalBindingPath.State(state));
}