// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.TerminalKit.Validation;

/// <summary>
/// Enforces simple character-class rules (letters/digits/whitespace) on a text field.
/// </summary>
public sealed class TerminalCharacterSetAttribute : TerminalValidationAttribute
{
    /// <summary>
    /// Initializes the attribute with the desired character classes.
    /// </summary>
    public TerminalCharacterSetAttribute(bool allowLetters = true, bool allowDigits = true, bool allowWhitespace = false)
    {
        this.AllowLetters = allowLetters;
        this.AllowDigits = allowDigits;
        this.AllowWhitespace = allowWhitespace;
    }

    /// <summary>
    /// Gets a value indicating whether letters are allowed.
    /// </summary>
    public bool AllowLetters { get; }

    /// <summary>
    /// Gets a value indicating whether digits are allowed.
    /// </summary>
    public bool AllowDigits { get; }

    /// <summary>
    /// Gets a value indicating whether whitespace characters are allowed.
    /// </summary>
    public bool AllowWhitespace { get; }

    /// <summary>
    /// Gets or sets the custom validation message.
    /// </summary>
    public string? Message { get; init; }

    /// <inheritdoc />
    public override ITerminalFormFieldValidator CreateValidator() => new CharacterSetFieldValidator(
        this.AllowLetters,
        this.AllowDigits,
        this.AllowWhitespace,
        this.Message);
}