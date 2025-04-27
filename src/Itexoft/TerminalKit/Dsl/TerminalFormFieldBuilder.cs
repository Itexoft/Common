// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Text.RegularExpressions;
using Itexoft.Extensions;
using Itexoft.TerminalKit.Validation;

namespace Itexoft.TerminalKit.Dsl;

/// <summary>
/// Fluent helper for configuring a single form field inside the DSL.
/// </summary>
public sealed class TerminalFormFieldBuilder
{
    private readonly TerminalFormFieldEditor editor;
    private readonly DataBindingKey key;
    private readonly List<string> options = [];
    private readonly List<ITerminalFormFieldValidator> validators = [];
    private string label;
    private bool required;

    internal TerminalFormFieldBuilder(DataBindingKey key, TerminalFormFieldEditor editor)
    {
        this.key = key;
        this.editor = editor;
        this.label = key.Path;
    }

    /// <summary>
    /// Overrides the default label shown to the user.
    /// </summary>
    public TerminalFormFieldBuilder Label(string label)
    {
        if (!string.IsNullOrWhiteSpace(label))
            this.label = label;

        return this;
    }

    /// <summary>
    /// Marks the field as required or optional.
    /// </summary>
    public TerminalFormFieldBuilder Required(bool required = true)
    {
        this.required = required;

        return this;
    }

    /// <summary>
    /// Supplies a finite set of options, turning the field into a picker.
    /// </summary>
    public TerminalFormFieldBuilder Options(IEnumerable<string> options)
    {
        options.Required();
        this.options.Clear();
        this.options.AddRange(options.Where(option => !string.IsNullOrWhiteSpace(option)));

        return this;
    }

    /// <summary>
    /// Adds a regex validator with an optional custom message.
    /// </summary>
    public TerminalFormFieldBuilder Regex(string pattern, string? message = null, RegexOptions options = RegexOptions.None)
    {
        this.validators.Add(new TerminalRegexFieldValidator(pattern, message, options));

        return this;
    }

    /// <summary>
    /// Restricts input to letters only.
    /// </summary>
    public TerminalFormFieldBuilder OnlyLetters(string? message = null) => this.AllowCharacters(true, false, false, message);

    /// <summary>
    /// Restricts input to digits only.
    /// </summary>
    public TerminalFormFieldBuilder OnlyDigits(string? message = null) => this.AllowCharacters(false, true, false, message);

    /// <summary>
    /// Allows both letters and digits.
    /// </summary>
    public TerminalFormFieldBuilder LettersAndDigits(string? message = null) => this.AllowCharacters(true, true, false, message);

    /// <summary>
    /// Configures which character classes are allowed.
    /// </summary>
    public TerminalFormFieldBuilder AllowCharacters(bool allowLetters, bool allowDigits, bool allowWhitespace, string? message = null)
    {
        this.validators.Add(new CharacterSetFieldValidator(allowLetters, allowDigits, allowWhitespace, message));

        return this;
    }

    /// <summary>
    /// Adds a custom validator callback that can inspect the entire form field definition.
    /// </summary>
    public TerminalFormFieldBuilder CustomValidator(Func<TerminalFormFieldDefinition, string?, string?> validator)
    {
        validator.Required();
        this.validators.Add(new DelegateFieldValidator(validator));

        return this;
    }

    internal TerminalFormFieldDefinition Build() => new()
    {
        Key = this.key,
        Label = this.label,
        Editor = this.editor,
        Options = this.options.ToArray(),
        IsRequired = this.required,
        Validators = this.validators.ToArray(),
    };
}
