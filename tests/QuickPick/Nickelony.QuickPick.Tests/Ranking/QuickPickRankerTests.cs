namespace Nickelony.QuickPick.Tests;

/// <summary>
/// Pins how the ranker turns the items of a provider into rows: the filter, the order of equal scores, the recent items,
/// the groups with their counts, the bound on the number of items and the highlights.
/// </summary>
[TestClass]
public sealed class QuickPickRankerTests
{
	private static readonly QuickPickGroup s_recentGroup = new("recent", "recently used");

	private static IReadOnlyList<QuickPickRow> Rank(
		ScriptedQuickPickProvider provider,
		string query,
		IReadOnlyList<QuickPickItem> items,
		IReadOnlyList<string>? recentKeys = null,
		int maxItemCount = 100
	)
	{
		return QuickPickRanker.Rank(
			filterMode: provider.FilterMode,
			recentGroup: provider.RecentGroup,
			items,
			query,
			recentKeys: recentKeys ?? [],
			maxItemCount
		);
	}

	private static string[] Texts(IEnumerable<QuickPickRow> rows) =>
		[.. rows.Select(row => row.Item is null ? $"--{row.SeparatorLabel}--" : row.Item.Label)];

	private static QuickPickItem[] Items(params string[] labels) => [.. labels.Select(label => new QuickPickItem(label, label))];

	[TestMethod]
	public void Rank_Query_KeepsOnlyTheItemsItMatches()
	{
		var provider = new ScriptedQuickPickProvider();

		IReadOnlyList<QuickPickRow> rows = Rank(provider, "fmtdoc", Items("Close File", "Format Document", "Format Selection"));

		Assert.AreSequenceEqual(["Format Document"], Texts(rows));
	}

	[TestMethod]
	public void Rank_Query_PutsTheHighestScoreFirst()
	{
		var provider = new ScriptedQuickPickProvider();

		IReadOnlyList<QuickPickRow> rows = Rank(provider, "doc", Items("redoc", "open doc", "Document"));

		// A label that starts with the query ranks first, then the match at a word start, then the inner match.
		Assert.AreSequenceEqual(["Document", "open doc", "redoc"], Texts(rows));
	}

	[TestMethod]
	public void Rank_EqualScores_OrderByLabelThenByTheOrderTheProviderReturned()
	{
		var provider = new ScriptedQuickPickProvider();

		IReadOnlyList<QuickPickRow> rows = Rank(provider, "alp", Items("alpha3", "alpha1", "alpha2"));

		Assert.AreSequenceEqual(["alpha1", "alpha2", "alpha3"], Texts(rows));

		QuickPickItem[] same = [new("x1", "alpha"), new("x2", "alpha")];
		rows = Rank(provider, "alp", [same[1], same[0]]);

		Assert.AreSequenceEqual(["x2", "x1"], [.. rows.Select(row => row.Item!.Key)]);
	}

	[TestMethod]
	public void Rank_EqualScores_PutTheMostRecentlyUsedFirst()
	{
		var provider = new ScriptedQuickPickProvider { RecentGroup = s_recentGroup };

		IReadOnlyList<QuickPickRow> rows = Rank(provider, query: "alp", items: Items("alpha1", "alpha2", "alpha3"), recentKeys: ["alpha3", "alpha2"]);

		Assert.AreSequenceEqual(["alpha3", "alpha2", "alpha1"], Texts(rows));
	}

	[TestMethod]
	public void Rank_EqualScoresAndRecency_PutTheShorterLabelFirst()
	{
		var provider = new ScriptedQuickPickProvider();

		// Both labels hold the match "ab" the same way after an "x", so only the label length tells them apart.
		IReadOnlyList<QuickPickRow> rows = Rank(provider, "ab", Items("xabxx", "xab"));

		Assert.AreSequenceEqual(["xab", "xabxx"], Texts(rows));
	}

