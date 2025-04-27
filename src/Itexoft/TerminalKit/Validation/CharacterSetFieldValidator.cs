// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Text;

namespace Itexoft.TerminalKit.Validation;

internal sealed class CharacterSetFieldValidator : ITerminalFormFieldValidator
{
    private readonly bool _allowDigits;
    private readonly bool _allowLetters;
    private readonly bool _allowWhitespace;
    private readonly string? _message;

    public CharacterSetFieldValidator(bool allowLetters, bool allowDigits, bool allowWhitespace, string? message = null)
    {
        if (!allowLetters && !allowDigits && !allowWhitespace)
            throw new ArgumentException("At least one character class must be allowed.");

        this._allowLetters = allowLetters;
        this._allowDigits = allowDigits;
        this._allowWhitespace = allowWhitespace;
        this._message = message;
    }

    public string? Validate(TerminalFormFieldDefinition field, string? value)
    {
        if (string.IsNullOrEmpty(value))
            return null;

        foreach (var ch in value)
        {
            if (char.IsLetter(ch) && this._allowLetters)
                continue;

            if (char.IsDigit(ch) && this._allowDigits)
                continue;

            if (char.IsWhiteSpace(ch) && this._allowWhitespace)
                continue;

            return this._message ?? this.BuildDefaultMessage();
        }

        return null;
    }

    private string BuildDefaultMessage()
    {
        var builder = new StringBuilder("Allowed characters: ");
        var first = true;
        if (this._allowLetters)
        {
            builder.Append("letters");
            first = false;
        }

        if (this._allowDigits)
        {
            builder.Append(first ? string.Empty : ", ");
            builder.Append("digits");
            first = false;
        }

        if (this._allowWhitespace)
        {
            builder.Append(first ? string.Empty : ", ");
            builder.Append("whitespace");
        }

        return builder.ToString();
    }
}