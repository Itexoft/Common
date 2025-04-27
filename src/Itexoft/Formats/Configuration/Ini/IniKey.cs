// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Formats.Configuration.Ini;

public readonly struct IniKey
{
    private readonly ReadOnlyMemory<char> memory;

    internal IniKey(ReadOnlyMemory<char> memory)
    {
        this.memory = memory;
        this.Text = memory.IsEmpty ? string.Empty : memory.ToString();
    }

    public ReadOnlySpan<char> Span => this.memory.Span;

    public ReadOnlyMemory<char> Memory => this.memory;

    public string Text { get; }

    public bool IsEmpty => this.memory.IsEmpty;

    public override string ToString() => this.Text;
}
