using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Nickelony.IDEKit.Core.FindReplace;

/// <summary>Pure text-level find and replace primitives with no UI or document dependencies.</summary>
/// <remarks>
/// Repeated searches over one pattern reuse a bounded regex cache. Matching defaults to <see cref="Regex.InfiniteMatchTimeout"/>;
/// a finite <see cref="SearchQuery.MatchTimeout"/> bounds backtracking and can throw <see cref="RegexMatchTimeoutException"/>. The helpers define
/// their own search direction, so each helper that takes a <see cref="SearchQuery"/> throws <see cref="ArgumentException"/> for a non-empty
/// pattern with <see cref="RegexOptions.RightToLeft"/>, in addition to the exceptions listed per member. <see cref="BuildPattern"/> escapes literal
/// find text so both find modes share one code path.
/// </remarks>
public static class FindReplaceOperations
{
	/// <summary>Matches nothing, so an empty search pattern reports no matches instead of matching at every position.</summary>
	private static readonly Regex s_neverMatchRegex = new("(?!)", RegexOptions.None, Regex.InfiniteMatchTimeout);

	/// <summary>Builds a regex pattern from the find text, escaping it when it is not already a regular expression.</summary>
	/// <returns>The built pattern, or an empty string when the find text is empty.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="findText"/> is <see langword="null"/>.</exception>
	/// <remarks>
	/// A whole-word match groups a regular expression so every alternative keeps both boundaries, and uses <c>\b</c>,
	/// which anchors on word characters. Find text that starts or ends with a non-word character (for example <c>-foo</c>) then does not match
	/// after a space and can match inside a larger token; a token-accurate rule needs the caller's identifier policy, which this neutral primitive
	/// does not know.
	/// </remarks>
	public static string BuildPattern(string findText, bool useRegex, bool matchWholeWord)
	{
		ArgumentNullException.ThrowIfNull(findText);

		if (string.IsNullOrEmpty(findText))
			return string.Empty;

		string pattern = useRegex ? findText : Regex.Escape(findText);

		if (matchWholeWord)
		{
			// Group regular expressions so every alternative keeps both word boundaries; escaped
			// literal text cannot contain a top-level alternation and needs no grouping.
			pattern = useRegex ? @"\b(?:" + pattern + @")\b" : @"\b" + pattern + @"\b";
		}

		return pattern;
	}

	/// <summary>Builds <see cref="RegexOptions"/> for the requested case sensitivity.</summary>
	/// <remarks>
	/// Case-insensitive matching adds <see cref="RegexOptions.CultureInvariant"/>, so the result does not depend on the current culture
	/// (for example the Turkish dotted and dotless I). Callers that build their own options should add the flag themselves.
	/// </remarks>
	public static RegexOptions BuildRegexOptions(bool caseSensitive) =>
		caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

	/// <summary>Returns the number of matches of the query pattern in <paramref name="text"/>.</summary>
	/// <returns>The number of matches, or zero when the pattern is empty.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="text"/> is <see langword="null"/>, or the query pattern is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">The query pattern is not a valid regular expression.</exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// The query match timeout is negative (except for <see cref="Regex.InfiniteMatchTimeout"/>) or exceeds the maximum accepted
	/// by <see cref="Regex"/>.
	/// </exception>
	/// <exception cref="RegexMatchTimeoutException">A match attempt exceeds a finite query match timeout.</exception>
	public static int CountMatches(string text, SearchQuery query)
	{
		ArgumentNullException.ThrowIfNull(text);
		ArgumentNullException.ThrowIfNull(query.Pattern);

		ValidateMatchTimeout(query.MatchTimeout);

		return TryGetSearchRegex(pattern: query.Pattern, options: query.Options, matchTimeout: query.MatchTimeout, out Regex? regex)
			? regex.Count(text)
			: 0;
	}

