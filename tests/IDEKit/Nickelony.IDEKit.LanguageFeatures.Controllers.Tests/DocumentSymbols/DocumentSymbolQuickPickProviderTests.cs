using Nickelony.IDEKit.Core.Navigation;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Controllers.DocumentSymbols;
using Nickelony.IDEKit.LanguageFeatures.DocumentSymbols;
using Nickelony.QuickPick;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Tests;

[TestClass]
public sealed class DocumentSymbolQuickPickProviderTests
{
	private const string DocumentId = "doc.lua";
	private const string Text = "class Alpha\n  method One\n  method Two\nfunction Beta\nclass Gamma\n";

	private static TextRange Of(string token) => new(Text.IndexOf(token, StringComparison.Ordinal), token.Length);

	private static DocumentSymbol[] Outline()
	{
		return
		[
			new(name: "Alpha", kind: DocumentSymbolKind.Class, range: Of("class Alpha\n  method One\n  method Two"), selectionRange: Of("Alpha"))
			{
				Children =
				[
					new("One", DocumentSymbolKind.Method, selectionRange: Of("One")),
					new("Two", DocumentSymbolKind.Method, selectionRange: Of("Two")),
				],
			},

			new("Beta", DocumentSymbolKind.Function, selectionRange: Of("Beta")),
			new("Gamma", DocumentSymbolKind.Class, selectionRange: Of("Gamma")),
		];
	}

	private static FakeEditorNavigationTarget Target() => new(11, 12, 12, 13, 11);

	private static DocumentSymbolQuickPickProvider ProviderOver(
		RecordingDocumentSymbolProvider outline,
		FakeEditorNavigationTarget target,
		DocumentSymbolQuickPickOptions? options = null,
		Func<string>? getText = null,
		Action<Exception>? reportFault = null
	) =>
		new(() => new SymbolDocumentDescriptor(provider: outline, DocumentId, documentText: getText?.Invoke() ?? Text, target), options, reportFault);

	private static Task<IReadOnlyList<QuickPickItem>> ListAsync(
		DocumentSymbolQuickPickProvider provider,
		string query = "",
		CancellationToken cancellationToken = default
	) => provider.GetItemsAsync(new QuickPickRequest(query, []), cancellationToken);

	private static string[] Labels(IReadOnlyList<QuickPickItem> items) => [.. items.Select(item => item.Label)];

	[TestMethod]
	public async Task GetItemsAsync_FlattensTheOutlineInDocumentOrderWithTheContainerAsTheDescription()
	{
		DocumentSymbolQuickPickProvider provider = ProviderOver(new RecordingDocumentSymbolProvider(Outline()), Target());

		IReadOnlyList<QuickPickItem> items = await ListAsync(provider, cancellationToken: TestContext.CancellationToken);

		Assert.AreSequenceEqual(["Alpha", "One", "Two", "Beta", "Gamma"], Labels(items));
		Assert.AreSequenceEqual([null, "Alpha", "Alpha", null, null], [.. items.Select(item => item.Description)]);
		Assert.AreSequenceEqual(["0", "1", "2", "3", "4"], [.. items.Select(item => item.Key)]);
		Assert.IsTrue(items.All(item => item.IsEnabled));
	}

	[TestMethod]
	public async Task GetItemsAsync_PutsTheKindAsTheIconKey()
	{
		DocumentSymbolQuickPickProvider provider = ProviderOver(new RecordingDocumentSymbolProvider(Outline()), Target());

		IReadOnlyList<QuickPickItem> items = await ListAsync(provider, cancellationToken: TestContext.CancellationToken);

		Assert.AreSequenceEqual(
			["symbol-class", "symbol-method", "symbol-method", "symbol-function", "symbol-class"],
			items.Select(item => item.IconKey).ToArray()
		);
	}

	[TestMethod]
	public async Task GetItemsAsync_ListsTheSymbolsUnderOneSeparatorThatCountsThem()
	{
		DocumentSymbolQuickPickProvider provider = ProviderOver(new RecordingDocumentSymbolProvider(Outline()), Target());

		IReadOnlyList<QuickPickItem> items = await ListAsync(provider, cancellationToken: TestContext.CancellationToken);

		Assert.HasCount(1, items.Select(item => item.Group).Distinct());
		Assert.AreEqual("symbols", items[0].Group!.Label);
		Assert.IsTrue(items[0].Group!.ShowsCount);
	}

