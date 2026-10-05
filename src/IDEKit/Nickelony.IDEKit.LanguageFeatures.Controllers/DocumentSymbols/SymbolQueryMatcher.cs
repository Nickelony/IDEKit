using Nickelony.Matching;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.DocumentSymbols;

/// <summary>
/// Matches a symbol against the text typed in a symbol mode, as VS Code does: the first word names the symbol and the words after it name
/// its container.
/// </summary>
/// <remarks>
/// A symbol whose name holds the whole query matches without its container. Otherwise, the first word must match the name and the rest
/// must match the container. A query without a word to match, such as a blank one, matches every symbol.
/// </remarks>
internal sealed class SymbolQueryMatcher
{
	private readonly FuzzyMatcher? _whole;
	private readonly FuzzyMatcher? _name;
	private readonly FuzzyMatcher? _container;

	/// <param name="query">The text typed after the prefix of the mode.</param>
	internal SymbolQueryMatcher(string query)
	{
		(string name, string container) = SymbolQuerySplitter.Split(query);
		var whole = new FuzzyMatcher(query.Trim());

		if (whole.IsEmpty)
			return;

		_whole = whole;
		_name = container.Length == 0 ? whole : new FuzzyMatcher(name);
		_container = container.Length == 0 ? null : new FuzzyMatcher(container);
	}

	/// <summary>Whether the query has no word to match, so every symbol matches.</summary>
	internal bool MatchesEverything => _whole is null;

	/// <summary>Matches a symbol by its name and its container.</summary>
	/// <param name="name">The name of the symbol.</param>
	/// <param name="container">The text that names its container, or <see langword="null"/> for none.</param>
	/// <param name="score">The score of the match, which is <c>0</c> when the query matches everything.</param>
	/// <param name="nameRanges">The characters of the name that matched.</param>
	/// <param name="containerRanges">The characters of the container that matched.</param>
	/// <returns><see langword="true"/> when the symbol matches.</returns>
	internal bool TryMatch(
		string name,
		string? container,
		out int score,
		out IReadOnlyList<MatchRange> nameRanges,
		out IReadOnlyList<MatchRange> containerRanges
	)
	{
		score = 0;
		nameRanges = [];
		containerRanges = [];

		if (_whole is null)
			return true;

		if (_whole.TryMatch(name, null, out FuzzyMatch wholeMatch))
		{
			score = wholeMatch.Score;
			nameRanges = wholeMatch.LabelRanges;

			return true;
		}

		if (
			_container is null
			|| container is null
			|| !_name!.TryMatch(name, null, out FuzzyMatch nameMatch)
			|| !_container.TryMatch(container, null, out FuzzyMatch containerMatch)
		)
		{
			return false;
		}

		score = nameMatch.Score + containerMatch.Score;
		nameRanges = nameMatch.LabelRanges;
		containerRanges = containerMatch.LabelRanges;

		return true;
	}

	/// <summary>Checks the words after the first one, for a mode whose server has matched the first word already.</summary>
	/// <param name="name">The name of the symbol.</param>
	/// <param name="description">The text that describes where the symbol lives: its container and its document.</param>
	/// <returns>
	/// <see langword="true"/> when the query has no word after the first, when the name holds the whole query, or when the words after the first
	/// match the description.
	/// </returns>
	internal bool MatchesContainerWords(string name, string description) =>
		_container is null || _whole!.TryMatch(name, null, out _) || _container.TryMatch(description, null, out _);
}
