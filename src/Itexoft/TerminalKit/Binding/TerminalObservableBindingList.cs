// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Collections;

namespace Itexoft.TerminalKit.Binding;

/// <summary>
/// Observable list wrapper that emits change notifications for binding scenarios.
/// </summary>
public sealed class TerminalObservableBindingList<T> : IList<T>
{
    private readonly IList<T> inner;

    /// <summary>
    /// Initializes the list with a fresh <see cref="List{T}" /> instance.
    /// </summary>
    public TerminalObservableBindingList() : this(new List<T>()) { }

    /// <summary>
    /// Wraps an existing list so it can emit change notifications.
    /// </summary>
    /// <param name="source">The list to observe.</param>
    public TerminalObservableBindingList(IList<T> source) => this.inner = source ?? throw new ArgumentNullException(nameof(source));

    /// <summary>
    /// Gets a snapshot-friendly view of the items.
    /// </summary>
    public IReadOnlyList<T> Items => this.inner as IReadOnlyList<T> ?? this.inner.ToArray();

    /// <summary>
    /// Gets or sets the item at the specified index.
    /// </summary>
    /// <param name="index">The zero-based index.</param>
    public T this[int index]
    {
        get => this.inner[index];
        set
        {
            var previous = this.inner[index];
            this.inner[index] = value;
            this.OnListChanged(new(TerminalBindingListChangeType.Replace, index, value, previous));
        }
    }

    /// <summary>
    /// Gets the number of elements contained in the list.
    /// </summary>
    public int Count => this.inner.Count;

    /// <summary>
    /// Gets a value indicating whether the list is read-only.
    /// </summary>
    public bool IsReadOnly => this.inner.IsReadOnly;

    /// <summary>
    /// Appends an item to the end of the list.
    /// </summary>
    /// <param name="item">Item to add.</param>
    public void Add(T item)
    {
        this.inner.Add(item);
        this.OnListChanged(new(TerminalBindingListChangeType.Add, this.inner.Count - 1, item));
    }

    /// <summary>
    /// Removes all items from the list.
    /// </summary>
    public void Clear()
    {
        if (this.inner.Count == 0)
            return;

        this.inner.Clear();
        this.OnListChanged(new(TerminalBindingListChangeType.Reset, -1, default));
    }

    /// <summary>
    /// Determines whether the list contains a specific value.
    /// </summary>
    /// <param name="item">Item to locate.</param>
    public bool Contains(T item) => this.inner.Contains(item);

    /// <summary>
    /// Copies the elements of the list to an array, starting at the specified array index.
    /// </summary>
    /// <param name="array">Destination array.</param>
    /// <param name="arrayIndex">Starting index inside the destination array.</param>
    public void CopyTo(T[] array, int arrayIndex) => this.inner.CopyTo(array, arrayIndex);

    /// <summary>
    /// Returns an enumerator that iterates through the list.
    /// </summary>
    public IEnumerator<T> GetEnumerator() => this.inner.GetEnumerator();

    /// <summary>
    /// Searches for the specified object and returns the zero-based index.
    /// </summary>
    /// <param name="item">Item to locate.</param>
    public int IndexOf(T item) => this.inner.IndexOf(item);

    /// <summary>
    /// Inserts an item at the specified index.
    /// </summary>
    /// <param name="index">Target index.</param>
    /// <param name="item">Item to insert.</param>
    public void Insert(int index, T item)
    {
        this.inner.Insert(index, item);
        this.OnListChanged(new(TerminalBindingListChangeType.Add, index, item));
    }

    /// <summary>
    /// Removes the first occurrence of the specified object.
    /// </summary>
    /// <param name="item">Item to remove.</param>
    public bool Remove(T item)
    {
        var index = this.inner.IndexOf(item);

        if (index < 0)
            return false;

        this.inner.RemoveAt(index);
        this.OnListChanged(new(TerminalBindingListChangeType.Remove, index, item));

        return true;
    }

    /// <summary>
    /// Removes the item at the specified index.
    /// </summary>
    /// <param name="index">Index of the item to remove.</param>
    public void RemoveAt(int index)
    {
        var removed = this.inner[index];
        this.inner.RemoveAt(index);
        this.OnListChanged(new(TerminalBindingListChangeType.Remove, index, removed));
    }

    IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();

    /// <summary>
    /// Occurs whenever the underlying list mutates.
    /// </summary>
    public event EventHandler<TerminalBindingListChangedEventArgs<T>>? ListChanged;

    /// <summary>
    /// Moves an item from one index to another.
    /// </summary>
    /// <param name="oldIndex">Original index.</param>
    /// <param name="newIndex">Destination index.</param>
    public void Move(int oldIndex, int newIndex)
    {
        if (oldIndex == newIndex)
            return;

        var item = this.inner[oldIndex];
        this.inner.RemoveAt(oldIndex);
        this.inner.Insert(newIndex, item);
        this.OnListChanged(new(TerminalBindingListChangeType.Move, newIndex, item));
    }

    private void OnListChanged(TerminalBindingListChangedEventArgs<T> args) => this.ListChanged?.Invoke(this, args);
}