	[TestMethod]
	public void Rank_RecencyOfALowerScore_DoesNotOutrankAHigherScore()
	{
		var provider = new ScriptedQuickPickProvider { RecentGroup = s_recentGroup };

		IReadOnlyList<QuickPickRow> rows = Rank(provider, query: "doc", items: Items("redoc", "Document"), recentKeys: ["redoc"]);

		Assert.AreSequenceEqual(["Document", "redoc"], Texts(rows));
	}

	[TestMethod]
	public void Rank_EmptyQuery_KeepsTheOrderOfTheProvider()
	{
		var provider = new ScriptedQuickPickProvider();

		IReadOnlyList<QuickPickRow> rows = Rank(provider, "", Items("c", "a", "b"));

		Assert.AreSequenceEqual(["c", "a", "b"], Texts(rows));
	}

	[TestMethod]
	public void Rank_EmptyQueryWithARecentGroup_ListsTheRecentItemsFirstInTheOrderOfUse()
	{
		var provider = new ScriptedQuickPickProvider { RecentGroup = s_recentGroup };

		IReadOnlyList<QuickPickRow> rows = Rank(provider, query: "", items: Items("a", "b", "c", "d"), recentKeys: ["c", "a"]);

		Assert.AreSequenceEqual(["--recently used--", "c", "a", "b", "d"], Texts(rows));
		Assert.AreEqual(s_recentGroup, rows[1].Item!.Group);
		Assert.IsNull(rows[3].Item!.Group);
	}

	[TestMethod]
	public void Rank_EmptyQueryWithoutARecentGroup_IgnoresTheRecentKeys()
	{
		var provider = new ScriptedQuickPickProvider();

		IReadOnlyList<QuickPickRow> rows = Rank(provider, query: "", items: Items("a", "b", "c"), recentKeys: ["c"]);

		Assert.AreSequenceEqual(["a", "b", "c"], Texts(rows));
	}

	[TestMethod]
	public void Rank_QueryWithARecentGroup_ShowsNoRecentSeparator()
	{
		var provider = new ScriptedQuickPickProvider { RecentGroup = s_recentGroup };

		IReadOnlyList<QuickPickRow> rows = Rank(provider, query: "a", items: Items("a1", "a2"), recentKeys: ["a2"]);

		Assert.AreSequenceEqual(["a2", "a1"], Texts(rows));
	}

	[TestMethod]
	public void Rank_Groups_AreOrderedByTheirFirstItemAndShowTheirCounts()
	{
		var provider = new ScriptedQuickPickProvider();
		var functions = new QuickPickGroup("function", "Functions", showsCount: true);
		var variables = new QuickPickGroup("variable", "Variables");

		QuickPickItem[] items =
		[
			new("f1", "f1") { Group = functions },
			new("v1", "v1") { Group = variables },
			new("f2", "f2") { Group = functions },
			new("loose", "loose"),
		];

		IReadOnlyList<QuickPickRow> rows = Rank(provider, "", items);

		Assert.AreSequenceEqual(["--Functions (2)--", "f1", "f2", "--Variables--", "v1", "loose"], Texts(rows));
	}

	[TestMethod]
	public void Rank_GroupWithoutALabel_KeepsItsItemsTogetherWithoutASeparator()
	{
		var provider = new ScriptedQuickPickProvider();
		var group = new QuickPickGroup("kept");

		QuickPickItem[] items = [new("a", "a") { Group = group }, new("b", "b"), new("c", "c") { Group = group }];

		Assert.AreSequenceEqual(["a", "c", "b"], Texts(Rank(provider, "", items)));
	}

	[TestMethod]
	public void Rank_GroupCount_FollowsTheFilter()
	{
		var provider = new ScriptedQuickPickProvider();
		var group = new QuickPickGroup("function", "Functions", showsCount: true);

		QuickPickItem[] items =
		[
			new("alpha", "alpha") { Group = group },
			new("beta", "beta") { Group = group },
			new("alps", "alps") { Group = group },
		];

		IReadOnlyList<QuickPickRow> rows = Rank(provider, "alp", items);

		// The shorter label shares more of the query, so "alps" ranks above "alpha".
		Assert.AreSequenceEqual(["--Functions (2)--", "alps", "alpha"], Texts(rows));
	}

