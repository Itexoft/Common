// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.TerminalKit.Presets;

/// <summary>
/// Shared viewport window for scrollable regions.
/// </summary>
public sealed class TerminalViewportState
{
    /// <summary>
    /// Gets or sets the zero-based index of the first visible item.
    /// </summary>
    public int Offset { get; set; }

    /// <summary>
    /// Gets or sets the number of items displayed at once.
    /// </summary>
    public int WindowSize { get; set; } = 10;

    /// <summary>
    /// Adjusts the viewport so that the specified index remains visible.
    /// </summary>
    /// <param name="index">Target index that should be visible.</param>
    /// <param name="totalCount">Total number of items in the collection.</param>
    public void EnsureVisible(int index, int totalCount)
    {
        if (this.WindowSize <= 0)
            this.WindowSize = 1;

        if (totalCount <= this.WindowSize)
        {
            this.Offset = 0;

            return;
        }

        if (index < this.Offset)
        {
            this.Offset = index;

            return;
        }

        var lastVisibleIndex = this.Offset + this.WindowSize - 1;
        if (index > lastVisibleIndex)
            this.Offset = index - this.WindowSize + 1;

        var maxOffset = totalCount - this.WindowSize;
        if (this.Offset > maxOffset)
            this.Offset = maxOffset;

        if (this.Offset < 0)
            this.Offset = 0;
    }
}