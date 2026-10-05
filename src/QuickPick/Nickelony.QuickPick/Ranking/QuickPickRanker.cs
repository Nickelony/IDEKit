using Nickelony.Matching;

namespace Nickelony.QuickPick;

/// <summary>Turns the items a provider returned into the rows of the list: it filters, ranks, groups and highlights them.</summary>
/// <remarks>
/// <para>
/// The filter mode of the provider decides the filter and the order. A query keeps the items it matches and puts the highest score first.
/// Equal scores order by recency, then as VS Code's scorer orders them: the more compact match of the label, the shorter label, a match in the label
/// over one in the description, the shorter distance of the match, the shorter text, the label in alphabetical order, and last the order
/// the provider returned. An empty query keeps the order of the provider, except that a provider with a recent group lists its recently
/// accepted items first, in the order of use, under that group. A group is listed by its order, and groups of equal order by the position
/// of their first item.
/// </para>
/// <para>
/// The ranker holds no state and reads only its arguments, so it runs on any thread. It checks the token between candidates.
/// </para>
/// </remarks>
internal static class QuickPickRanker
{
	private const int LabelScoreThreshold = 1 << 16;
	private const int LabelPrefixScoreThreshold = 1 << 17;
	private const int IdentityScore = 1 << 18;

	/// <summary>Builds the rows for one query.</summary>
	/// <param name="filterMode">How the provider that returned the items asked its items to be filtered.</param>
	/// <param name="recentGroup">The group of the recent items of that provider, or <see langword="null"/> when it keeps none.</param>
	/// <param name="items">The items the provider returned; a <see langword="null"/> entry is skipped.</param>
	/// <param name="query">The text typed after the prefix of the provider.</param>
	/// <param name="recentKeys">The keys of the recently accepted items of the provider, the latest first.</param>
	/// <param name="maxItemCount">The largest number of items to list.</param>
	/// <param name="cancellationToken">A token that stops the ranking between two candidates.</param>
	/// <exception cref="OperationCanceledException">The token was canceled.</exception>
	internal static IReadOnlyList<QuickPickRow> Rank(
		QuickPickFilterMode filterMode,
		QuickPickGroup? recentGroup,
		IReadOnlyList<QuickPickItem> items,
		string query,
		IReadOnlyList<string> recentKeys,
		int maxItemCount,
		CancellationToken cancellationToken = default
	)
	{
		Dictionary<string, int> recentRanks = RankRecentKeys(recentGroup is null ? [] : recentKeys);
		bool listsRecentFirst = false;

		List<Candidate> candidates = filterMode switch
		{
			QuickPickFilterMode.None => Unfiltered(items, recentRanks, cancellationToken),
			QuickPickFilterMode.Words => FilterByWords(items, query, recentRanks, cancellationToken, out listsRecentFirst),
			_ => FilterByScore(filterMode, items, query, recentRanks, cancellationToken, out listsRecentFirst),
		};

		if (recentGroup is not null && listsRecentFirst)
			AssignRecentGroup(candidates, recentGroup);

		return BuildRows(GroupByOrder(candidates), maxItemCount);
	}

	private static Dictionary<string, int> RankRecentKeys(IReadOnlyList<string> recentKeys)
	{
		Dictionary<string, int> ranks = new(recentKeys.Count, StringComparer.Ordinal);

		for (int index = 0; index < recentKeys.Count; index++)
			ranks.TryAdd(recentKeys[index], index);

		return ranks;
	}

	private static List<Candidate> Unfiltered(
		IReadOnlyList<QuickPickItem> items,
		Dictionary<string, int> recentRanks,
		CancellationToken cancellationToken
	)
	{
		List<Candidate> candidates = new(items.Count);

		for (int index = 0; index < items.Count; index++)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (items[index] is { } item)
				candidates.Add(new Candidate(item, Score: 0, RecentRank: RecentRankOf(item, recentRanks), index));
		}