	[TestMethod]
	public void Rank_MoreItemsThanTheBound_DropsTheLowestRankedAndCountsWhatIsShown()
	{
		var provider = new ScriptedQuickPickProvider();
		var group = new QuickPickGroup("g", "Group", showsCount: true);

		QuickPickItem[] items = [.. Enumerable.Range(0, 10).Select(index => new QuickPickItem($"i{index}", $"i{index}") { Group = group })];

		IReadOnlyList<QuickPickRow> rows = Rank(provider, query: "", items, maxItemCount: 3);

		Assert.AreSequenceEqual(["--Group (3)--", "i0", "i1", "i2"], Texts(rows));
	}

	[TestMethod]
	public void Rank_NoneFilterMode_KeepsEveryItemInTheOrderOfTheProvider()
	{
		var provider = new ScriptedQuickPickProvider { FilterMode = QuickPickFilterMode.None };

		IReadOnlyList<QuickPickRow> rows = Rank(provider, "zzz", Items("b", "a"));

		Assert.AreSequenceEqual(["b", "a"], Texts(rows));
	}

	[TestMethod]
	public void Rank_PathFilterMode_PrefersTheFileNameOverTheFolders()
	{
		var provider = new ScriptedQuickPickProvider { FilterMode = QuickPickFilterMode.Path };

		QuickPickItem[] items = [new("a", "Program.cs") { Description = "src/main" }, new("b", "Main.cs") { Description = "src" }];

		IReadOnlyList<QuickPickRow> rows = Rank(provider, "main", items);

		Assert.AreSequenceEqual(["Main.cs", "Program.cs"], Texts(rows));
	}

	[TestMethod]
	public void Rank_Query_SetsTheHighlightsOfTheLabelAndTheDescription()
	{
		var provider = new ScriptedQuickPickProvider();

		QuickPickItem[] items = [new("a", "Format Document"), new("b", "Open") { Description = "Category" }];

		IReadOnlyList<QuickPickRow> rows = Rank(provider, "fmtdoc", [items[0]]);

		Assert.AreSequenceEqual(
			[new MatchRange(0, 1), new MatchRange(3, 1), new MatchRange(5, 1), new MatchRange(7, 3)],
			[.. rows[0].Item!.LabelHighlights]
		);

		rows = Rank(provider, "cat", [items[1]]);

		Assert.IsEmpty(rows[0].Item!.LabelHighlights);
		Assert.AreSequenceEqual([new MatchRange(0, 3)], [.. rows[0].Item!.DescriptionHighlights]);
	}

	[TestMethod]
	public void Rank_EmptyQuery_KeepsTheHighlightsTheProviderSet()
	{
		var provider = new ScriptedQuickPickProvider();
		var item = new QuickPickItem("a", "Alpha") { LabelHighlights = [new MatchRange(1, 2)] };

		IReadOnlyList<QuickPickRow> rows = Rank(provider, "", [item]);

		Assert.AreSequenceEqual([new MatchRange(1, 2)], [.. rows[0].Item!.LabelHighlights]);
	}

	[TestMethod]
	public void Rank_NullItem_IsSkipped()
	{
		var provider = new ScriptedQuickPickProvider();

		IReadOnlyList<QuickPickRow> rows = Rank(provider, "", [null!, new QuickPickItem("a", "a")]);

		Assert.AreSequenceEqual(["a"], Texts(rows));
	}

	[TestMethod]
	public void Rank_QueryOfWhitespace_ListsEveryItem()
	{
		var provider = new ScriptedQuickPickProvider();
		Assert.AreSequenceEqual(["a", "b"], Texts(Rank(provider, "  ", Items("a", "b"))));
	}

