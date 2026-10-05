using System.Buffers;

namespace Nickelony.Matching;

/// <summary>
/// Scores candidates against one query as a subsequence match and reports the matched characters, as the quick open of VS Code does.
/// </summary>
/// <remarks>
/// <para>
/// Every character of the query must appear in the candidate in order, compared ordinally and without regard to case. A match scores
/// a point per matched character, and earns a bonus for a character at the start of the candidate, a character at a word start (after a space
/// or one of <c>/ \ _ - . ' " :</c>), an upper-case letter that begins a run, a character that continues a run of matched characters, and a character
/// whose case equals the query's. A candidate whose label starts with the query scores above any other label match, a label match scores
/// above a match that needs the description, and in <see cref="FuzzyMatchMode.Path"/> a query equal to the whole path scores highest.
/// </para>
/// <para>
/// The query is prepared as VS Code prepares it. The characters <c>*</c>, <c>…</c> and <c>"</c> and the whitespace
/// are dropped from it. Whitespace separates pieces: each piece must match, the scores add, and the matched characters merge. A piece wrapped
/// in double quotes must match as one contiguous run. The <c>/</c> and the <c>\</c> are equal.
/// </para>
/// <para>
/// The scores are those of VS Code's scorer, with these differences. A description joins the label
/// with a <c>/</c> in <see cref="FuzzyMatchMode.Path"/> and directly in <see cref="FuzzyMatchMode.Text"/>, whatever the platform. The query keeps
/// a trailing <c>#</c>, which VS Code's scorer drops, because a host removes the prefix of a mode before it passes the query. A candidate is compared
/// one UTF-16 unit at a time, with the invariant lower case. A pair of query piece and candidate whose product of lengths exceeds about four
/// million characters never matches. The matcher only scores: a caller that orders equal scores breaks the tie as VS Code does, by the compactness
/// of the match, the length of the label, the distance of the match, the length of the text and the label.
/// </para>
/// <para>
/// The matcher is immutable and safe to call from several threads. A candidate that does not hold the query as a subsequence is rejected in one pass
/// over its text. <see cref="Score"/> allocates nothing for a candidate of ordinary length, so a host can score every candidate on every keystroke;
/// <see cref="TryMatch"/> also builds the ranges to highlight, so a host calls it for the candidates it shows.
/// </para>
/// </remarks>
public sealed class FuzzyMatcher
{
	// A label match scores above any description match, a label that starts with the query above any other label match, and a path that
	// equals the query above everything.
	private const int LabelScoreThreshold = 1 << 16;
	private const int LabelPrefixScoreThreshold = 1 << 17;
	private const int IdentityScore = 1 << 18;

	// The working storage of one score lives on the stack up to these sizes and comes from the shared pool beyond them.
	private const int StackIntLimit = 1024;
	private const int StackCharLimit = 256;

	// A candidate whose score matrix would hold more cells than this does not match, so a pathological text cannot exhaust memory.
	private const long MaximumCellCount = 1 << 22;

	private readonly FuzzyMatchMode _mode;
	private readonly Piece[] _pieces;
	private readonly string _pathQuery;
	private readonly bool _containsPathSeparator;

	/// <param name="query">The text to look for. Whitespace separates pieces that must all match.</param>
	/// <param name="mode">How the matcher reads the label and the description of a candidate.</param>
	/// <exception cref="ArgumentNullException"><paramref name="query"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="mode"/> is not a defined value.</exception>
	public FuzzyMatcher(string query, FuzzyMatchMode mode = FuzzyMatchMode.Text)
	{
		ArgumentNullException.ThrowIfNull(query);

		if (!Enum.IsDefined(mode))
			throw new ArgumentOutOfRangeException(nameof(mode), mode, "The mode must be a defined value.");

		_mode = mode;
		_pieces = ParsePieces(query);
		_pathQuery = query.Trim().Replace('\\', '/');
		_containsPathSeparator = query.AsSpan().IndexOfAny('/', '\\') >= 0;
	}

	/// <summary>Whether the query holds no characters to match, which no candidate matches.</summary>
	/// <remarks>
	/// A host that shows every candidate for an empty query tests this before it scores.
	/// </remarks>
	public bool IsEmpty => _pieces.Length == 0;

	/// <summary>Scores a candidate.</summary>
	/// <param name="label">The primary text of the candidate: a name, or a file name in <see cref="FuzzyMatchMode.Path"/>.</param>
	/// <param name="description">
	/// The secondary text of the candidate, such as a qualifier or a folder path, or <see langword="null"/> for none.
	/// </param>
	/// <returns>A positive score when the query matches the candidate, and <c>0</c> when it does not or the query is empty.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="label"/> is <see langword="null"/>.</exception>
	public int Score(string label, string? description = null)
	{
		ArgumentNullException.ThrowIfNull(label);
		return ScoreCandidate(label, description, labelPositions: null, descriptionPositions: null);
	}

