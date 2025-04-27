// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.TerminalKit.Validation;

/// <summary>
/// Contract for validating console form input.
/// </summary>
public interface ITerminalFormFieldValidator
{
    /// <summary>
    /// Validates the specified value and returns an error message or <c>null</c>.
    /// </summary>
    string? Validate(TerminalFormFieldDefinition field, string? value);
}
