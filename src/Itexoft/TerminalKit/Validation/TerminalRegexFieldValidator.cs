// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Text.RegularExpressions;

namespace Itexoft.TerminalKit.Validation;

public sealed class TerminalRegexFieldValidator : ITerminalFormFieldValidator
{
    private readonly string? message;
    private readonly Regex regex;

    public TerminalRegexFieldValidator(string pattern, string? message = null, RegexOptions options = RegexOptions.None)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            throw new ArgumentException("Pattern cannot be empty.", nameof(pattern));

        this.regex = new(pattern, options | RegexOptions.Compiled);
        this.message = message;
    }

    public string? Validate(TerminalFormFieldDefinition field, string? value)
    {
        if (string.IsNullOrEmpty(value))
            return null;

        return this.regex.IsMatch(value) ? null : this.message ?? $"Value '{value}' does not match pattern '{this.regex}'.";
    }
}