	/// <summary>Matches a candidate and reports the characters to highlight.</summary>
	/// <param name="label">The primary text of the candidate: a name, or a file name in <see cref="FuzzyMatchMode.Path"/>.</param>
	/// <param name="description">
	/// The secondary text of the candidate, such as a qualifier or a folder path, or <see langword="null"/> for none.
	/// </param>
	/// <param name="match">The match when the method returns <see langword="true"/>; otherwise, <see langword="default"/>.</param>
	/// <returns><see langword="true"/> when the query matches the candidate; otherwise, <see langword="false"/>.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="label"/> is <see langword="null"/>.</exception>
	public bool TryMatch(string label, string? description, out FuzzyMatch match)
	{
		ArgumentNullException.ThrowIfNull(label);

		var labelPositions = new List<int>();
		var descriptionPositions = new List<int>();

		int score = ScoreCandidate(label, description, labelPositions, descriptionPositions);

		if (score == 0)
		{
			match = default;
			return false;
		}

		match = new FuzzyMatch(score, ToRanges(labelPositions), ToRanges(descriptionPositions));
		return true;
	}

	private static Piece[] ParsePieces(string query)
	{
		List<Piece> pieces = [];

		foreach (string part in query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
		{
			string text = Normalize(part);

			if (text.Length == 0)
				continue;

			bool isQuoted = part.Length >= 2 && part[0] == '"' && part[^1] == '"';

			pieces.Add(new Piece(text, ToLower(text), isQuoted));
		}

		return [.. pieces];
	}

	// The characters that VS Code drops from a query: the wildcard, the ellipsis, the quote and the whitespace.
	private static string Normalize(string text)
	{
		int dropped = 0;

		foreach (char character in text)
		{
			if (IsDropped(character))
				dropped++;
		}

		if (dropped == 0)
			return text;

		return string.Create(
			text.Length - dropped,
			text,

			static (span, source) =>
			{
				int written = 0;

				foreach (char character in source)
				{
					if (!IsDropped(character))
						span[written++] = character;
				}
			}
		);
	}

	private static bool IsDropped(char character) => character is '*' or '…' or '"' || char.IsWhiteSpace(character);

	private static string ToLower(string text)
	{
		return string.Create(
			text.Length,
			text,

			static (span, source) =>
			{
				for (int index = 0; index < span.Length; index++)
					span[index] = char.ToLowerInvariant(source[index]);
			}
		);
	}

	private static MatchRange[] ToRanges(List<int> positions)
	{
		if (positions.Count == 0)
			return [];

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

	private static bool IsPathSeparator(char character) => character is '/' or '\\';

	private static bool ConsiderAsEqual(char lowerQueryCharacter, char lowerTargetCharacter) =>
		lowerQueryCharacter == lowerTargetCharacter || (IsPathSeparator(lowerQueryCharacter) && IsPathSeparator(lowerTargetCharacter));

	private static int SeparatorBonus(char character)
	{
		return character switch
		{
			'/' or '\\' => 5,
			'_' or '-' or '.' or ' ' or '\'' or '"' or ':' => 4,
			_ => 0,
		};
	}

	private int ScoreCandidate(string label, string? description, List<int>? labelPositions, List<int>? descriptionPositions)
	{
		if (_pieces.Length == 0)
			return 0;

		if (_mode == FuzzyMatchMode.Path && IsWholePath(label, description))
		{
			AddAll(labelPositions, label.Length);
			AddAll(descriptionPositions, description?.Length ?? 0);

			return IdentityScore;
		}

		if (_pieces.Length == 1)
			return ScorePiece(piece: _pieces[0], label, description, labelPositions, descriptionPositions);

		// Every piece must match, the scores add, and the matched characters of the pieces merge.
		List<int>? labelUnion = labelPositions is null ? null : [];
		List<int>? descriptionUnion = descriptionPositions is null ? null : [];

		int total = 0;

		foreach (Piece piece in _pieces)
		{
			List<int>? labelOfPiece = labelUnion is null ? null : [];
			List<int>? descriptionOfPiece = descriptionUnion is null ? null : [];

			int score = ScorePiece(piece, label, description, labelPositions: labelOfPiece, descriptionPositions: descriptionOfPiece);

			if (score == 0)
				return 0;

			total += score;

			labelUnion?.AddRange(labelOfPiece!);
			descriptionUnion?.AddRange(descriptionOfPiece!);
		}

		AddDistinctSorted(labelPositions, labelUnion);
		AddDistinctSorted(descriptionPositions, descriptionUnion);

		return total;
	}

	private static void AddAll(List<int>? positions, int count)
	{
		if (positions is null)
			return;

		for (int index = 0; index < count; index++)
			positions.Add(index);
	}

	private static void AddDistinctSorted(List<int>? target, List<int>? source)
	{
		if (target is null || source is null)
			return;

		source.Sort();

		int previous = -1;

		foreach (int position in source)
		{
			if (position != previous)
				target.Add(position);

			previous = position;
		}
	}

	// A query that names the whole path, ignoring case and the kind of separator.
	private bool IsWholePath(string label, string? description)
	{
		int folderLength = string.IsNullOrEmpty(description) ? 0 : description.Length + 1;

		if (_pathQuery.Length != folderLength + label.Length)
			return false;

		for (int index = 0; index < _pathQuery.Length; index++)
		{
			char expected =
				index < folderLength - 1 ? description![index]
				: index == folderLength - 1 ? '/'
				: label[index - folderLength];

			if (!ConsiderAsEqual(char.ToLowerInvariant(_pathQuery[index]), char.ToLowerInvariant(expected)))
				return false;
		}

		return true;
	}

	private int ScorePiece(Piece piece, string label, string? description, List<int>? labelPositions, List<int>? descriptionPositions)
	{
		// A path query with a separator names the folders too, so the file name does not decide alone.
		bool prefersLabel = _mode == FuzzyMatchMode.Text || !_containsPathSeparator;
		bool hasDescription = !string.IsNullOrEmpty(description);

		if (prefersLabel || !hasDescription)
		{
			int labelScore = ScoreText(piece, label, labelPositions);

			if (labelScore > 0)
				return LabelBaseScore(piece, label, labelPositions) + labelScore;
		}

		return hasDescription ? ScoreDescriptionAndLabel(piece, description: description!, label, labelPositions, descriptionPositions) : 0;
	}

	// A label that starts with the query ranks above the rest, longer prefixes first, and highlights the prefix.
	private static int LabelBaseScore(Piece piece, string label, List<int>? labelPositions)
	{
		if (!StartsWithPiece(piece, label))
			return LabelScoreThreshold;

		if (labelPositions is not null)
		{
			labelPositions.Clear();
			AddAll(labelPositions, piece.Text.Length);
		}

		return LabelPrefixScoreThreshold + (int)Math.Round(100.0 * piece.Text.Length / label.Length, MidpointRounding.AwayFromZero);
	}

	private static bool StartsWithPiece(Piece piece, string label)
	{
		if (label.Length < piece.Text.Length)
			return false;

		for (int index = 0; index < piece.Text.Length; index++)
		{
			if (!ConsiderAsEqual(piece.Lower[index], char.ToLowerInvariant(label[index])))
				return false;
		}

		return true;
	}

	private int ScoreDescriptionAndLabel(Piece piece, string description, string label, List<int>? labelPositions, List<int>? descriptionPositions)
	{
		// In path mode a separator joins the folder and the file name; otherwise, the label follows the description directly.
		int joinerLength = _mode == FuzzyMatchMode.Path ? 1 : 0;
		int labelStart = description.Length + joinerLength;
		int length = labelStart + label.Length;

		char[]? rented = null;
		Span<char> text = length <= StackCharLimit ? stackalloc char[length] : (rented = ArrayPool<char>.Shared.Rent(length)).AsSpan(0, length);

		try
		{
			description.CopyTo(text);

			if (joinerLength > 0)
				text[description.Length] = '/';

			label.CopyTo(text[labelStart..]);

			List<int>? positions = labelPositions is null ? null : [];
			int score = ScoreText(piece, text, positions);

			if (score > 0 && positions is not null)
			{
				// The character that joins the folder and the file name belongs to neither part, so a match on it highlights nothing.
				foreach (int position in positions)
				{
					if (position < description.Length)
						descriptionPositions?.Add(position);
					else if (position >= labelStart)
						labelPositions!.Add(position - labelStart);
				}
			}

			return score;
		}
		finally
		{
			if (rented is not null)
				ArrayPool<char>.Shared.Return(rented);
		}
	}

	// Whether the characters of the piece appear in the target in order, which every match needs and which one pass settles.
	private static bool HoldsSubsequence(Piece piece, ReadOnlySpan<char> target)
	{
		int matched = 0;

		foreach (char character in target)
		{
			if (ConsiderAsEqual(piece.Lower[matched], char.ToLowerInvariant(character)) && ++matched == piece.Lower.Length)
				return true;
		}

		return false;
	}

	private static bool StartsWithPieceAt(Piece piece, ReadOnlySpan<char> target, int start)
	{
		if (target.Length - start < piece.Lower.Length)
			return false;

		for (int index = 0; index < piece.Lower.Length; index++)
		{
			if (!ConsiderAsEqual(piece.Lower[index], char.ToLowerInvariant(target[start + index])))
				return false;
		}

		return true;
	}

	// Scores a text as the best subsequence match of the piece over a matrix of query characters by text characters, where a cell
	// continues the match of the cell diagonally before it.
	private static int ScoreText(Piece piece, ReadOnlySpan<char> target, List<int>? positions)
	{
		int queryLength = piece.Text.Length;
		int targetLength = target.Length;

		if (targetLength < queryLength || (long)queryLength * targetLength > MaximumCellCount || !HoldsSubsequence(piece, target))
			return 0;

		int cellCount = queryLength * targetLength;
		int[]? rented = null;

		Span<int> storage =
			cellCount * 2 <= StackIntLimit
				? stackalloc int[cellCount * 2]
				: (rented = ArrayPool<int>.Shared.Rent(cellCount * 2)).AsSpan(0, cellCount * 2);

		try
		{
			Span<int> scores = storage[..cellCount];
			Span<int> runLengths = storage[cellCount..];

			for (int queryIndex = 0; queryIndex < queryLength; queryIndex++)
			{
				for (int targetIndex = 0; targetIndex < targetLength; targetIndex++)
				{
					int current = (queryIndex * targetLength) + targetIndex;
					int leftScore = targetIndex > 0 ? scores[current - 1] : 0;

					bool hasDiagonal = queryIndex > 0 && targetIndex > 0;

					int diagonalScore = hasDiagonal ? scores[current - targetLength - 1] : 0;
					int runLength = hasDiagonal ? runLengths[current - targetLength - 1] : 0;

					// After the first query character, a character scores only when the previous query character matched before it.
					int score = diagonalScore == 0 && queryIndex > 0 ? 0 : ScoreCharacter(piece, queryIndex, target, targetIndex, runLength);

					// A quoted piece may start its run only where the whole piece follows.
					bool startsRun = !piece.ExpectsContiguousRun || queryIndex > 0 || StartsWithPieceAt(piece, target, targetIndex);

					if (score != 0 && diagonalScore + score >= leftScore && startsRun)
					{
						runLengths[current] = runLength + 1;
						scores[current] = diagonalScore + score;
					}
					else
					{
						runLengths[current] = 0;
						scores[current] = leftScore;
					}
				}
			}

			int result = scores[cellCount - 1];

			if (result != 0 && positions is not null)
				CollectPositions(runLengths, targetLength, queryLength, positions);

			return result;
		}
		finally
		{
			if (rented is not null)
				ArrayPool<int>.Shared.Return(rented);
		}
	}

	private static void CollectPositions(ReadOnlySpan<int> runLengths, int targetLength, int queryLength, List<int> positions)
	{
		int first = positions.Count;
		int queryIndex = queryLength - 1;
		int targetIndex = targetLength - 1;

		while (queryIndex >= 0 && targetIndex >= 0)
		{
			if (runLengths[(queryIndex * targetLength) + targetIndex] == 0)
			{
				targetIndex--;
				continue;
			}

			positions.Add(targetIndex);
			queryIndex--;
			targetIndex--;
		}

		positions.Reverse(first, positions.Count - first);
	}

	private static int ScoreCharacter(Piece piece, int queryIndex, ReadOnlySpan<char> target, int targetIndex, int runLength)
	{
		char targetCharacter = target[targetIndex];

		if (!ConsiderAsEqual(piece.Lower[queryIndex], char.ToLowerInvariant(targetCharacter)))
			return 0;

		int score = 1;

		if (runLength > 0)
			score += (Math.Min(runLength, 3) * 6) + (Math.Max(0, runLength - 3) * 3);

		if (piece.Text[queryIndex] == targetCharacter)
			score += 1;

		if (targetIndex == 0)
			return score + 8;

		int separatorBonus = SeparatorBonus(target[targetIndex - 1]);

		if (separatorBonus > 0)
			return score + separatorBonus;

		// An upper-case letter that begins a run counts as a word start, as in the camel case of IUiDispatcher and XMLHttpRequest.
		if (runLength == 0 && char.IsUpper(targetCharacter))
			score += 2;

		return score;
	}

	private sealed record Piece(string Text, string Lower, bool ExpectsContiguousRun);
}
