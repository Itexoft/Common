// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Text.Rewriting.Json.Internal.Rules;

internal sealed class CaptureObjectRule<T> : ProjectionCaptureRule where T : class
{
    private readonly Action<T> onObject;
    private readonly JsonProjectionPlan<T> projection;

    internal CaptureObjectRule(string pointer, JsonProjectionPlan<T> projection, Action<T> onObject) : base(pointer)
    {
        this.projection = projection ?? throw new ArgumentNullException(nameof(projection));
        this.onObject = onObject ?? throw new ArgumentNullException(nameof(onObject));
    }

    internal override void Capture(string literal)
    {
        var model = this.projection.Project(literal);
        this.onObject(model);
    }
}
