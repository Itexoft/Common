// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Text.RegularExpressions;

namespace Itexoft.TerminalKit.Validation;

/// <summary>
/// Validates string input using a regular expression.
/// </summary>
public sealed class TerminalRegexAttribute : TerminalValidationAttribute
{
    /// <summary>
    /// Initializes the attribute with the required pattern.
    /// </summary>
    /// <param name="pattern">Regular expression applied to user input.</param>
    public TerminalRegexAttribute(string pattern) => this.Pattern = pattern;

    /// <summary>
    /// Gets the regular expression pattern.
    /// </summary>
    public string Pattern { get; }

    /// <summary>
    /// Gets or sets the custom validation message.
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    /// Gets or sets the regex options applied to the pattern.
    /// </summary>
    public RegexOptions Options { get; init; }

    /// <inheritdoc />
    public override ITerminalFormFieldValidator CreateValidator() =>
        new TerminalRegexFieldValidator(this.Pattern, this.Message, this.Options);
}
