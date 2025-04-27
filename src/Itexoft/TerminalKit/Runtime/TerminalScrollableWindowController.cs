// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.TerminalKit.Presets;
using Itexoft.TerminalKit.Rendering;

namespace Itexoft.TerminalKit;

/// <summary>
/// Encapsulates selection + viewport logic for scrollable regions so navigation can be wired declaratively.
/// </summary>
internal sealed class TerminalScrollableWindowController(TerminalViewportState viewport, Func<int> countProvider)
{
    private readonly Func<int> countProvider = countProvider ?? throw new ArgumentNullException(nameof(countProvider));
    private readonly TerminalViewportState viewport = viewport ?? throw new ArgumentNullException(nameof(viewport));

    public int Selection { get; private set; }

    public void MovePrevious() => this.SetSelection(this.Selection - 1);

    public void MoveNext() => this.SetSelection(this.Selection + 1);

    public void PageUp() => this.SetSelection(this.Selection - this.GetWindowSize());

    public void PageDown() => this.SetSelection(this.Selection + this.GetWindowSize());

    public void MoveTo(int index) => this.SetSelection(index);

    public void MoveToEnd() => this.SetSelection(this.countProvider() - 1);

    public void Sync()
    {
        var count = this.countProvider();

        if (count == 0)
        {
            this.Selection = 0;
            this.viewport.Offset = 0;
            Invalidate();

            return;
        }

        this.Selection = Math.Clamp(this.Selection, 0, count - 1);
        this.viewport.EnsureVisible(this.Selection, count);
        Invalidate();
    }

    private void SetSelection(int value)
    {
        var count = this.countProvider();

        if (count <= 0)
        {
            this.Selection = 0;
            this.viewport.Offset = 0;
            Invalidate();

            return;
        }

        this.Selection = Math.Clamp(value, 0, count - 1);
        this.viewport.EnsureVisible(this.Selection, count);
        Invalidate();
    }

    private int GetWindowSize() => this.viewport.WindowSize <= 0 ? 1 : this.viewport.WindowSize;

    private static void Invalidate() => TerminalDispatcher.Current?.Invalidate();
}
