// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Networking.Reliable;

/// <summary>
/// Snapshot of buffering/acknowledgement metrics for <see cref="ReliableStreamWriter" />.
/// </summary>
public readonly record struct ReliableStreamMetrics(

    /// <summary>
    ///     Number of bytes currently buffered awaiting acknowledgement.
    /// </summary>
    int BufferedBytes,

    /// <summary>
    ///     Number of in-flight blocks awaiting acknowledgement.
    /// </summary>
    int BufferedBlocks,

    /// <summary>
    ///     Sequence number of the most recent acknowledged block.
    /// </summary>
    long LastAcknowledgedSequence,

    /// <summary>
    ///     Delay between sending the last acknowledged block and receiving its acknowledgement (milliseconds).
    /// </summary>
    double LastAcknowledgementLatencyMilliseconds);