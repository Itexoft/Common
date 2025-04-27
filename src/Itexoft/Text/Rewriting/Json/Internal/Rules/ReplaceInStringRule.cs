// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Text.Rewriting.Json.Dsl;

namespace Itexoft.Text.Rewriting.Json.Internal.Rules;

internal sealed class ReplaceInStringRule : JsonRewriteRule
{
    internal ReplaceInStringRule(string pointer, Func<string, string> replacer)
    {
        this.Pointer = pointer ?? throw new ArgumentNullException(nameof(pointer));
        var valueReplacer = replacer ?? throw new ArgumentNullException(nameof(replacer));
        this.Replacer = ctx => valueReplacer(ctx.Value);
    }

    internal ReplaceInStringRule(string pointer, Func<JsonStringContext, ValueTask<string>> replacerAsync)
    {
        this.Pointer = pointer ?? throw new ArgumentNullException(nameof(pointer));
        this.ReplacerAsync = replacerAsync ?? throw new ArgumentNullException(nameof(replacerAsync));
    }

    internal ReplaceInStringRule(string pointer, Func<JsonStringContext, bool> predicate, Func<JsonStringContext, string> replacer)
    {
        this.Pointer = pointer ?? throw new ArgumentNullException(nameof(pointer));
        this.Predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
        this.Replacer = replacer ?? throw new ArgumentNullException(nameof(replacer));
    }

    internal ReplaceInStringRule(
        string pointer,
        Func<JsonStringContext, ValueTask<bool>> predicateAsync,
        Func<JsonStringContext, ValueTask<string>> replacerAsync)
    {
        this.Pointer = pointer ?? throw new ArgumentNullException(nameof(pointer));
        this.PredicateAsync = predicateAsync ?? throw new ArgumentNullException(nameof(predicateAsync));
        this.ReplacerAsync = replacerAsync ?? throw new ArgumentNullException(nameof(replacerAsync));
    }

    internal ReplaceInStringRule(Func<JsonStringContext, bool> predicate, Func<JsonStringContext, string> replacer)
    {
        this.Predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
        this.Replacer = replacer ?? throw new ArgumentNullException(nameof(replacer));
    }

    internal ReplaceInStringRule(Func<JsonStringContext, ValueTask<bool>> predicateAsync, Func<JsonStringContext, ValueTask<string>> replacerAsync)
    {
        this.PredicateAsync = predicateAsync ?? throw new ArgumentNullException(nameof(predicateAsync));
        this.ReplacerAsync = replacerAsync ?? throw new ArgumentNullException(nameof(replacerAsync));
    }

    internal override string? Pointer { get; }

    internal Func<JsonStringContext, bool>? Predicate { get; }

    internal Func<JsonStringContext, ValueTask<bool>>? PredicateAsync { get; }

    internal Func<JsonStringContext, string>? Replacer { get; }

    internal Func<JsonStringContext, ValueTask<string>>? ReplacerAsync { get; }

    internal override bool HasAsync => this.PredicateAsync is not null || this.ReplacerAsync is not null;
}
