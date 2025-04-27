// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.TerminalKit.Validation;

namespace Itexoft.TerminalKit;

/// <summary>
/// Describes a single field inside metadata edit dialog.
/// </summary>
public sealed class TerminalFormFieldDefinition
{
    /// <summary>
    /// Gets the binding key associated with the field.
    /// </summary>
    public DataBindingKey Key { get; init; } = DataBindingKey.Empty;

    /// <summary>
    /// Gets the label shown to the user.
    /// </summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>
    /// Gets the editor type used to collect input.
    /// </summary>
    public TerminalFormFieldEditor Editor { get; init; } = TerminalFormFieldEditor.Text;

    /// <summary>
    /// Gets the list of options for select-style editors.
    /// </summary>
    public IReadOnlyList<string> Options { get; init; } = [];

    /// <summary>
    /// Gets a value indicating whether the field must be filled in.
    /// </summary>
    public bool IsRequired { get; init; }

    /// <summary>
    /// Gets a value indicating whether the field is read-only.
    /// </summary>
    public bool IsReadOnly { get; init; }

    /// <summary>
    /// Gets the validators applied to the field.
    /// </summary>
    public IReadOnlyList<ITerminalFormFieldValidator> Validators { get; init; } = [];

    /// <summary>
    /// Runs configured validators and returns an error message if validation fails.
    /// </summary>
    /// <param name="value">Value to verify.</param>
    public string? Validate(string? value)
    {
        if (this.Validators == null)
            return null;

        foreach (var validator in this.Validators)
        {
            if (validator == null)
                continue;

            var error = validator.Validate(this, value);

            if (!string.IsNullOrWhiteSpace(error))
                return error;
        }

        return null;
    }
}