	[TestMethod]
	public void Rank_WordsMode_KeepsTheOrderOfTheProviderAndMatchesPrefixWordStartsAndSubstrings()
	{
		var provider = new ScriptedQuickPickProvider { FilterMode = QuickPickFilterMode.Words };
		QuickPickItem[] items = Items("Toggle Line Comment", "Open File", "Format Document", "Find Previous", "Go To Definition");

		Assert.AreSequenceEqual(["Toggle Line Comment"], Texts(Rank(provider, "tlc", items)));
		Assert.AreSequenceEqual(["Open File", "Find Previous", "Go To Definition"], Texts(Rank(provider, "fi", items)));
		Assert.AreSequenceEqual(["Format Document"], Texts(Rank(provider, "rmat", items)));
		Assert.AreSequenceEqual(["Go To Definition"], Texts(Rank(provider, "go to def", items)));
		Assert.AreSequenceEqual([], Texts(Rank(provider, "zzz", items)));
	}

	[TestMethod]
	public void Rank_WordsMode_HighlightsTheLabelWordsAndTheDescriptionSubstring()
	{
		var provider = new ScriptedQuickPickProvider { FilterMode = QuickPickFilterMode.Words };

		QuickPickItem[] items = [new("a", "Toggle Line Comment"), new("b", "Open") { Description = "Category" }];

		IReadOnlyList<QuickPickRow> rows = Rank(provider, "tlc", items);

		Assert.AreSequenceEqual([new MatchRange(0, 1), new MatchRange(7, 1), new MatchRange(12, 1)], [.. rows[0].Item!.LabelHighlights]);

		rows = Rank(provider, "gor", items);

		Assert.AreSequenceEqual(["Open"], Texts(rows));
		Assert.AreSequenceEqual([new MatchRange(4, 3)], [.. rows[0].Item!.DescriptionHighlights]);
	}

	[TestMethod]
	public void Rank_WordsMode_KeepsTheRecentItemsFirstWhileTyping()
	{
		var provider = new ScriptedQuickPickProvider { FilterMode = QuickPickFilterMode.Words, RecentGroup = s_recentGroup };
		QuickPickItem[] items = [.. new[] { "Save", "Save All", "Close", "Revert", "Save As" }.Select(label => new QuickPickItem(label, label))];

		IReadOnlyList<QuickPickRow> rows = Rank(provider, query: "", items, recentKeys: ["Revert", "Save As"]);

		Assert.AreSequenceEqual(["--recently used--", "Revert", "Save As", "Save", "Save All", "Close"], Texts(rows));

		rows = Rank(provider, query: "sa", items, recentKeys: ["Revert", "Save As"]);

		// Only the recent item that matches the query stays in the group; the others follow in the order of the provider.
		Assert.AreSequenceEqual(["--recently used--", "Save As", "Save", "Save All"], Texts(rows));
	}

	[TestMethod]
	public void Rank_WordsMode_EveryWordMatchesWhereverItStandsAndInAnyOrder()
	{
		var provider = new ScriptedQuickPickProvider { FilterMode = QuickPickFilterMode.Words };
		QuickPickItem[] items = Items("Format Document", "Close Tab", "Find in Files", "Go To Definition");

		Assert.AreSequenceEqual(["Format Document"], Texts(Rank(provider, "format doc", items)));
		Assert.AreSequenceEqual(["Format Document"], Texts(Rank(provider, "doc format", items)));
		Assert.AreSequenceEqual(["Find in Files"], Texts(Rank(provider, "files find", items)));
		Assert.AreSequenceEqual(["Go To Definition"], Texts(Rank(provider, "def go", items)));
		Assert.AreSequenceEqual([], Texts(Rank(provider, "doc zzz", items)));
		Assert.AreSequenceEqual(["Format Document"], Texts(Rank(provider, "  doc   format  ", items)));
	}

	[TestMethod]
	public void Rank_WordsMode_HighlightsEachWordAndJoinsTheOnesThatTouchOrOverlap()
	{
		var provider = new ScriptedQuickPickProvider { FilterMode = QuickPickFilterMode.Words };
		QuickPickItem[] items = [new("a", "Format Document") { Description = "Edit" }];

		IReadOnlyList<QuickPickRow> rows = Rank(provider, "doc format", items);

		Assert.AreSequenceEqual([new MatchRange(0, 6), new MatchRange(7, 3)], [.. rows[0].Item!.LabelHighlights]);

		rows = Rank(provider, "for form", items);

		Assert.AreSequenceEqual([new MatchRange(0, 4)], [.. rows[0].Item!.LabelHighlights]);

		rows = Rank(provider, "edit format", items);

		Assert.AreSequenceEqual([new MatchRange(0, 6)], [.. rows[0].Item!.LabelHighlights]);
		Assert.AreSequenceEqual([new MatchRange(0, 4)], [.. rows[0].Item!.DescriptionHighlights]);
	}

