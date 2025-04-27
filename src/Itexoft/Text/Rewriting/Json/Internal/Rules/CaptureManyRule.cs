// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Text.Rewriting.Json.Internal.Rules;

internal sealed class CaptureManyRule<T> : ProjectionCaptureRule where T : class
{
    private readonly Action<T> onItem;
    private readonly JsonProjectionPlan<T> projection;

    internal CaptureManyRule(string pointer, JsonProjectionPlan<T> projection, Action<T> onItem) : base(pointer)
    {
        this.projection = projection ?? throw new ArgumentNullException(nameof(projection));
        this.onItem = onItem ?? throw new ArgumentNullException(nameof(onItem));
    }

    internal override void Capture(string literal)
    {
        foreach (var item in this.projection.ProjectMany(literal))
            this.onItem(item);
    }
}
