// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Text.Rewriting.Json.Internal.Rules;

internal sealed class RequireRule : JsonRewriteRule
{
    internal RequireRule(string pointer, Func<string, bool>? predicate, string? errorMessage)
    {
        this.Pointer = pointer ?? throw new ArgumentNullException(nameof(pointer));
        this.Predicate = predicate;
        this.ErrorMessage = errorMessage;
    }

    internal RequireRule(string pointer, Func<string, ValueTask<bool>> predicateAsync, string? errorMessage)
    {
        this.Pointer = pointer ?? throw new ArgumentNullException(nameof(pointer));
        this.PredicateAsync = predicateAsync ?? throw new ArgumentNullException(nameof(predicateAsync));
        this.ErrorMessage = errorMessage;
    }

    internal override string Pointer { get; }

    internal Func<string, bool>? Predicate { get; }

    internal Func<string, ValueTask<bool>>? PredicateAsync { get; }

    internal string? ErrorMessage { get; }

    internal override bool HasAsync => this.PredicateAsync is not null;
}
