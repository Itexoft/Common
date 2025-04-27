// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.TerminalKit;

/// <summary>
/// Supported editor types for metadata fields.
/// </summary>
public enum TerminalFormFieldEditor
{
    /// <summary>
    /// Single-line text editor.
    /// </summary>
    Text,

    /// <summary>
    /// Finite list of options displayed as a picker.
    /// </summary>
    Select,

    /// <summary>
    /// Multi-line text editor.
    /// </summary>
    TextArea,
}