	[TestMethod]
	public async Task GetItemsAsync_RequestsTheOutlineWithTheDocumentTextAndNoFilter()
	{
		var outline = new RecordingDocumentSymbolProvider(Outline());
		DocumentSymbolQuickPickProvider provider = ProviderOver(outline, Target());

		await ListAsync(provider, "alp", TestContext.CancellationToken);

		Assert.HasCount(1, outline.Requests);
		Assert.AreEqual(DocumentId, outline.Requests[0].DocumentId);
		Assert.AreEqual(Text, outline.Requests[0].DocumentText);
		Assert.AreEqual(string.Empty, outline.Requests[0].FilterText);
	}

	[TestMethod]
	public void GetItemsAsync_RequestsTheOutlineOnceForTheKeystrokesOfASession()
	{
		var outline = new RecordingDocumentSymbolProvider(Outline());
		using var host = new QuickPickTestHost(ProviderOver(outline, Target()));

		host.Open("@");

		host.Type("@g");
		host.Type("@ga");
		host.Type("@gam");

		Assert.AreEqual(1, outline.CallCount);
		Assert.AreSequenceEqual(["Gamma"], [.. host.Labels]);
	}

	[TestMethod]
	public async Task GetItemsAsync_ReusesTheOutlineForAnotherEditorOfTheSameDocumentAndPointsTheItemsAtThatEditor()
	{
		FakeEditorNavigationTarget first = Target();
		FakeEditorNavigationTarget second = Target();

		IEditorNavigationTarget current = first;

		var outline = new RecordingDocumentSymbolProvider(Outline());

		var provider = new DocumentSymbolQuickPickProvider(() =>
			new SymbolDocumentDescriptor(provider: outline, DocumentId, documentText: Text, target: current)
		);

		await ListAsync(provider, cancellationToken: TestContext.CancellationToken);
		current = second;
		IReadOnlyList<QuickPickItem> items = await ListAsync(provider, cancellationToken: TestContext.CancellationToken);
		provider.Preview(items[0]);

		Assert.AreEqual(1, outline.CallCount);
		Assert.IsEmpty(first.Log);
		Assert.AreSequenceEqual(["save", "select:0:6-0:11"], second.Log);
	}

	[TestMethod]
	public async Task GetItemsAsync_RequestsTheOutlineAgainForAnotherProviderOrDocument()
	{
		var first = new RecordingDocumentSymbolProvider(Outline());
		var second = new RecordingDocumentSymbolProvider(Outline());

		IDocumentSymbolProvider current = first;
		string documentId = DocumentId;
		FakeEditorNavigationTarget target = Target();

		var provider = new DocumentSymbolQuickPickProvider(() =>
			new SymbolDocumentDescriptor(provider: current, documentId, documentText: Text, target)
		);

		await ListAsync(provider, cancellationToken: TestContext.CancellationToken);
		current = second;
		await ListAsync(provider, cancellationToken: TestContext.CancellationToken);
		documentId = "other.lua";
		await ListAsync(provider, cancellationToken: TestContext.CancellationToken);

		Assert.AreEqual(1, first.CallCount);
		Assert.AreEqual(2, second.CallCount);
		Assert.AreEqual("other.lua", second.Requests[1].DocumentId);
	}

	[TestMethod]
	public async Task GetItemsAsync_RequestsTheOutlineAgainWhenTheTextChanged()
	{
		string text = Text;
		var outline = new RecordingDocumentSymbolProvider(Outline());
		DocumentSymbolQuickPickProvider provider = ProviderOver(outline, Target(), getText: () => text);

		await ListAsync(provider, cancellationToken: TestContext.CancellationToken);
		text += "-- edited\n";
		await ListAsync(provider, cancellationToken: TestContext.CancellationToken);

		Assert.AreEqual(2, outline.CallCount);
		Assert.AreEqual(text, outline.Requests[1].DocumentText);
	}

