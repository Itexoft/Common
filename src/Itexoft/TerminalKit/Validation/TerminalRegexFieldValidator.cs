// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Text.RegularExpressions;

namespace Itexoft.TerminalKit.Validation;

public sealed class TerminalRegexFieldValidator : ITerminalFormFieldValidator
{
    private readonly string? _message;
    private readonly Regex _regex;

    public TerminalRegexFieldValidator(string pattern, string? message = null, RegexOptions options = RegexOptions.None)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            throw new ArgumentException("Pattern cannot be empty.", nameof(pattern));

        this._regex = new(pattern, options | RegexOptions.Compiled);
        this._message = message;
    }

    public string? Validate(TerminalFormFieldDefinition field, string? value)
    {
        if (string.IsNullOrEmpty(value))
            return null;

        return this._regex.IsMatch(value)
            ? null
            : this._message ?? $"Value '{value}' does not match pattern '{this._regex}'.";
    }
}