		return candidates;
	}

	private static int RecentRankOf(QuickPickItem item, Dictionary<string, int> recentRanks) =>
		recentRanks.TryGetValue(item.Key, out int rank) ? rank : int.MaxValue;

	// The words of the command palette keep the order of the provider, filter by prefix, word starts and substring, and put the recent items first.
	private static List<Candidate> FilterByWords(
		IReadOnlyList<QuickPickItem> items,
		string query,
		Dictionary<string, int> recentRanks,
		CancellationToken cancellationToken,
		out bool listsRecentFirst
	)
	{
		string[] words = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
		List<Candidate> candidates = new(items.Count);

		// The command palette keeps the recent items first whatever the user types.
		listsRecentFirst = true;

		for (int index = 0; index < items.Count; index++)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (items[index] is not { } item)
				continue;

			QuickPickItem shown = item;

			if (words.Length > 0)
			{
				if (!TryMatchEveryWord(words, item, out IReadOnlyList<MatchRange> labelRanges, out IReadOnlyList<MatchRange> descriptionRanges))
					continue;

				shown = item with { LabelHighlights = labelRanges, DescriptionHighlights = descriptionRanges };
			}

			candidates.Add(new Candidate(Item: shown, Score: 0, RecentRank: RecentRankOf(item, recentRanks), index));
		}

		candidates.Sort(CompareByRecencyThenOrder);

		return candidates;
	}

	// Every word of the query matches the label or else the description, wherever the words stand, so the order of the words is free.
	private static bool TryMatchEveryWord(
		string[] words,
		QuickPickItem item,
		out IReadOnlyList<MatchRange> labelRanges,
		out IReadOnlyList<MatchRange> descriptionRanges
	)
	{
		List<MatchRange> inLabel = [];
		List<MatchRange> inDescription = [];

		foreach (string word in words)
		{
			if (WordMatcher.TryMatch(word, item.Label, out IReadOnlyList<MatchRange> labelMatch))
			{
				inLabel.AddRange(labelMatch);
			}
			else if (
				!string.IsNullOrEmpty(item.Description)
				&& WordMatcher.TryMatch(word, item.Description, out IReadOnlyList<MatchRange> descriptionMatch)
			)
			{
				inDescription.AddRange(descriptionMatch);
			}
			else
			{
				labelRanges = [];
				descriptionRanges = [];
				return false;
			}
		}

		labelRanges = MergeRanges(inLabel);
		descriptionRanges = MergeRanges(inDescription);

		return true;
	}

	// The ranges of several words, ascending, with the overlapping and touching ones joined.
	private static List<MatchRange> MergeRanges(List<MatchRange> ranges)
	{
		ranges.Sort(static (left, right) => left.Offset.CompareTo(right.Offset));

		List<MatchRange> merged = [];

		foreach (MatchRange range in ranges)
		{
			if (merged.Count > 0 && range.Offset <= merged[^1].EndOffset)
			{
				MatchRange last = merged[^1];
				merged[^1] = new MatchRange(last.Offset, Math.Max(last.EndOffset, range.EndOffset) - last.Offset);
			}
			else
			{
				merged.Add(range);
			}
		}

		return merged;
	}

	private static List<Candidate> FilterByScore(
		QuickPickFilterMode filterMode,
		IReadOnlyList<QuickPickItem> items,
		string query,
		Dictionary<string, int> recentRanks,
		CancellationToken cancellationToken,
		out bool listsRecentFirst
	)
	{
		var matcher = new FuzzyMatcher(query, filterMode == QuickPickFilterMode.Path ? FuzzyMatchMode.Path : FuzzyMatchMode.Text);

		// A query lets recency break a tie only; an empty query lists the recent items first.
		listsRecentFirst = matcher.IsEmpty;

		if (matcher.IsEmpty)
		{
			List<Candidate> all = Unfiltered(items, recentRanks, cancellationToken);

			all.Sort(CompareByRecencyThenOrder);

			return all;
		}

		bool keepsAll = filterMode == QuickPickFilterMode.RankOnly;
		List<Candidate> candidates = new(items.Count);

		for (int index = 0; index < items.Count; index++)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (items[index] is not { } item)
				continue;

			int score = matcher.Score(item.Label, item.Description);

			if (score > 0 || keepsAll)
				candidates.Add(new Candidate(item, score, RecentRank: RecentRankOf(item, recentRanks), index) { Matcher = matcher });
		}

		candidates.Sort(CompareByScore);

		return candidates;
	}

	private static int CompareByRecencyThenOrder(Candidate left, Candidate right)
	{
		int byRecency = left.RecentRank.CompareTo(right.RecentRank);
		return byRecency != 0 ? byRecency : left.Index.CompareTo(right.Index);
	}

	// The order of VS Code's scorer, with the recency of an item breaking a tie in the score first.
	private static int CompareByScore(Candidate left, Candidate right)
	{
		if ((left.Score == IdentityScore || right.Score == IdentityScore) && left.Score != right.Score)
			return left.Score == IdentityScore ? -1 : 1;

		if (left.Score != right.Score)
			return right.Score.CompareTo(left.Score);

		// Items the query does not match, which a rank-only mode keeps, stay in the order of the provider.
		if (left.Score == 0)
			return left.Index.CompareTo(right.Index);

		int byRecency = left.RecentRank.CompareTo(right.RecentRank);

		if (byRecency != 0)
			return byRecency;

		if (left.Score > LabelScoreThreshold)
		{
			if (left.Score < LabelPrefixScoreThreshold)
			{
				int byCompactness = CompareByMatchLength(left.LabelRanges, right.LabelRanges);

				if (byCompactness != 0)
					return byCompactness;
			}

			int byLabelLength = left.Item.Label.Length.CompareTo(right.Item.Label.Length);

			if (byLabelLength != 0)
				return byLabelLength;
		}

		bool leftMatchesLabel = left.LabelRanges.Count > 0;
		bool rightMatchesLabel = right.LabelRanges.Count > 0;

		if (leftMatchesLabel != rightMatchesLabel)
			return leftMatchesLabel ? -1 : 1;

		int leftDistance = MatchDistance(left);
		int rightDistance = MatchDistance(right);

		if (leftDistance != 0 && rightDistance != 0 && leftDistance != rightDistance)
			return leftDistance.CompareTo(rightDistance);

		int byTextLength = TextLength(left.Item).CompareTo(TextLength(right.Item));

		if (byTextLength != 0)
			return byTextLength;

		int byLabel = string.Compare(left.Item.Label, right.Item.Label, StringComparison.OrdinalIgnoreCase);

		return byLabel != 0 ? byLabel : left.Index.CompareTo(right.Index);
	}

	private static int TextLength(QuickPickItem item) => item.Label.Length + (item.Description?.Length ?? 0);

	// The span from the first to the last matched character of the label: a shorter span is a more compact match.
	private static int CompareByMatchLength(IReadOnlyList<MatchRange> left, IReadOnlyList<MatchRange> right)
	{
		if (left.Count == 0 && right.Count == 0)
			return 0;

		if (right.Count == 0)
			return -1;

		if (left.Count == 0)
			return 1;

		return SpanOf(left).CompareTo(SpanOf(right));
	}

	private static int SpanOf(IReadOnlyList<MatchRange> ranges) => ranges[^1].EndOffset - ranges[0].Offset;

	// How far apart the first and the last matched characters are across the description and the label.
	private static int MatchDistance(Candidate candidate)
	{
		IReadOnlyList<MatchRange> label = candidate.LabelRanges;
		IReadOnlyList<MatchRange> description = candidate.DescriptionRanges;

		int start = -1;
		int end = -1;

		if (description.Count > 0)
			start = description[0].Offset;
		else if (label.Count > 0)
			start = label[0].Offset;

		if (label.Count > 0)
		{
			end = label[^1].EndOffset;

			if (description.Count > 0 && candidate.Item.Description is { } text)
				end += text.Length;
		}
		else if (description.Count > 0)
		{
			end = description[^1].EndOffset;
		}

		return end - start;
	}

	private static void AssignRecentGroup(List<Candidate> candidates, QuickPickGroup recentGroup)
	{
		for (int index = 0; index < candidates.Count; index++)
		{
			Candidate candidate = candidates[index];

			if (candidate.RecentRank != int.MaxValue)
				candidates[index] = candidate with { Item = candidate.Item with { Group = recentGroup } };
		}
	}

	// Groups are listed by their order and then by the position of their first item; the items that belong to no group form one run of order 0.
	private static List<List<Candidate>> GroupByOrder(List<Candidate> candidates)
	{
		List<(int Order, List<Candidate> Run)> runs = [];
		Dictionary<string, List<Candidate>> byGroupId = new(StringComparer.Ordinal);
		List<Candidate>? ungrouped = null;

		foreach (Candidate candidate in candidates)
		{
			List<Candidate> run;

			if (candidate.Item.Group is { } group)
			{
				if (!byGroupId.TryGetValue(group.Id, out run!))
				{
					run = [];

					byGroupId.Add(group.Id, run);
					runs.Add((group.Order, run));
				}
			}
			else
			{
				if (ungrouped is null)
				{
					ungrouped = [];
					runs.Add((Order: 0, ungrouped));
				}

				run = ungrouped;
			}

			run.Add(candidate);
		}

		return [.. runs.OrderBy(entry => entry.Order).Select(entry => entry.Run)];
	}

	private static List<QuickPickRow> BuildRows(List<List<Candidate>> groups, int maxItemCount)
	{
		List<QuickPickRow> rows = [];
		int remaining = maxItemCount;

		foreach (List<Candidate> run in groups)
		{
			if (remaining <= 0)
				break;

			int count = Math.Min(run.Count, remaining);
			remaining -= count;

			if (run[0].Item.Group is { Label: { } label } group)
				rows.Add(QuickPickRow.ForSeparator(group.ShowsCount ? $"{label} ({count})" : label));

			for (int index = 0; index < count; index++)
				rows.Add(QuickPickRow.ForItem(run[index].Highlighted()));
		}

		return rows;
	}

	private sealed record Candidate(QuickPickItem Item, int Score, int RecentRank, int Index)
	{
		private FuzzyMatch? _match;

		internal FuzzyMatcher? Matcher { get; init; }

		internal IReadOnlyList<MatchRange> LabelRanges => MatchOf()?.LabelRanges ?? [];

		internal IReadOnlyList<MatchRange> DescriptionRanges => MatchOf()?.DescriptionRanges ?? [];

		// The ranges are built once, for the candidates that a comparison or the list needs.
		private FuzzyMatch? MatchOf()
		{
			if (Matcher is null || Score == 0)
				return null;

			if (_match is null && Matcher.TryMatch(Item.Label, Item.Description, out FuzzyMatch match))
				_match = match;

			return _match;
		}

		internal QuickPickItem Highlighted() =>
			MatchOf() is { } match ? Item with { LabelHighlights = match.LabelRanges, DescriptionHighlights = match.DescriptionRanges } : Item;
	}
}
