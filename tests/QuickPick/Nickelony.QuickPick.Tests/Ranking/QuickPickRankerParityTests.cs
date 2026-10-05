namespace Nickelony.QuickPick.Tests;

/// <summary>
/// Compares the order of the ranker with a transcription of the fuzzy scorer of VS Code over a corpus of commands, symbols and file paths,
/// so a change to the ranking cannot drift from the reference unnoticed.
/// </summary>
[TestClass]
public sealed class QuickPickRankerParityTests
{
	private static readonly string[] s_commands =
	[
		"New file...|File",
		"New folder...|File",
		"Save|File",
		"Save all|File",
		"Revert|File",
		"Close tab|File",
		"Complete word|Edit",
		"Quick fix|Edit",
		"Toggle line comment|Edit",
		"Trim trailing whitespace|Edit",
		"Format document|Edit",
		"Rename symbol...|Edit",
		"Toggle bookmark|Edit",
		"Find and replace...|Search",
		"Find next|Search",
		"Find previous|Search",
		"Find all|Search",
		"Find in files|Search",
		"Replace all|Search",
		"Go to definition|Navigate",
		"Find all references|Navigate",
		"Next problem|Navigate",
		"Split editor|View",
		"Open preview|View",
		"Toggle theme|View",
		"Command palette...|View",
		"Key bindings...|View",
		"Start the Lua language server...|Language",
		"Use the in-process Lua providers|Language",
		"Stop the Roslyn language server|Language",
		"Run command|Run",
		"Convert indentation to spaces|Edit",
		"Convert indentation to tabs|Edit",
		"Reindent spaces to the indent size|Edit",
	];

	private static readonly string[] s_symbols =
	[
		"AbortReason",
		"ActionSubscription",
		"ActiveDocumentContext",
		"AdjacentBookmarkLineResolver",
		"CompletionController",
		"CompletionItemAdapter",
		"CompletionWindowPresenter",
		"DiagnosticsController",
		"DocumentSymbolProvider",
		"DBNull",
		"FuzzyMatch",
		"FuzzyMatcher",
		"FuzzyMatchMode",
		"HoverController",
		"HtmlParser",
		"HTMLParser",
		"HttpResponse",
		"IOException",
		"IQuickPickProvider",
		"IQuickPickRecentStore",
		"IUiDispatcher",
		"IUiDispatcherFactory",
		"KeyBindingScope",
		"LanguageServerClient",
		"LatestRequestGate",
		"LineTerminators",
		"LspClient",
		"QuickPickController",
		"QuickPickControllerOptions",
		"QuickPickGroup",
		"QuickPickItem",
		"QuickPickPresenter",
		"QuickPickPresentationState",
		"QuickPickRanker",
		"QuickPickRow",
		"RequestLifetime",
		"SignatureHelpController",
		"TextEditorEditApplier",
		"UIElement",
		"WorkspaceDocumentStore",
		"WorkspaceDocumentTracker",
		"XMLHttpRequest",
		"XmlHttpRequest",
	];

	private static readonly string[] s_files =
	[
		"AGENTS.md",
		"README.md",
		"Directory.Build.props",
		"Directory.Build.targets",
		"docs/ArchitectureGuide.md",
		"docs/CoreGuide.md",
		"docs/QuickPickGuide.md",
		"docs/index.md",
		"samples/README.md",
		"samples/Nickelony.Samples.Wpf/SampleWindow.cs",
		"samples/Nickelony.Samples.Wpf/Nickelony.Samples.Wpf.csproj",
		"samples/Nickelony.Samples.Avalonia/SampleWindow.cs",
		"samples/Nickelony.Samples.Avalonia/Nickelony.Samples.Avalonia.csproj",
		"shared/QuickPick/QuickPickPresenter.cs",
		"shared/QuickPick/QuickPickPresenterHooks.cs",
		"shared/QuickPick/QuickPickRowEmitter.cs",
		"shared/QuickPick/QuickPickTheme.cs",
		"shared/Editor.LanguageFeatures/Completion/CompletionWindowPresenter.cs",
		"shared/Editor.LanguageFeatures/Hover/HoverControllerFactory.cs",
		"shared/docs/QuickPickSeam.xml",
		"shared/Samples/QuickPick/CommandQuickPickProvider.cs",
		"shared/Samples/QuickPick/SampleQuickPick.cs",
		"src/QuickPick/Nickelony.QuickPick.Wpf/QuickPickSeam.cs",
		"src/QuickPick/Nickelony.QuickPick.Avalonia/QuickPickSeam.cs",
		"src/IDEKit/Nickelony.IDEKit.Core/README.md",
		"src/Matching/Nickelony.Matching/FuzzyMatcher.cs",
		"src/Matching/Nickelony.Matching/FuzzyMatch.cs",
		"src/QuickPick/Nickelony.QuickPick/QuickPickController.cs",
		"src/QuickPick/Nickelony.QuickPick/Ranking/QuickPickRanker.cs",
		"src/QuickPick/Nickelony.QuickPick/IQuickPickProvider.cs",
		"src/IDEKit/Nickelony.IDEKit.Core/Nickelony.IDEKit.Core.csproj",
		"src/IDEKit/Nickelony.IDEKit.LanguageFeatures.Controllers/README.md",
		"src/IDEKit/Nickelony.IDEKit.LanguageFeatures.Controllers/Hover/HoverController.cs",
		"src/KeyBindings/Nickelony.KeyBindings/README.md",
		"tests/QuickPick/Nickelony.QuickPick.Tests/QuickPickControllerTests.cs",
		"tests/QuickPick/Nickelony.QuickPick.Tests/Ranking/QuickPickRankerTests.cs",
		"tests/Matching/Nickelony.Matching.Tests/FuzzyMatcherTests.cs",
		"tests/IDEKit/Nickelony.IDEKit.Core.Tests/Nickelony.IDEKit.Core.Tests.csproj",
		"tests/Nickelony.Repository.Tests/README.md",
	];

