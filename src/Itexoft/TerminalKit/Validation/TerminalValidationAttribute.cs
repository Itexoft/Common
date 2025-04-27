// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.TerminalKit.Validation;

/// <summary>
/// Base attribute that produces a field validator consumed by the console form renderer.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = true)]
public abstract class TerminalValidationAttribute : Attribute
{
    /// <summary>
    /// Creates the validator instance corresponding to the attribute.
    /// </summary>
    public abstract ITerminalFormFieldValidator CreateValidator();
}
