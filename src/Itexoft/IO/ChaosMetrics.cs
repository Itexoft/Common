// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.IO;

public readonly record struct ChaosMetrics(
    TimeSpan Elapsed,
    long ReadOperations,
    long WriteOperations,
    long FlushOperations,
    long BytesRead,
    long BytesWritten)
{
    public long TotalOperations => this.ReadOperations + this.WriteOperations + this.FlushOperations;
}
