// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Formats.Configuration.Ini;

public readonly struct IniValue
{
    private readonly ReadOnlyMemory<char> memory;

    internal IniValue(ReadOnlyMemory<char> memory) => this.memory = memory;

    public ReadOnlySpan<char> Span => this.memory.Span;

    public ReadOnlyMemory<char> Memory => this.memory;

    public bool IsEmpty => this.memory.IsEmpty;

    public override string ToString() => this.memory.ToString();

    public bool TryGetBoolean(out bool value) => IniValueParsing.TryParseBoolean(this.Span, out value);

    public bool TryGetInt32(out int value) => IniValueParsing.TryParseInt32(this.Span, out value);

    public bool TryGetInt64(out long value) => IniValueParsing.TryParseInt64(this.Span, out value);

    public bool TryGetNumber(out long value) => IniValueParsing.TryParseNumber(this.Span, out value);
}
