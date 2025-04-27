// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.TerminalKit;

/// <summary>
/// Well-known events raised by metadata forms.
/// </summary>
public static class TerminalFormEvents
{
    /// <summary>
    /// Raised when the user confirms the form.
    /// </summary>
    public static readonly TerminalEventKey Submit = TerminalEventKey.Create<TerminalMetadataForm>("Submit");

    /// <summary>
    /// Raised when the user cancels the form.
    /// </summary>
    public static readonly TerminalEventKey Cancel = TerminalEventKey.Create<TerminalMetadataForm>("Cancel");
}