	private static readonly string[] s_commandQueries =
	[
		"fmtdoc",
		"format doc",
		"doc format",
		"formatdoc",
		"togcom",
		"tlc",
		"find",
		"fr",
		"gtd",
		"goto def",
		"go def",
		"def go",
		"rename",
		"save",
		"sa",
		"kb",
		"key",
		"palette",
		"run",
		"edit format",
		"theme",
		"lua",
		"ref",
		"close",
		"*fmt",
		"\"fin\"",
		"\"nd\"",
		"ce…",
	];

	private static readonly string[] s_symbolQueries =
	[
		"qpp",
		"iqpp",
		"cp",
		"ip",
		"lsp",
		"lspc",
		"dsp",
		"xhr",
		"ioe",
		"uie",
		"htmlp",
		"hp",
		"lsc",
		"wds",
		"fmm",
		"qpc",
		"cwt",
		"tes",
		"kbs",
		"quick pick",
		"pick quick",
		"controller quick",
		"\"Pick\"",
	];

	private static readonly string[] s_fileQueries =
	[
		"QuickPickController",
		"qpc",
		"quickpick controller",
		"controller quickpick",
		"fuzzymatcher",
		"fzm",
		"src/core/readme",
		"core readme",
		"readme core",
		"readme",
		"agents",
		"QuickPick/",
		"tests quickpick",
		"*.md",
		"Seam.cs",
		"qps",
		"arch",
		"conv",
		"csproj",
		"Directory.Build",
		"controllers/qp",
		"fmtests",
		"\"Seam\"",
		"sample window",
		"src/QuickPick/Nickelony.QuickPick/QuickPickController.cs",
		"SRC\\QUICKPICK\\NICKELONY.QUICKPICK\\RANKING\\QUICKPICKRANKER.CS",
	];

	private static List<FuzzyScorerTestHelpers.Item> Commands()
	{
		return
		[
			.. s_commands
				.Select(line => line.Split('|'))
				.Select((parts, index) => new FuzzyScorerTestHelpers.Item(Label: parts[0], Description: parts[1], Path: null, index)),
		];
	}

	private static List<FuzzyScorerTestHelpers.Item> Symbols() =>
		[.. s_symbols.Select((symbol, index) => new FuzzyScorerTestHelpers.Item(Label: symbol, Description: null, Path: null, index))];

	private static List<FuzzyScorerTestHelpers.Item> Files()
	{
		return
		[
			.. s_files.Select(
				(path, index) =>
					new FuzzyScorerTestHelpers.Item(
						Label: path[(path.LastIndexOf('/') + 1)..],
						Description: path.Contains('/') ? path[..path.LastIndexOf('/')] : string.Empty,
						path,
						index
					)
			),
		];
	}

	private static string[] Orders(QuickPickFilterMode mode, IReadOnlyList<FuzzyScorerTestHelpers.Item> corpus, string query)
	{
		QuickPickItem[] items =
		[
			.. corpus.Select(item => new QuickPickItem(item.Index.ToString(), item.Label)
			{
				Description = string.IsNullOrEmpty(item.Description) ? null : item.Description,
			}),
		];

		IReadOnlyList<QuickPickRow> rows = QuickPickRanker.Rank(
			filterMode: mode,
			recentGroup: null,
			items,
			query,
			recentKeys: [],
			maxItemCount: 100_000
		);

		return
		[
			.. rows.Where(row => row.Item is not null)
				.Select(row => corpus[int.Parse(row.Item!.Key)].Label + "|" + corpus[int.Parse(row.Item!.Key)].Description),
		];
	}

	private static string[] Reference(IReadOnlyList<FuzzyScorerTestHelpers.Item> corpus, string query) =>
		[.. FuzzyScorerTestHelpers.Rank(corpus, query).Select(item => item.Label + "|" + item.Description)];

	private static void AssertSameOrder(QuickPickFilterMode mode, IReadOnlyList<FuzzyScorerTestHelpers.Item> corpus, IEnumerable<string> queries)
	{
		var differences = new List<string>();

		foreach (string query in queries)
		{
			string[] expected = Reference(corpus, query);
			string[] actual = Orders(mode, corpus, query);

			if (!expected.SequenceEqual(actual))
				differences.Add($"'{query}': reference [{string.Join("; ", expected.Take(4))}] but ranker [{string.Join("; ", actual.Take(4))}]");
		}

		Assert.IsEmpty(differences, string.Join(Environment.NewLine, differences));
	}

	[TestMethod]
	public void Rank_CommandQueries_OrderAsTheReferenceDoes() => AssertSameOrder(QuickPickFilterMode.Fuzzy, Commands(), s_commandQueries);

	[TestMethod]
	public void Rank_SymbolQueries_OrderAsTheReferenceDoes() => AssertSameOrder(QuickPickFilterMode.Fuzzy, Symbols(), s_symbolQueries);

	[TestMethod]
	public void Rank_FileQueries_OrderAsTheReferenceDoes() => AssertSameOrder(QuickPickFilterMode.Path, Files(), s_fileQueries);

	[TestMethod]
	public void Rank_FilterModeNone_KeepsTheOrderOfTheProvider()
	{
		IReadOnlyList<FuzzyScorerTestHelpers.Item> corpus = Commands();

		string[] actual = Orders(QuickPickFilterMode.None, corpus, "zzz");

		Assert.AreSequenceEqual([.. corpus.Select(item => item.Label + "|" + item.Description)], actual);
	}
}