	[TestMethod]
	public async Task GetItemsAsync_DoesNotKeepAnAnswerWithoutSymbols()
	{
		var outline = new RecordingDocumentSymbolProvider();
		DocumentSymbolQuickPickProvider provider = ProviderOver(outline, Target());

		IReadOnlyList<QuickPickItem> first = await ListAsync(provider, cancellationToken: TestContext.CancellationToken);
		outline.Symbols = Outline();
		IReadOnlyList<QuickPickItem> second = await ListAsync(provider, cancellationToken: TestContext.CancellationToken);

		Assert.HasCount(1, first);
		Assert.IsFalse(first[0].IsEnabled);
		Assert.AreEqual("The editor has no symbols.", first[0].Label);
		Assert.AreEqual(2, outline.CallCount);
		Assert.HasCount(5, second);
	}

	[TestMethod]
	public async Task GetItemsAsync_AQueryThatStopsWaiting_LeavesTheSharedRequestRunningForTheNextQuery()
	{
		var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var outline = new RecordingDocumentSymbolProvider(Outline()) { Gate = gate };

		DocumentSymbolQuickPickProvider provider = ProviderOver(outline, Target());
		using var superseded = new CancellationTokenSource();

		Task<IReadOnlyList<QuickPickItem>> older = ListAsync(provider, "a", superseded.Token);

		await superseded.CancelAsync();
		await Assert.ThrowsAsync<OperationCanceledException>(() => older);

		Assert.IsFalse(outline.Tokens[0].IsCancellationRequested);

		Task<IReadOnlyList<QuickPickItem>> newer = ListAsync(provider, "g", TestContext.CancellationToken);

		gate.SetResult(true);

		Assert.AreEqual(1, outline.CallCount);
		Assert.AreSequenceEqual(["Gamma"], Labels(await newer));
	}

	[TestMethod]
	public void GetItemsAsync_ASupersededQueryDoesNotSendTheOutlineRequestAgain()
	{
		var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var outline = new RecordingDocumentSymbolProvider(Outline()) { Gate = gate };
		using var host = new QuickPickTestHost(ProviderOver(outline, Target()));

		host.Controller.Open("@");
		host.Controller.SetText("@be");
		host.PassDebounce();

		Assert.AreEqual(1, outline.CallCount);
		Assert.IsFalse(outline.Tokens[0].IsCancellationRequested);

		gate.SetResult(true);
		host.Dispatcher.PumpUntil(() => host.Controller.State.Rows.Count > 0);

		Assert.AreEqual(1, outline.CallCount);
		Assert.AreSequenceEqual(["Beta"], [.. host.Labels]);
	}

	[TestMethod]
	public void OnDeactivated_CancelsTheRequestInFlightAndAsksAgainInTheNextStay()
	{
		var outline = new RecordingDocumentSymbolProvider(Outline())
		{
			Gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously),
		};

		using var host = new QuickPickTestHost(ProviderOver(outline, Target()));

		host.Controller.Open("@");

		Assert.IsFalse(outline.Tokens[0].IsCancellationRequested);

		host.Controller.Dismiss();

		Assert.IsTrue(outline.Tokens[0].IsCancellationRequested);
		Assert.IsEmpty(host.Faults);

		outline.Gate = null;
		host.Open("@");

