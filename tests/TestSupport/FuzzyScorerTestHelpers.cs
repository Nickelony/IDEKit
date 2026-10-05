using System.Text.RegularExpressions;

namespace Nickelony.Testing;

/// <summary>
/// A transcription of the scoring and ordering of VS Code's <c>fuzzyScorer</c> (<c>prepareQuery</c>, <c>scoreFuzzy</c>,
/// <c>scoreItemFuzzy</c> and <c>compareItemsByFuzzyScore</c>), used as the reference that the matcher and the quick-pick ranker are compared with.
/// </summary>
/// <remarks>
/// The oracle keeps VS Code's rules where the library differs on purpose: it strips a trailing <c>#</c> from a query and splits a query
/// on the space character only. The path separator is <c>/</c>.
/// </remarks>
internal static partial class FuzzyScorerTestHelpers
{
	private const int LabelThreshold = 1 << 16;
	private const int PrefixThreshold = 1 << 17;
	private const int Identity = 1 << 18;

	private const char Separator = '/';

	internal sealed record Piece(string Normalized, string NormalizedLower, bool ExpectContiguous);

	internal sealed record Query(
		string PathNormalized,
		string Normalized,
		string NormalizedLower,
		List<Piece>? Pieces,
		bool ContainsPathSeparator,
		bool ExpectContiguous
	);

	internal sealed record Item(string Label, string? Description, string? Path, int Index);

	internal sealed record ItemScore(int Score, List<(int Start, int End)>? LabelMatch, List<(int Start, int End)>? DescriptionMatch);

	internal static Query Prepare(string original)
	{
		(string pathNormalized, string normalized, string lower) = Normalize(original);

		List<Piece>? pieces = null;
		string[] parts = original.Split(' ');

		if (parts.Length > 1)
		{
			foreach (string part in parts)
			{
				(_, string pieceNormalized, string pieceLower) = Normalize(part);

				if (pieceNormalized.Length > 0)
					(pieces ??= []).Add(new Piece(pieceNormalized, pieceLower, IsQuoted(part)));
			}
		}

		return new Query(
			pathNormalized,
			normalized,
			NormalizedLower: lower,
			pieces,
			ContainsPathSeparator: pathNormalized.Contains(Separator),
			ExpectContiguous: IsQuoted(original)
		);
	}

	internal static ItemScore ScoreItem(Item item, Query query)
	{
		if (query.Normalized.Length == 0)
			return new ItemScore(0, null, null);

		bool preferLabel = item.Path is null || !query.ContainsPathSeparator;

		if (item.Path is not null && string.Equals(query.PathNormalized, item.Path.Replace('\\', Separator), StringComparison.OrdinalIgnoreCase))
			return new ItemScore(Identity, [(0, item.Label.Length)], item.Description is null ? null : [(0, item.Description.Length)]);

		if (query.Pieces is { Count: > 1 })
		{
			int total = 0;

			List<(int, int)> labelMatches = [];
			List<(int, int)> descriptionMatches = [];

			foreach (Piece piece in query.Pieces)
			{
				ItemScore score = ScorePiece(
					item,
					normalized: piece.Normalized,
					lower: piece.NormalizedLower,
					expectContiguous: piece.ExpectContiguous,
					preferLabel
				);

				if (score.Score == 0)
					return new ItemScore(0, null, null);

				total += score.Score;

				labelMatches.AddRange(score.LabelMatch ?? []);
				descriptionMatches.AddRange(score.DescriptionMatch ?? []);
			}

			return new ItemScore(total, Merge(labelMatches), Merge(descriptionMatches));
		}

		return ScorePiece(item, normalized: query.Normalized, lower: query.NormalizedLower, expectContiguous: query.ExpectContiguous, preferLabel);
	}

	/// <summary>Scores and orders the items as <c>compareItemsByFuzzyScore</c> does; an item that does not match is left out.</summary>
	internal static List<Item> Rank(IReadOnlyList<Item> items, string query)
	{
		Query prepared = Prepare(query);

		var scored = items.Select(item => (Item: item, Score: ScoreItem(item, prepared))).Where(pair => pair.Score.Score > 0).ToList();

		scored.Sort((left, right) => Compare(left: left.Item, leftScore: left.Score, right: right.Item, rightScore: right.Score));

		return [.. scored.Select(pair => pair.Item)];
	}

	private static (string PathNormalized, string Normalized, string Lower) Normalize(string text)
	{
		string path = text.Replace('\\', Separator);
		string normalized = DroppedCharacters().Replace(path, string.Empty);

		normalized = TrailingHash().Replace(normalized, string.Empty);

		return (path, normalized, normalized.ToLowerInvariant());
	}

	[GeneratedRegex("[\\*…\\s\"]")]
	private static partial Regex DroppedCharacters();

