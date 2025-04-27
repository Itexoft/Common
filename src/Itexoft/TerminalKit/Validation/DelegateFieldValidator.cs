// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.TerminalKit.Validation;

internal sealed class DelegateFieldValidator(Func<TerminalFormFieldDefinition, string?, string?> validator) : ITerminalFormFieldValidator
{
    private readonly Func<TerminalFormFieldDefinition, string?, string?> validator = validator ?? throw new ArgumentNullException(nameof(validator));

    public string? Validate(TerminalFormFieldDefinition field, string? value) => this.validator(field, value);
}
