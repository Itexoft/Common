// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.TerminalKit.Binding;

/// <summary>
/// Describes the shape of a change inside a bindable collection.
/// </summary>
public enum TerminalBindingListChangeType
{
    /// <summary>
    /// A new item has been appended or inserted.
    /// </summary>
    Add,

    /// <summary>
    /// An existing item has been removed.
    /// </summary>
    Remove,

    /// <summary>
    /// One item has been replaced with another.
    /// </summary>
    Replace,

    /// <summary>
    /// An item has been moved to a new index.
    /// </summary>
    Move,

    /// <summary>
    /// The collection has been cleared or massively updated.
    /// </summary>
    Reset,
}