	[GeneratedRegex("(?<=.)#$")]
	private static partial Regex TrailingHash();

	private static bool IsQuoted(string text) => text.StartsWith('"') && text.EndsWith('"');

	private static bool ConsiderAsEqual(char left, char right) => left == right || ((left is '/' or '\\') && (right is '/' or '\\'));

	private static int SeparatorBonus(char character)
	{
		return character switch
		{
			'/' or '\\' => 5,
			'_' or '-' or '.' or ' ' or '\'' or '"' or ':' => 4,
			_ => 0,
		};
	}

	private static int CharacterScore(char query, char queryLower, string target, string targetLower, int index, int sequenceLength)
	{
		if (!ConsiderAsEqual(queryLower, targetLower[index]))
			return 0;

		int score = 1;

		if (sequenceLength > 0)
			score += (Math.Min(sequenceLength, 3) * 6) + (Math.Max(0, sequenceLength - 3) * 3);

		if (query == target[index])
			score += 1;

		if (index == 0)
			return score + 8;

		int bonus = SeparatorBonus(target[index - 1]);

		if (bonus != 0)
			return score + bonus;

		return target[index] is >= 'A' and <= 'Z' && sequenceLength == 0 ? score + 2 : score;
	}

	private static (int Score, List<int> Positions) ScoreFuzzy(string target, string query, string queryLower, bool allowNonContiguous)
	{
		if (target.Length == 0 || query.Length == 0 || target.Length < query.Length)
			return (0, []);

		int targetLength = target.Length;
		int queryLength = query.Length;

		string targetLower = target.ToLowerInvariant();

		int[] scores = new int[queryLength * targetLength];
		int[] sequences = new int[queryLength * targetLength];

		for (int queryIndex = 0; queryIndex < queryLength; queryIndex++)
		{
			int row = queryIndex * targetLength;
			int previousRow = row - targetLength;

			bool hasPreviousQuery = queryIndex > 0;

			for (int targetIndex = 0; targetIndex < targetLength; targetIndex++)
			{
				bool hasPreviousTarget = targetIndex > 0;

				int current = row + targetIndex;
				int diagonal = previousRow + targetIndex - 1;
				int leftScore = hasPreviousTarget ? scores[current - 1] : 0;
				int diagonalScore = hasPreviousQuery && hasPreviousTarget ? scores[diagonal] : 0;
				int sequenceLength = hasPreviousQuery && hasPreviousTarget ? sequences[diagonal] : 0;

				int score =
					diagonalScore == 0 && hasPreviousQuery
						? 0
						: CharacterScore(
							query: query[queryIndex],
							queryLower: queryLower[queryIndex],
							target,
							targetLower,
							index: targetIndex,
							sequenceLength
						);

				if (
					score != 0
					&& diagonalScore + score >= leftScore
					&& (allowNonContiguous || hasPreviousQuery || targetLower.AsSpan(targetIndex).StartsWith(queryLower))
				)
				{
					sequences[current] = sequenceLength + 1;
					scores[current] = diagonalScore + score;
				}
				else
				{
					sequences[current] = 0;
					scores[current] = leftScore;
				}
			}
		}

		List<int> positions = [];

		int matchIndex = queryLength - 1;
		int position = targetLength - 1;

		while (matchIndex >= 0 && position >= 0)
		{
			if (sequences[(matchIndex * targetLength) + position] != 0)
			{
				positions.Add(position);
				matchIndex--;
			}

			position--;
		}

		positions.Reverse();

		return (scores[(queryLength * targetLength) - 1], positions);
	}

	private static List<(int Start, int End)> ToRanges(List<int> positions)
	{
		List<(int Start, int End)> ranges = [];

		foreach (int position in positions)
		{
			if (ranges.Count > 0 && ranges[^1].End == position)
				ranges[^1] = (ranges[^1].Start, position + 1);
			else
				ranges.Add((position, position + 1));
		}

		return ranges;
	}

	private static List<(int Start, int End)> Merge(List<(int Start, int End)> ranges)
	{
		List<(int Start, int End)> merged = [];

		foreach ((int Start, int End) range in ranges.OrderBy(range => range.Start))
		{
			if (merged.Count == 0 || merged[^1].End < range.Start || range.End < merged[^1].Start)
				merged.Add(range);
			else
				merged[^1] = (Math.Min(merged[^1].Start, range.Start), Math.Max(merged[^1].End, range.End));
		}

		return merged;
	}

