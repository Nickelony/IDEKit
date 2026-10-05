namespace Nickelony.Matching;

/// <summary>Describes how a query matched a candidate: its score and the matched characters of the label and the description.</summary>
/// <remarks>
/// A range covers a run of consecutive matched characters, so a host highlights a range as one span. Both lists are empty when the query matched
/// nothing in that part. The <see langword="default"/> value, which <see cref="FuzzyMatcher.TryMatch"/> returns for no match, holds no lists.
/// </remarks>
public readonly record struct FuzzyMatch
{
	/// <param name="score">The rank of the match. A higher score is a better match, and the score of a match is always positive.</param>
	/// <param name="labelRanges">The matched characters of the label, as ascending ranges that never touch.</param>
	/// <param name="descriptionRanges">The matched characters of the description, as ascending ranges that never touch.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="labelRanges"/> or <paramref name="descriptionRanges"/> is <see langword="null"/>.
	/// </exception>
	public FuzzyMatch(int score, IReadOnlyList<MatchRange> labelRanges, IReadOnlyList<MatchRange> descriptionRanges)
	{
		ArgumentNullException.ThrowIfNull(labelRanges);
		ArgumentNullException.ThrowIfNull(descriptionRanges);

		Score = score;
		LabelRanges = labelRanges;
		DescriptionRanges = descriptionRanges;
	}

	/// <summary>The rank of the match. A higher score is a better match, and the score of a match is always positive.</summary>
	public int Score { get; }

	/// <summary>The matched characters of the label, as ascending ranges that never touch.</summary>
	public IReadOnlyList<MatchRange> LabelRanges { get; }

	/// <summary>The matched characters of the description, as ascending ranges that never touch.</summary>
	public IReadOnlyList<MatchRange> DescriptionRanges { get; }
}
