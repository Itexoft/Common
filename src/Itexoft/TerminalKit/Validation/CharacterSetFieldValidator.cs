// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Text;

namespace Itexoft.TerminalKit.Validation;

internal sealed class CharacterSetFieldValidator : ITerminalFormFieldValidator
{
    private readonly bool allowDigits;
    private readonly bool allowLetters;
    private readonly bool allowWhitespace;
    private readonly string? message;

    public CharacterSetFieldValidator(bool allowLetters, bool allowDigits, bool allowWhitespace, string? message = null)
    {
        if (!allowLetters && !allowDigits && !allowWhitespace)
            throw new ArgumentException("At least one character class must be allowed.");

        this.allowLetters = allowLetters;
        this.allowDigits = allowDigits;
        this.allowWhitespace = allowWhitespace;
        this.message = message;
    }

    public string? Validate(TerminalFormFieldDefinition field, string? value)
    {
        if (string.IsNullOrEmpty(value))
            return null;

        foreach (var ch in value)
        {
            if (char.IsLetter(ch) && this.allowLetters)
                continue;

            if (char.IsDigit(ch) && this.allowDigits)
                continue;

            if (char.IsWhiteSpace(ch) && this.allowWhitespace)
                continue;

            return this.message ?? this.BuildDefaultMessage();
        }

        return null;
    }

    private string BuildDefaultMessage()
    {
        var builder = new StringBuilder("Allowed characters: ");
        var first = true;

        if (this.allowLetters)
        {
            builder.Append("letters");
            first = false;
        }

        if (this.allowDigits)
        {
            builder.Append(first ? string.Empty : ", ");
            builder.Append("digits");
            first = false;
        }

        if (this.allowWhitespace)
        {
            builder.Append(first ? string.Empty : ", ");
            builder.Append("whitespace");
        }

        return builder.ToString();
    }
}