	[TestMethod]
	public void Rank_RankOnlyMode_KeepsEveryItemAndListsTheMatchesFirst()
	{
		var provider = new ScriptedQuickPickProvider { FilterMode = QuickPickFilterMode.RankOnly };

		IReadOnlyList<QuickPickRow> rows = Rank(provider, "alp", Items("xx", "alpha", "beta", "alps"));

		Assert.AreSequenceEqual(["alps", "alpha", "xx", "beta"], Texts(rows));
		Assert.IsEmpty(rows[2].Item!.LabelHighlights);
	}

	[TestMethod]
	public void Rank_RankOnlyMode_EmptyQuery_KeepsTheOrderOfTheProvider()
	{
		var provider = new ScriptedQuickPickProvider { FilterMode = QuickPickFilterMode.RankOnly };
		Assert.AreSequenceEqual(["b", "a"], Texts(Rank(provider, "", Items("b", "a"))));
	}

	[TestMethod]
	public void Rank_GroupOrder_ListsTheGroupsByOrderWhateverThePositionOfTheirItems()
	{
		var provider = new ScriptedQuickPickProvider();
		var later = new QuickPickGroup("later", "Later", order: 2);
		var first = new QuickPickGroup("first", "First", order: 1);

		QuickPickItem[] items = [new("a", "alpha") { Group = later }, new("b", "alphabet") { Group = first }, new("c", "alp") { Group = later }];

		// Under a query, the better score of the item in the later group does not move its group up.
		Assert.AreSequenceEqual(["--First--", "alphabet", "--Later--", "alp", "alpha"], Texts(Rank(provider, "alp", items)));
		Assert.AreSequenceEqual(["--First--", "alphabet", "--Later--", "alpha", "alp"], Texts(Rank(provider, "", items)));
	}

	[TestMethod]
	public void Rank_GroupsOfEqualOrder_FollowTheirFirstItem()
	{
		var provider = new ScriptedQuickPickProvider();
		var one = new QuickPickGroup("one", "One");
		var two = new QuickPickGroup("two", "Two");

		QuickPickItem[] items = [new("a", "a") { Group = two }, new("b", "b") { Group = one }, new("c", "c") { Group = two }];

		Assert.AreSequenceEqual(["--Two--", "a", "c", "--One--", "b"], Texts(Rank(provider, "", items)));
	}

	[TestMethod]
	public void Rank_CanceledToken_Throws()
	{
		var provider = new ScriptedQuickPickProvider();
		using var cancellation = new CancellationTokenSource();

		cancellation.Cancel();

		Assert.ThrowsExactly<OperationCanceledException>(() =>
			QuickPickRanker.Rank(
				filterMode: provider.FilterMode,
				recentGroup: null,
				items: Items("a", "b"),
				query: "a",
				recentKeys: [],
				maxItemCount: 100,
				cancellationToken: cancellation.Token
			)
		);
	}

	[TestMethod]
	public void Rank_AQueryThatNoItemHolds_RejectsEachItemBeforeBuildingTheMatrix()
	{
		var provider = new ScriptedQuickPickProvider();

		QuickPickItem[] items = [.. Enumerable.Range(0, 5).Select(index => new QuickPickItem($"k{index}", new string('x', 2000) + index))];
		string query = new('q', 1500);

		long before = GC.GetAllocatedBytesForCurrentThread();

		Assert.IsEmpty(Rank(provider, query, items));

		// A matrix of this size would take megabytes, so a small figure shows the single pass over the text rejected the item first.
		Assert.IsLessThan(1_000_000L, GC.GetAllocatedBytesForCurrentThread() - before);
	}
}