	/// <summary>Returns all matches of the query pattern in <paramref name="text"/>.</summary>
	/// <returns>The matches in document order, or an empty collection when the pattern is empty.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="text"/> is <see langword="null"/>, or the query pattern is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">The query pattern is not a valid regular expression.</exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// The query match timeout is negative (except for <see cref="Regex.InfiniteMatchTimeout"/>) or exceeds the maximum accepted
	/// by <see cref="Regex"/>.
	/// </exception>
	/// <exception cref="RegexMatchTimeoutException">
	/// A match attempt exceeds a finite query match timeout while the returned collection is enumerated.
	/// </exception>
	/// <remarks>
	/// The returned collection is evaluated lazily while it is enumerated, so a finite query match timeout is enforced during enumeration
	/// rather than by this call.
	/// </remarks>
	public static MatchCollection FindAllMatches(string text, SearchQuery query)
	{
		ArgumentNullException.ThrowIfNull(text);
		ArgumentNullException.ThrowIfNull(query.Pattern);

		ValidateMatchTimeout(query.MatchTimeout);

		if (!TryGetSearchRegex(pattern: query.Pattern, options: query.Options, matchTimeout: query.MatchTimeout, out Regex? regex))
			return s_neverMatchRegex.Matches(text);

		return regex.Matches(text);
	}

	/// <summary>Finds the first match that starts at or after <paramref name="startOffset"/>.</summary>
	/// <returns>The first match at or after <paramref name="startOffset"/>, or <see langword="null"/> when there is none.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="text"/> is <see langword="null"/>, or the query pattern is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="startOffset"/> is outside the document text, or the query match timeout is negative
	/// (except for <see cref="Regex.InfiniteMatchTimeout"/>) or exceeds the maximum accepted by <see cref="Regex"/>.
	/// </exception>
	/// <exception cref="ArgumentException">The query pattern is not a valid regular expression.</exception>
	/// <exception cref="RegexMatchTimeoutException">A match attempt exceeds a finite query match timeout.</exception>
	/// <remarks>
	/// Matches are located in the full document text, so regular-expression anchors
	/// and boundary assertions (<c>^</c>, <c>$</c>, <c>\A</c>, <c>\z</c>, and <c>\b</c>) resolve against the document rather than the searched range,
	/// and the returned match carries absolute document offsets.
	/// </remarks>
	public static Match? FindNextMatch(string text, int startOffset, SearchQuery query)
	{
		ArgumentNullException.ThrowIfNull(text);
		ArgumentNullException.ThrowIfNull(query.Pattern);

		ValidateMatchTimeout(query.MatchTimeout);

		ArgumentOutOfRangeException.ThrowIfNegative(startOffset);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(startOffset, text.Length);

		if (!TryGetSearchRegex(pattern: query.Pattern, options: query.Options, matchTimeout: query.MatchTimeout, out Regex? regex))
			return null;

		// Regex.Match reports a failed search as a non-successful match object, which is normalized
		// to null so both find helpers share the same "no match" shape.
		Match match = regex.Match(text, startOffset);

		return match.Success ? match : null;
	}

	/// <summary>Finds the last match that ends at or before <paramref name="endOffset"/>.</summary>
	/// <returns>The last match at or before <paramref name="endOffset"/>, or <see langword="null"/> when there is none.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="text"/> is <see langword="null"/>, or the query pattern is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="endOffset"/> is outside the document text, or the query match timeout is negative
	/// (except for <see cref="Regex.InfiniteMatchTimeout"/>) or exceeds the maximum accepted by <see cref="Regex"/>.
	/// </exception>
	/// <exception cref="ArgumentException">The query pattern is not a valid regular expression.</exception>
	/// <exception cref="RegexMatchTimeoutException">A match attempt exceeds a finite query match timeout.</exception>
	/// <remarks>
	/// Matches are located in the full document text, so regular-expression anchors and boundary assertions resolve against the document
	/// rather than the searched range, and the returned match carries absolute document offsets. The scan starts at the document beginning and stops
	/// at the first match beyond <paramref name="endOffset"/>, so every call re-scans the prefix; hosts that navigate many matches should enumerate
	/// <see cref="FindAllMatches(string, SearchQuery)"/> instead.
	/// </remarks>
	public static Match? FindPreviousMatch(string text, int endOffset, SearchQuery query)
	{
		ArgumentNullException.ThrowIfNull(text);
		ArgumentNullException.ThrowIfNull(query.Pattern);

		ValidateMatchTimeout(query.MatchTimeout);

		ArgumentOutOfRangeException.ThrowIfNegative(endOffset);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(endOffset, text.Length);

		if (!TryGetSearchRegex(pattern: query.Pattern, options: query.Options, matchTimeout: query.MatchTimeout, out Regex? regex))
			return null;

		Match? previousMatch = null;

		// Matches are enumerated in document order and do not overlap, so the first match that
		// reaches beyond the requested end also ends the scan.
		foreach (Match match in regex.Matches(text))
		{
			if (match.Index + match.Length > endOffset)
				break;

			previousMatch = match;
		}

		return previousMatch;
	}

