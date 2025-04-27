// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Text.Rewriting.Primitives;

/// <summary>
/// Callback invoked when a rule is matched.
/// </summary>
/// <param name="ruleId">Rule identifier assigned during plan creation.</param>
/// <param name="match">Span containing the matched text.</param>
public delegate void MatchHandler(int ruleId, ReadOnlySpan<char> match);

/// <summary>
/// Async callback invoked when a rule is matched.
/// </summary>
/// <param name="ruleId">Rule identifier assigned during plan creation.</param>
/// <param name="match">Memory containing the matched text. The buffer is valid only for the duration of the callback.</param>
/// <returns>A task that completes when handling is done.</returns>
public delegate ValueTask MatchHandlerAsync(int ruleId, ReadOnlyMemory<char> match);

/// <summary>
/// Callback that produces a replacement string for a match.
/// </summary>
/// <param name="ruleId">Rule identifier assigned during plan creation.</param>
/// <param name="match">Span containing the matched text.</param>
/// <returns>Replacement text, or null/empty to skip replacement.</returns>
public delegate string? ReplacementFactory(int ruleId, ReadOnlySpan<char> match);

/// <summary>
/// Callback that produces a replacement string using extended context.
/// </summary>
/// <param name="ruleId">Rule identifier assigned during plan creation.</param>
/// <param name="match">Span containing the matched text.</param>
/// <param name="metrics">Current stream metrics.</param>
/// <returns>Replacement text, or null/empty to skip replacement.</returns>
public delegate string? ReplacementFactoryWithContext(int ruleId, ReadOnlySpan<char> match, RewriteMetrics metrics);

/// <summary>
/// Async callback that produces a replacement string for a match.
/// </summary>
/// <param name="ruleId">Rule identifier assigned during plan creation.</param>
/// <param name="match">Memory containing the matched text. The buffer is valid only for the duration of the callback.</param>
/// <returns>Replacement text, or null/empty to skip replacement.</returns>
public delegate ValueTask<string?> ReplacementFactoryAsync(int ruleId, ReadOnlyMemory<char> match);

/// <summary>
/// Async callback that produces a replacement string using extended context.
/// </summary>
/// <param name="ruleId">Rule identifier assigned during plan creation.</param>
/// <param name="match">Memory containing the matched text. The buffer is valid only for the duration of the callback.</param>
/// <param name="metrics">Current stream metrics.</param>
/// <returns>Replacement text, or null/empty to skip replacement.</returns>
public delegate ValueTask<string?> ReplacementFactoryWithContextAsync(int ruleId, ReadOnlyMemory<char> match, RewriteMetrics metrics);

/// <summary>
/// Callback that returns the length of the tail segment considered a match.
/// </summary>
/// <param name="tail">Latest buffered characters.</param>
/// <returns>Length of the match ending at the tail; 0 for no match.</returns>
public delegate int TailMatcher(ReadOnlySpan<char> tail);
