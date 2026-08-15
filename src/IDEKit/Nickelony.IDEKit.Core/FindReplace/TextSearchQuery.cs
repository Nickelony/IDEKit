using System.Text.RegularExpressions;

namespace Nickelony.IDEKit.Core.FindReplace;

/// <summary>
/// Describes a regular-expression text search: the pattern, the match options, and the per-match
/// timeout.
/// </summary>
/// <remarks>
/// The options are applied as supplied, with one exception: the find and replace helpers define
/// their own search direction, so they reject <see cref="RegexOptions.RightToLeft"/> with an
/// <see cref="ArgumentException"/>. Add <see cref="RegexOptions.CultureInvariant"/> for
/// culture-independent matching; <see cref="FindReplaceText.BuildRegexOptions"/> adds it for
/// case-insensitive searches. For patterns from an untrusted source, add
/// <see cref="RegexOptions.NonBacktracking"/> (.NET 7 and later) so the engine rejects patterns it
/// cannot run without backtracking instead of relying on the match timeout; a pattern the engine
/// cannot run throws <see cref="NotSupportedException"/> when the regex is constructed.
/// The find and replace helpers accept this type directly through their
/// query overloads, so one query can drive search, count, and replace operations.
/// </remarks>
/// <param name="Pattern">
/// The regular expression pattern. A <see langword="default"/> instance carries <see langword="null"/>
/// here despite the annotation; the find helpers reject such a query with <see cref="ArgumentNullException"/>.
/// </param>
/// <param name="Options">The options used for matching.</param>
/// <param name="MatchTimeout">
/// The maximum time for a single match attempt, or <see cref="TimeSpan.Zero"/> (the default) to use
/// <see cref="Regex.InfiniteMatchTimeout"/>. Passing <see cref="Regex.InfiniteMatchTimeout"/> itself
/// is also accepted and means the same.
/// </param>
public readonly record struct TextSearchQuery(string Pattern, RegexOptions Options, TimeSpan MatchTimeout = default);