	private static ItemScore ScorePiece(Item item, string normalized, string lower, bool expectContiguous, bool preferLabel)
	{
		bool allowNonContiguous = !expectContiguous;

		if (preferLabel || string.IsNullOrEmpty(item.Description))
		{
			(int score, List<int> positions) = ScoreFuzzy(target: item.Label, query: normalized, queryLower: lower, allowNonContiguous);

			if (score != 0)
			{
				bool isPrefix = item.Label.StartsWith(normalized, StringComparison.OrdinalIgnoreCase);

				int baseScore = isPrefix
					? PrefixThreshold + (int)Math.Round((double)normalized.Length / item.Label.Length * 100, MidpointRounding.AwayFromZero)
					: LabelThreshold;

				return new ItemScore(baseScore + score, isPrefix ? [(0, normalized.Length)] : ToRanges(positions), null);
			}
		}

		if (string.IsNullOrEmpty(item.Description))
			return new ItemScore(0, null, null);

		string description = item.Path is not null ? item.Description + Separator : item.Description;
		int descriptionLength = description.Length;

		(int total, List<int> matched) = ScoreFuzzy(target: description + item.Label, query: normalized, queryLower: lower, allowNonContiguous);

		if (total == 0)
			return new ItemScore(0, null, null);

		List<(int, int)> labelRanges = [];
		List<(int, int)> descriptionRanges = [];

		foreach ((int start, int end) in ToRanges(matched))
		{
			if (start < descriptionLength && end > descriptionLength)
			{
				labelRanges.Add((0, end - descriptionLength));
				descriptionRanges.Add((start, descriptionLength));
			}
			else if (start >= descriptionLength)
			{
				labelRanges.Add((start - descriptionLength, end - descriptionLength));
			}
			else
			{
				descriptionRanges.Add((start, end));
			}
		}

		return new ItemScore(total, labelRanges, descriptionRanges);
	}

	private static int CompareByMatchLength(List<(int Start, int End)>? left, List<(int Start, int End)>? right)
	{
		if ((left?.Count ?? 0) == 0 && (right?.Count ?? 0) == 0)
			return 0;

		if ((right?.Count ?? 0) == 0)
			return -1;

		if ((left?.Count ?? 0) == 0)
			return 1;

		int leftSpan = left![^1].End - left[0].Start;
		int rightSpan = right![^1].End - right[0].Start;

		return leftSpan == rightSpan ? 0
			: rightSpan < leftSpan ? 1
			: -1;
	}

	private static int MatchDistance(Item item, ItemScore score)
	{
		int start = -1;
		int end = -1;

		if (score.DescriptionMatch is { Count: > 0 })
			start = score.DescriptionMatch[0].Start;
		else if (score.LabelMatch is { Count: > 0 })
			start = score.LabelMatch[0].Start;

		if (score.LabelMatch is { Count: > 0 })
		{
			end = score.LabelMatch[^1].End;

			if (score.DescriptionMatch is { Count: > 0 } && item.Description is not null)
				end += item.Description.Length;
		}
		else if (score.DescriptionMatch is { Count: > 0 })
		{
			end = score.DescriptionMatch[^1].End;
		}

		return end - start;
	}

	private static int Compare(Item left, ItemScore leftScore, Item right, ItemScore rightScore)
	{
		int leftValue = leftScore.Score;
		int rightValue = rightScore.Score;

		if ((leftValue == Identity || rightValue == Identity) && leftValue != rightValue)
			return leftValue == Identity ? -1 : 1;

		if (leftValue > LabelThreshold || rightValue > LabelThreshold)
		{
			if (leftValue != rightValue)
				return leftValue > rightValue ? -1 : 1;

			if (leftValue < PrefixThreshold && rightValue < PrefixThreshold)
			{
				int byLength = CompareByMatchLength(leftScore.LabelMatch, rightScore.LabelMatch);

				if (byLength != 0)
					return byLength;
			}

			if (left.Label.Length != right.Label.Length)
				return left.Label.Length - right.Label.Length;
		}

		if (leftValue != rightValue)
			return leftValue > rightValue ? -1 : 1;

		bool leftHasLabel = leftScore.LabelMatch is { Count: > 0 };
		bool rightHasLabel = rightScore.LabelMatch is { Count: > 0 };

		if (leftHasLabel && !rightHasLabel)
			return -1;

		if (rightHasLabel && !leftHasLabel)
			return 1;

		int leftDistance = MatchDistance(left, leftScore);
		int rightDistance = MatchDistance(right, rightScore);

		if (leftDistance != 0 && rightDistance != 0 && leftDistance != rightDistance)
			return rightDistance > leftDistance ? -1 : 1;

		int leftLength = left.Label.Length + (left.Description?.Length ?? 0);
		int rightLength = right.Label.Length + (right.Description?.Length ?? 0);

		if (leftLength != rightLength)
			return leftLength - rightLength;

		if (left.Path is not null && right.Path is not null && left.Path.Length != right.Path.Length)
			return left.Path.Length - right.Path.Length;

		int byLabel = string.Compare(left.Label, right.Label, StringComparison.OrdinalIgnoreCase);

		return byLabel != 0 ? byLabel : left.Index.CompareTo(right.Index);
	}
}
