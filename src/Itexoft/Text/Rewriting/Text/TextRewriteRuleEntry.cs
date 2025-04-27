// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Text.Rewriting.Primitives;

namespace Itexoft.Text.Rewriting.Text;

/// <summary>
/// Base representation of a compiled text rewrite rule used by the streaming engine.
/// </summary>
public abstract class TextRewriteRuleEntry
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TextRewriteRuleEntry" /> class.
    /// </summary>
    /// <param name="action">Mutation applied when the rule matches.</param>
    /// <param name="priority">Priority used to resolve overlaps; lower wins.</param>
    /// <param name="fixedLength">Length of a literal/regex match when deterministic; 0 when variable.</param>
    /// <param name="maxMatchLength">Maximum length this rule may consume.</param>
    protected TextRewriteRuleEntry(MatchAction action, int priority, int fixedLength, int maxMatchLength)
    {
        if (maxMatchLength <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxMatchLength), "maxMatchLength must be > 0.");

        this.Action = action;
        this.Priority = priority;
        this.FixedLength = fixedLength;
        this.MaxMatchLength = maxMatchLength;
        this.Order = -1;
    }

    /// <summary>
    /// Gets the mutation to apply when this rule matches.
    /// </summary>
    public MatchAction Action { get; }

    /// <summary>
    /// Gets the priority used when <see cref="MatchSelection.PriorityThenLongest" /> is configured.
    /// </summary>
    public int Priority { get; }

    /// <summary>
    /// Gets the deterministic order assigned during plan compilation.
    /// </summary>
    public int Order { get; private set; }

    /// <summary>
    /// Gets the known match length; zero indicates a variable-length rule.
    /// </summary>
    public int FixedLength { get; }

    /// <summary>
    /// Gets the upper bound for how many characters this rule can consume.
    /// </summary>
    public int MaxMatchLength { get; }

    /// <summary>
    /// Gets the literal replacement to apply when <see cref="Action" /> is <see cref="MatchAction.Replace" />.
    /// </summary>
    public virtual string? Replacement => null;

    /// <summary>
    /// Gets the synchronous factory that produces a replacement string.
    /// </summary>
    public virtual ReplacementFactory? ReplacementFactory => null;

    /// <summary>
    /// Gets the synchronous factory that produces a replacement string with access to metrics.
    /// </summary>
    public virtual ReplacementFactoryWithContext? ReplacementFactoryWithContext => null;

    /// <summary>
    /// Gets the asynchronous factory that produces a replacement string.
    /// </summary>
    public virtual ReplacementFactoryAsync? ReplacementFactoryAsync => null;

    /// <summary>
    /// Gets the asynchronous factory that produces a replacement string with access to metrics.
    /// </summary>
    public virtual ReplacementFactoryWithContextAsync? ReplacementFactoryWithContextAsync => null;

    /// <summary>
    /// Gets the synchronous callback invoked when this rule matches.
    /// </summary>
    public virtual MatchHandler? OnMatch => null;

    /// <summary>
    /// Gets the asynchronous callback invoked when this rule matches.
    /// </summary>
    public virtual MatchHandlerAsync? OnMatchAsync => null;

    /// <summary>
    /// Gets a value indicating whether the rule contains asynchronous callbacks or factories.
    /// </summary>
    public virtual bool HasAsyncCallbacks => this.ReplacementFactoryAsync is not null
                                             || this.ReplacementFactoryWithContextAsync is not null
                                             || this.OnMatchAsync is not null;

    /// <summary>
    /// Assigns a deterministic order value during plan compilation.
    /// </summary>
    /// <param name="value">Order to set.</param>
    internal void AssignOrder(int value) => this.Order = value;
}
