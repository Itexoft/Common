// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.TerminalKit.Binding;

/// <summary>
/// Payload describing a mutation inside a bindable list.
/// </summary>
public sealed class TerminalBindingListChangedEventArgs<T> : EventArgs
{
    /// <summary>
    /// Initializes the payload with information about the mutation.
    /// </summary>
    /// <param name="changeType">Type of operation that occurred.</param>
    /// <param name="index">Affected index or <c>-1</c> for a reset.</param>
    /// <param name="item">The new item involved in the change, if any.</param>
    /// <param name="oldItem">The previous item for replace/remove mutations.</param>
    public TerminalBindingListChangedEventArgs(TerminalBindingListChangeType changeType, int index, T? item, T? oldItem = default)
    {
        this.ChangeType = changeType;
        this.Index = index;
        this.Item = item;
        this.OldItem = oldItem;
    }

    /// <summary>
    /// Gets the type of change that occurred.
    /// </summary>
    public TerminalBindingListChangeType ChangeType { get; }

    /// <summary>
    /// Gets the index affected by the operation.
    /// </summary>
    public int Index { get; }

    /// <summary>
    /// Gets the new item for add/replace operations.
    /// </summary>
    public T? Item { get; }

    /// <summary>
    /// Gets the previous item for replace/remove operations.
    /// </summary>
    public T? OldItem { get; }
}
