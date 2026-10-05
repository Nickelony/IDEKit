namespace Nickelony.Matching;

/// <summary>
/// Matches a query against the words of a text as the command palette of VS Code does: as a prefix, as word starts, or as a substring.
/// </summary>
/// <remarks>
/// <para>
/// A match is the first of three forms that holds, all ignoring case. The query is a prefix of the text. Each character of the query matches the next
/// character of a word, or the first character of a later word, where a word is a run that a space or a tab ends. The query is a contiguous substring
/// of the text. The matched characters merge into ranges.
/// </para>
/// <para>
/// The search for word starts backtracks, so a step budget bounds it. A text that exhausts the budget has no word-start match, and the substring form
/// can still match it.
/// </para>
/// </remarks>
public static class WordMatcher
{
	private const int StepBudget = 50_000;

	/// <summary>Matches a query against a text.</summary>
	/// <param name="query">The text to look for, which is not empty.</param>
	/// <param name="text">The text to look in.</param>
	/// <param name="ranges">
	/// The matched characters of <paramref name="text"/>, ascending, when the method returns <see langword="true"/>; otherwise, empty.
	/// </param>
	/// <returns><see langword="true"/> when the query matches the text; otherwise, <see langword="false"/>.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="query"/> or <paramref name="text"/> is <see langword="null"/>.</exception>
	public static bool TryMatch(string query, string text, out IReadOnlyList<MatchRange> ranges)
	{
		ArgumentNullException.ThrowIfNull(query);
		ArgumentNullException.ThrowIfNull(text);

		ranges = [];

		if (query.Length == 0 || text.Length < 1)
			return false;

		if (text.Length >= query.Length && text.StartsWith(query, StringComparison.OrdinalIgnoreCase))
		{
			ranges = [new MatchRange(0, query.Length)];
			return true;
		}

		string lowerQuery = query.ToLowerInvariant();
		string lowerText = text.ToLowerInvariant();

		if (TryMatchWordStarts(lowerQuery, lowerText, out List<int> positions))
		{
			ranges = ToRanges(positions);
			return true;
		}

		int index = lowerText.IndexOf(lowerQuery, StringComparison.Ordinal);

		if (index < 0)
			return false;

		ranges = [new MatchRange(index, query.Length)];
		return true;
	}

	private static bool TryMatchWordStarts(string query, string text, out List<int> positions)
	{
		positions = [];

		int budget = StepBudget;

		for (int start = 0; start < text.Length; start = NextWord(text, start + 1))
		{
			positions.Clear();

			if (MatchFrom(query, text, queryIndex: 0, textIndex: start, positions, ref budget))
				return true;

			if (budget < 0)
				break;
		}

		positions.Clear();
		return false;
	}

	private static bool MatchFrom(string query, string text, int queryIndex, int textIndex, List<int> positions, ref int budget)
	{
		if (queryIndex == query.Length)
			return true;

		if (textIndex == text.Length || --budget < 0 || query[queryIndex] != text[textIndex])
			return false;

		positions.Add(textIndex);

		if (MatchFrom(query, text, queryIndex: queryIndex + 1, textIndex: textIndex + 1, positions, ref budget))
			return true;

		for (int next = NextWord(text, textIndex + 1); next < text.Length; next = NextWord(text, next + 1))
		{
			if (MatchFrom(query, text, queryIndex: queryIndex + 1, textIndex: next, positions, ref budget))
				return true;
		}

		positions.RemoveAt(positions.Count - 1);
		return false;
	}

	// The start of the next word at or after a position: a space or tab, or the character after one.
	private static int NextWord(string text, int start)
	{
		for (int index = start; index < text.Length; index++)
		{
			if (IsBlank(text[index]) || (index > 0 && IsBlank(text[index - 1])))
				return index;
		}

		return text.Length;
	}

	private static bool IsBlank(char character) => character is ' ' or '\t';

	private static MatchRange[] ToRanges(List<int> positions)
	{
		var ranges = new List<MatchRange>();

		int start = positions[0];
		int length = 1;

		for (int index = 1; index < positions.Count; index++)
		{
			if (positions[index] == start + length)
			{
				length++;
				continue;
			}

			ranges.Add(new MatchRange(start, length));
			start = positions[index];
			length = 1;
		}

		ranges.Add(new MatchRange(start, length));
		return [.. ranges];
	}
}