	/// <summary>Replaces all matches of the query pattern in <paramref name="text"/> with <paramref name="replacement"/>.</summary>
	/// <returns>The transformed text, or the original text when the pattern is empty.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="text"/> or <paramref name="replacement"/> is <see langword="null"/>, or the query pattern is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">The query pattern is not a valid regular expression.</exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// The query match timeout is negative (except for <see cref="Regex.InfiniteMatchTimeout"/>) or exceeds the maximum accepted
	/// by <see cref="Regex"/>.
	/// </exception>
	/// <exception cref="RegexMatchTimeoutException">A match attempt exceeds a finite query match timeout.</exception>
	/// <remarks>
	/// The replacement is a substitution pattern, so tokens such as <c>$1</c> (captured group) and <c>$$</c> (literal <c>$</c>)
	/// are interpreted rather than escaped.
	/// </remarks>
	public static string ReplaceAll(string text, SearchQuery query, string replacement)
	{
		ArgumentNullException.ThrowIfNull(text);
		ArgumentNullException.ThrowIfNull(query.Pattern);
		ArgumentNullException.ThrowIfNull(replacement);

		ValidateMatchTimeout(query.MatchTimeout);

		if (!TryGetSearchRegex(pattern: query.Pattern, options: query.Options, matchTimeout: query.MatchTimeout, out Regex? regex))
			return text;

		return regex.Replace(text, replacement);
	}

	/// <summary>Tries to get the cached regex for a search, which exists only for a non-empty pattern.</summary>
	/// <returns><see langword="true"/> when the pattern is not empty and a regex is available.</returns>
	/// <remarks>
	/// An empty pattern means no search text, so every helper reports its own empty result instead of matching at every position.
	/// </remarks>
	private static bool TryGetSearchRegex(string pattern, RegexOptions options, TimeSpan matchTimeout, [NotNullWhen(true)] out Regex? regex)
	{
		if (pattern.Length == 0)
		{
			regex = null;
			return false;
		}

		// The helpers report their matches in a fixed direction, so a right-to-left regex would
		// silently violate their documented contracts: a "next" match could start before the start
		// offset, and match enumeration would run in reverse document order.
		if ((options & RegexOptions.RightToLeft) != 0)
		{
			throw new ArgumentException(
				"These helpers define their own search direction, so RegexOptions.RightToLeft is not supported.",
				nameof(options)
			);
		}

		regex = RegexCache.CreateRegex(pattern, options, matchTimeout);
		return true;
	}

	/// <summary>
	/// Rejects a negative timeout while accepting <see cref="Regex.InfiniteMatchTimeout"/>; a timeout above the maximum accepted
	/// by <see cref="Regex"/> is rejected by its constructor.
	/// </summary>
	private static void ValidateMatchTimeout(TimeSpan matchTimeout)
	{
		if (matchTimeout < TimeSpan.Zero && matchTimeout != Regex.InfiniteMatchTimeout)
		{
			throw new ArgumentOutOfRangeException(
				nameof(matchTimeout),
				matchTimeout,
				"The match timeout must not be negative, except for Regex.InfiniteMatchTimeout."
			);
		}
	}
}