		Assert.AreEqual(2, outline.CallCount);
		Assert.HasCount(5, host.Labels);
	}

	[TestMethod]
	public void OnDeactivated_ReleasesTheOutlineSoTheNextSessionAsksAgain()
	{
		var outline = new RecordingDocumentSymbolProvider(Outline());
		using var host = new QuickPickTestHost(ProviderOver(outline, Target()));

		host.Open("@");
		host.Controller.Dismiss();
		host.Open("@");
		host.Accept();
		host.Open("@");

		Assert.AreEqual(3, outline.CallCount);
	}

	[TestMethod]
	public void OnDeactivated_WhenTheTextSwitchesToAnotherMode_ReleasesTheOutline()
	{
		var outline = new RecordingDocumentSymbolProvider(Outline());
		var other = new ScriptedModeProvider(">");
		using var host = new QuickPickTestHost(ProviderOver(outline, Target()), other);

		host.Open("@");

		host.Controller.SetText(">");
		host.Controller.SetText("@");

		host.PassDebounce();

		Assert.AreEqual(2, outline.CallCount);
	}

	[TestMethod]
	public void GetItemsAsync_AFailingOutlineRequest_ReachesReportFaultOnTheDispatcherAndIsAskedAgain()
	{
		var outline = new RecordingDocumentSymbolProvider(Outline()) { Failure = () => new InvalidOperationException("The outline failed.") };
		List<(Exception Exception, int ThreadId)> faults = [];

		using var host = new QuickPickTestHost(
			ProviderOver(outline, Target(), reportFault: exception => faults.Add((exception, Environment.CurrentManagedThreadId)))
		);

		host.Open("@");

		Assert.HasCount(1, faults);
		Assert.AreEqual("The outline failed.", faults[0].Exception.Message);
		Assert.AreEqual(Environment.CurrentManagedThreadId, faults[0].ThreadId);

		host.Type("@a");

		Assert.AreEqual(2, outline.CallCount);
		Assert.HasCount(2, faults);

		outline.Failure = null;
		host.Type("@al");

		Assert.AreEqual(3, outline.CallCount);
		Assert.IsNotEmpty(host.Labels);
	}

	[TestMethod]
	public async Task GetItemsAsync_SeveralQueriesThatWaitForOneFailingRequest_ReportItOnce()
	{
		var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		var outline = new RecordingDocumentSymbolProvider(Outline())
		{
			Gate = gate,
			Failure = () => new InvalidOperationException("The outline failed."),
		};

		List<Exception> faults = [];
		DocumentSymbolQuickPickProvider provider = ProviderOver(outline, Target(), reportFault: faults.Add);

		Task<IReadOnlyList<QuickPickItem>> first = ListAsync(provider, "a", TestContext.CancellationToken);
		Task<IReadOnlyList<QuickPickItem>> second = ListAsync(provider, "al", TestContext.CancellationToken);

		gate.SetResult(true);
		await Task.WhenAll(first, second);

		Assert.AreEqual(1, outline.CallCount);
		Assert.HasCount(1, faults);
	}

	[TestMethod]
	public void GetItemsAsync_AnOutlineRequestThatCancelsItself_IsAFailureAndNotTheEndOfTheStay()
	{
		var outline = new RecordingDocumentSymbolProvider(Outline()) { Failure = () => new OperationCanceledException("The request timed out.") };
		List<Exception> faults = [];
		using var host = new QuickPickTestHost(ProviderOver(outline, Target(), reportFault: faults.Add));

		host.Open("@");

		Assert.HasCount(1, faults);
		Assert.IsInstanceOfType<OperationCanceledException>(faults[0]);
	}

	[TestMethod]
	public async Task GetItemsAsync_WithoutReportFault_DropsTheFailureAndListsTheHint()
	{
		var outline = new RecordingDocumentSymbolProvider(Outline()) { Failure = () => new InvalidOperationException("The outline failed.") };
		DocumentSymbolQuickPickProvider provider = ProviderOver(outline, Target());

		IReadOnlyList<QuickPickItem> items = await ListAsync(provider, cancellationToken: TestContext.CancellationToken);

		Assert.AreEqual("The editor has no symbols.", items.Single().Label);
	}

	[TestMethod]
	public async Task GetItemsAsync_ListsASymbolWithoutARangeDimmed()
	{
		DocumentSymbolQuickPickProvider provider = ProviderOver(
			new RecordingDocumentSymbolProvider(
				new DocumentSymbol("Lost", DocumentSymbolKind.Variable),
				new DocumentSymbol("Beta", DocumentSymbolKind.Function, Of("Beta"))
			),

			Target()
		);

		IReadOnlyList<QuickPickItem> items = await ListAsync(provider, cancellationToken: TestContext.CancellationToken);

		Assert.IsFalse(items[0].IsEnabled);
		Assert.IsNull(items[0].Tag);
		Assert.IsTrue(items[1].IsEnabled);
	}

	[TestMethod]
	public async Task GetItemsAsync_WithoutADocument_ListsOneDimmedHint()
	{
		var provider = new DocumentSymbolQuickPickProvider(() => null);

		IReadOnlyList<QuickPickItem> items = await ListAsync(provider, cancellationToken: TestContext.CancellationToken);

		Assert.HasCount(1, items);
		Assert.IsFalse(items[0].IsEnabled);
	}

	[TestMethod]
	public async Task GetItemsAsync_FallsBackToTheFullRangeWhenTheProviderGaveNoNameRange()
	{
		FakeEditorNavigationTarget target = Target();

		DocumentSymbolQuickPickProvider provider = ProviderOver(
			new RecordingDocumentSymbolProvider(new DocumentSymbol("Beta", DocumentSymbolKind.Function, range: Of("function Beta"))),
			target
		);

		IReadOnlyList<QuickPickItem> items = await ListAsync(provider, cancellationToken: TestContext.CancellationToken);
		provider.Preview(items[0]);

		Assert.AreSequenceEqual(["save", "select:3:0-3:13"], target.Log);
	}

	[TestMethod]
	public async Task GetItemsAsync_TheFirstWordMustMatchTheNameAndNotTheContainer()
	{
		DocumentSymbolQuickPickProvider provider = ProviderOver(new RecordingDocumentSymbolProvider(Outline()), Target());

		IReadOnlyList<QuickPickItem> byContainer = await ListAsync(provider, "alp", TestContext.CancellationToken);
		IReadOnlyList<QuickPickItem> byName = await ListAsync(provider, "on", TestContext.CancellationToken);

		Assert.AreSequenceEqual(["Alpha"], Labels(byContainer));
		Assert.AreSequenceEqual(["One"], Labels(byName));
	}

	[TestMethod]
	public async Task GetItemsAsync_TheWordsAfterTheFirstMustMatchTheContainer()
	{
		DocumentSymbolQuickPickProvider provider = ProviderOver(new RecordingDocumentSymbolProvider(Outline()), Target());

		IReadOnlyList<QuickPickItem> inAlpha = await ListAsync(provider, "on alp", TestContext.CancellationToken);
		IReadOnlyList<QuickPickItem> inNothing = await ListAsync(provider, "on zzz", TestContext.CancellationToken);
		IReadOnlyList<QuickPickItem> noContainer = await ListAsync(provider, "alpha one", TestContext.CancellationToken);

		Assert.AreSequenceEqual(["One"], Labels(inAlpha));
		Assert.IsEmpty(inNothing);
		Assert.IsEmpty(noContainer);
	}

	[TestMethod]
	public async Task GetItemsAsync_ANameThatHoldsTheWholeQueryNeedsNoContainer()
	{
		DocumentSymbol[] symbols = [new("Alpha Beta", DocumentSymbolKind.Variable, Of("Beta"))];
		DocumentSymbolQuickPickProvider provider = ProviderOver(new RecordingDocumentSymbolProvider(symbols), Target());

		IReadOnlyList<QuickPickItem> items = await ListAsync(provider, "alpha beta", TestContext.CancellationToken);

		Assert.AreSequenceEqual(["Alpha Beta"], Labels(items));
	}

	[TestMethod]
	public async Task GetItemsAsync_HighlightsTheMatchedCharactersOfTheNameAndTheContainer()
	{
		DocumentSymbolQuickPickProvider provider = ProviderOver(new RecordingDocumentSymbolProvider(Outline()), Target());

		QuickPickItem item = (await ListAsync(provider, "on alp", TestContext.CancellationToken)).Single();

		Assert.AreEqual("One", item.Label);
		Assert.IsNotEmpty(item.LabelHighlights);
		Assert.AreEqual(0, item.LabelHighlights[0].Offset);
		Assert.IsNotEmpty(item.DescriptionHighlights);
		Assert.AreEqual(0, item.DescriptionHighlights[0].Offset);
	}

	[TestMethod]
	public async Task GetItemsAsync_ListsTheBestScoreFirstAndKeepsDocumentOrderForEqualScores()
	{
		DocumentSymbol[] symbols =
		[
			new("xxa", DocumentSymbolKind.Variable, Of("Alpha")),
			new("ab", DocumentSymbolKind.Variable, Of("Alpha")),
			new("yyb", DocumentSymbolKind.Variable, Of("Alpha")),
			new("ab", DocumentSymbolKind.Variable, Of("Alpha")),
		];

		DocumentSymbolQuickPickProvider provider = ProviderOver(new RecordingDocumentSymbolProvider(symbols), Target());

		IReadOnlyList<QuickPickItem> items = await ListAsync(provider, "a", TestContext.CancellationToken);

		Assert.AreSequenceEqual(["ab", "ab", "xxa"], Labels(items));
		Assert.AreSequenceEqual(["1", "3", "0"], [.. items.Select(item => item.Key)]);
	}

	[TestMethod]
	public void FilterMode_IsNoneBecauseTheProviderFiltersAndRanksItself()
	{
		IQuickPickProvider provider = ProviderOver(new RecordingDocumentSymbolProvider(), Target());
		Assert.AreEqual(QuickPickFilterMode.None, provider.FilterMode);
	}

	[TestMethod]
	public void Typing_RanksTheSymbolsByTheQueryAndSaysSoWhenNoneMatches()
	{
		using var host = new QuickPickTestHost(ProviderOver(new RecordingDocumentSymbolProvider(Outline()), Target()));

		host.Open("@");
		host.Type("@on");

		Assert.AreSequenceEqual(["One"], [.. host.Labels]);

		host.Type("@zzz");

		Assert.AreEqual("!No matching editor symbols", host.RowTexts.Single());
	}

	[TestMethod]
	public void GroupByKind_ListsASeparatorPerKindInAlphabeticalOrderWithTheNumberOfSymbols()
	{
		DocumentSymbolQuickPickProvider provider = ProviderOver(
			new RecordingDocumentSymbolProvider(Outline()),
			Target(),

			DocumentSymbolQuickPickOptions.Default with
			{
				GroupByKind = true,
			}
		);

		using var host = new QuickPickTestHost(provider);

		host.Open("@:");

		Assert.AreSequenceEqual(
			["--classes (2)--", "Alpha", "Gamma", "--functions (1)--", "Beta", "--methods (2)--", "One", "Two"],
			[.. host.RowTexts]
		);
	}

	[TestMethod]
	public void GroupByKind_OrdersTheSeparatorsByTheirText()
	{
		DocumentSymbol[] outline =
		[
			new("v", DocumentSymbolKind.Variable, Of("Alpha")),
			new("t", DocumentSymbolKind.TypeParameter, Of("Alpha")),
			new("m", DocumentSymbolKind.EnumMember, Of("Alpha")),
			new("e", DocumentSymbolKind.Enum, Of("Alpha")),
			new("a", DocumentSymbolKind.Array, Of("Alpha")),
		];

		DocumentSymbolQuickPickProvider provider = ProviderOver(
			new RecordingDocumentSymbolProvider(outline),
			Target(),

			DocumentSymbolQuickPickOptions.Default with
			{
				GroupByKind = true,
			}
		);

		using var host = new QuickPickTestHost(provider);

		host.Open("@:");

		Assert.AreSequenceEqual(
			[
				"--arrays (1)--",
				"a",
				"--enumeration members (1)--",
				"m",
				"--enumerations (1)--",
				"e",
				"--type parameters (1)--",
				"t",
				"--variables (1)--",
				"v",
			],

			[.. host.RowTexts]
		);
	}

	[TestMethod]
	public void GroupByKind_UsesTheLabelsOfTheOptions()
	{
		DocumentSymbolQuickPickOptions options = DocumentSymbolQuickPickOptions.Default with
		{
			GroupByKind = true,
			GroupLabel = kind => kind == DocumentSymbolKind.Class ? "Typen" : null,
		};

		DocumentSymbolQuickPickProvider provider = ProviderOver(new RecordingDocumentSymbolProvider(Outline()), Target(), options);
		using var host = new QuickPickTestHost(provider);

		host.Open("@:");

		Assert.AreEqual("--Typen (2)--", host.RowTexts.First(text => text.StartsWith("--", StringComparison.Ordinal)));
	}

	[TestMethod]
	public void GroupByKind_KeepsTheRankOfTheQueryInsideEachGroup()
	{
		DocumentSymbol[] symbols =
		[
			new("xa", DocumentSymbolKind.Method, Of("Alpha")),
			new("ab", DocumentSymbolKind.Method, Of("Alpha")),
			new("a", DocumentSymbolKind.Class, Of("Alpha")),
		];

		DocumentSymbolQuickPickProvider provider = ProviderOver(
			new RecordingDocumentSymbolProvider(symbols),
			Target(),

			DocumentSymbolQuickPickOptions.Default with
			{
				GroupByKind = true,
			}
		);

		using var host = new QuickPickTestHost(provider);

		host.Open("@:");
		host.Type("@:a");

		Assert.AreSequenceEqual(["--classes (1)--", "a", "--methods (2)--", "ab", "xa"], [.. host.RowTexts]);
	}

	[TestMethod]
	public void Modes_AnswerTheirOwnPrefixAndName()
	{
		DocumentSymbolQuickPickProvider plain = ProviderOver(new RecordingDocumentSymbolProvider(), Target());

		DocumentSymbolQuickPickProvider grouped = ProviderOver(
			new RecordingDocumentSymbolProvider(),
			Target(),

			DocumentSymbolQuickPickOptions.Default with
			{
				GroupByKind = true,
			}
		);

		Assert.AreEqual("@", plain.Prefix);
		Assert.AreEqual("@:", grouped.Prefix);
		Assert.AreEqual("Go to symbol in editor", plain.Name);
		Assert.AreEqual("Go to symbol in editor by category", grouped.Name);
	}

	[TestMethod]
	public async Task Texts_FollowTheOptions()
	{
		DocumentSymbolQuickPickOptions options = DocumentSymbolQuickPickOptions.Default with
		{
			Name = "Symbole",
			GroupedName = "Symbole nach Art",
			Placeholder = "Name eingeben",
			EmptyText = "Keine Symbole",
			NoDocumentText = "Keine Datei",
			NoSymbolsText = "Leer",
		};

		var plain = new DocumentSymbolQuickPickProvider(() => null, options);
		var grouped = new DocumentSymbolQuickPickProvider(() => null, options with { GroupByKind = true });

		DocumentSymbolQuickPickProvider empty = ProviderOver(new RecordingDocumentSymbolProvider(), Target(), options);

		Assert.AreEqual("Symbole", plain.Name);
		Assert.AreEqual("Symbole nach Art", grouped.Name);
		Assert.AreEqual("Name eingeben", plain.Placeholder);
		Assert.AreEqual("Keine Symbole", plain.EmptyText);
		Assert.AreEqual("Keine Datei", (await ListAsync(plain, cancellationToken: TestContext.CancellationToken)).Single().Label);
		Assert.AreEqual("Leer", (await ListAsync(empty, cancellationToken: TestContext.CancellationToken)).Single().Label);
	}

	[TestMethod]
	public void Preview_SelectsTheNameOfTheSymbolAndSavesTheViewOnce()
	{
		FakeEditorNavigationTarget target = Target();
		using var host = new QuickPickTestHost(ProviderOver(new RecordingDocumentSymbolProvider(Outline()), target));

		host.Open("@");
		host.Controller.MoveActive(1);

		Assert.AreSequenceEqual(["save", "select:0:6-0:11", "select:1:9-1:12"], target.Log);
	}

	[TestMethod]
	public void Dismiss_RestoresTheViewOnce()
	{
		FakeEditorNavigationTarget target = Target();
		using var host = new QuickPickTestHost(ProviderOver(new RecordingDocumentSymbolProvider(Outline()), target));

		host.Open("@");
		host.Controller.MoveActive(1);

		host.Controller.Dismiss();
		host.Controller.Dismiss();

		Assert.AreSequenceEqual(["save", "select:0:6-0:11", "select:1:9-1:12", "restore"], target.Log);
	}

	[TestMethod]
	public void Accept_KeepsTheSelectionAndRestoresNothing()
	{
		FakeEditorNavigationTarget target = Target();
		using var host = new QuickPickTestHost(ProviderOver(new RecordingDocumentSymbolProvider(Outline()), target));

		host.Open("@");
		host.Controller.MoveActive(1);

		Assert.IsTrue(host.Accept());

		Assert.AreSequenceEqual(["save", "select:0:6-0:11", "select:1:9-1:12", "select:1:9-1:12"], target.Log);
		Assert.AreEqual(2, target.CaretLineNumber);
		Assert.AreEqual(13, target.CaretColumn);
	}

	[TestMethod]
	public void Accept_ForgetsThePreviewSoTheNextSessionSavesTheViewAgain()
	{
		FakeEditorNavigationTarget target = Target();
		using var host = new QuickPickTestHost(ProviderOver(new RecordingDocumentSymbolProvider(Outline()), target));

		host.Open("@");
		host.Accept();
		host.Open("@");
		host.Controller.Dismiss();

		Assert.AreEqual(2, target.Log.Count(entry => entry == "save"));
		Assert.ContainsSingle(entry => entry == "restore", target.Log);
		Assert.AreEqual("restore", target.Log[^1]);
	}

	[TestMethod]
	public async Task Preview_InAnotherEditor_RestoresTheFirstEditorBeforeItSavesTheSecond()
	{
		FakeEditorNavigationTarget first = Target();
		FakeEditorNavigationTarget second = Target();

		IEditorNavigationTarget current = first;

		var outline = new RecordingDocumentSymbolProvider(Outline());

		var provider = new DocumentSymbolQuickPickProvider(() =>
			new SymbolDocumentDescriptor(provider: outline, DocumentId, documentText: Text, target: current)
		);

		IReadOnlyList<QuickPickItem> fromFirst = await ListAsync(provider, cancellationToken: TestContext.CancellationToken);
		provider.Preview(fromFirst[0]);

		current = second;
		outline.Symbols = Outline();
		IReadOnlyList<QuickPickItem> fromSecond = await ListAsync(provider, cancellationToken: TestContext.CancellationToken);
		provider.Preview(fromSecond[1]);
		provider.CancelPreview();

		Assert.AreSequenceEqual(["save", "select:0:6-0:11", "restore"], first.Log);
		Assert.AreSequenceEqual(["save", "select:1:9-1:12", "restore"], second.Log);
	}

	[TestMethod]
	public async Task Preview_OfAnItemWithoutARange_ChangesNothing()
	{
		FakeEditorNavigationTarget target = Target();

		DocumentSymbolQuickPickProvider provider = ProviderOver(
			new RecordingDocumentSymbolProvider(new DocumentSymbol("Lost", DocumentSymbolKind.Variable)),
			target
		);

		IReadOnlyList<QuickPickItem> items = await ListAsync(provider, cancellationToken: TestContext.CancellationToken);
		provider.Preview(items[0]);
		provider.CancelPreview();

		Assert.IsEmpty(target.Log);
	}

	[TestMethod]
	public async Task Members_RejectNullArguments()
	{
		DocumentSymbolQuickPickProvider provider = ProviderOver(new RecordingDocumentSymbolProvider(), Target());

		Assert.ThrowsExactly<ArgumentNullException>(() => new DocumentSymbolQuickPickProvider(null!));
		await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => provider.GetItemsAsync(null!, TestContext.CancellationToken));
		await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => provider.AcceptAsync(null!, TestContext.CancellationToken));
		Assert.ThrowsExactly<ArgumentNullException>(() => provider.Preview(null!));
	}

	[TestMethod]
	public void Options_RejectANullGroupLabelAndANullText()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => DocumentSymbolQuickPickOptions.Default with { GroupLabel = null! });
		Assert.ThrowsExactly<ArgumentNullException>(() => DocumentSymbolQuickPickOptions.Default with { Name = null! });
		Assert.ThrowsExactly<ArgumentNullException>(() => DocumentSymbolQuickPickOptions.Default with { GroupedName = null! });
		Assert.ThrowsExactly<ArgumentNullException>(() => DocumentSymbolQuickPickOptions.Default with { Placeholder = null! });
		Assert.ThrowsExactly<ArgumentNullException>(() => DocumentSymbolQuickPickOptions.Default with { EmptyText = null! });
		Assert.ThrowsExactly<ArgumentNullException>(() => DocumentSymbolQuickPickOptions.Default with { NoDocumentText = null! });
		Assert.ThrowsExactly<ArgumentNullException>(() => DocumentSymbolQuickPickOptions.Default with { NoSymbolsText = null! });
	}

	public TestContext TestContext { get; set; } = null!;
}
