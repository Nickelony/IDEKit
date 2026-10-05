using Nickelony.IDEKit.Core.Paths;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Controllers.WorkspaceSymbols;
using Nickelony.IDEKit.LanguageFeatures.DocumentSymbols;
using Nickelony.IDEKit.LanguageFeatures.WorkspaceSymbols;
using Nickelony.QuickPick;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Tests;

[TestClass]
public sealed class WorkspaceSymbolQuickPickProviderTests
{
	// The mode waits this long after typing before it asks the servers.
	private static readonly TimeSpan s_symbolRest = TimeSpan.FromMilliseconds(200);

	private static readonly string s_root = LocalPath.Normalize(Path.Combine(Path.GetTempPath(), "workspace-symbols"));

	private static string InRoot(params string[] segments) => Path.Combine([s_root, .. segments]);

	private static WorkspaceSymbol Symbol(string name, DocumentSymbolKind kind, string path, string? container = null, int? line = null)
	{
		return new(
			name,
			kind,

			new WorkspaceSymbolLocation(
				path,
				line is { } at ? new TextPositionRange(new TextPosition(at, 2), new TextPosition(at, 2 + name.Length)) : null
			)
		)
		{
			ContainerName = container,
		};
	}

	private static WorkspaceSymbolQuickPickProvider ProviderOver(
		IReadOnlyList<IWorkspaceSymbolProvider> providers,
		Func<WorkspaceSymbol, CancellationToken, Task>? openAsync = null,
		WorkspaceSymbolQuickPickOptions? options = null,
		Action<Exception>? reportFault = null
	)
	{
		return new(
			getProviders: () => providers,
			openAsync: openAsync ?? ((_, _) => Task.CompletedTask),
			options: options ?? WorkspaceSymbolQuickPickOptions.Default with { RootPaths = [s_root] },
			reportFault
		);
	}

	private static Func<WorkspaceSymbol, CancellationToken, Task> Collect(List<WorkspaceSymbol> opened)
	{
		return (symbol, _) =>
		{
			opened.Add(symbol);
			return Task.CompletedTask;
		};
	}

	private static Task<IReadOnlyList<QuickPickItem>> ListAsync(
		WorkspaceSymbolQuickPickProvider provider,
		string query,
		CancellationToken cancellationToken
	) => provider.GetItemsAsync(new QuickPickRequest(query, []), cancellationToken);

	private static string[] Labels(IReadOnlyList<QuickPickItem> items) => [.. items.Select(item => item.Label)];

	[TestMethod]
	public async Task GetItemsAsync_WithABlankQuery_SendsNoRequestAndListsNothing()
	{
		var server = new RecordingWorkspaceSymbolProvider(Symbol("Util", DocumentSymbolKind.Class, InRoot("util.lua")));
		WorkspaceSymbolQuickPickProvider provider = ProviderOver([server]);

		IReadOnlyList<QuickPickItem> items = await ListAsync(provider, "  ", TestContext.CancellationToken);

		Assert.IsEmpty(items);
		Assert.IsEmpty(server.Queries);
	}

	[TestMethod]
	public async Task GetItemsAsync_SendsOnlyTheFirstWordOfTheQueryToEveryProvider()
	{
		var first = new RecordingWorkspaceSymbolProvider();
		var second = new RecordingWorkspaceSymbolProvider();

		WorkspaceSymbolQuickPickProvider provider = ProviderOver([first, second]);

		await ListAsync(provider, " util core src ", TestContext.CancellationToken);

		Assert.AreSequenceEqual(["util"], [.. first.Queries]);
		Assert.AreSequenceEqual(["util"], [.. second.Queries]);
	}

	[TestMethod]
	public void GetFilterText_IsTheFirstWord()
	{
		IQuickPickProvider provider = ProviderOver([]);

		Assert.AreEqual("util", provider.GetFilterText(" util core "));
		Assert.AreEqual(string.Empty, provider.GetFilterText("  "));
		Assert.ThrowsExactly<ArgumentNullException>(() => provider.GetFilterText(null!));
	}

	[TestMethod]
	public async Task GetItemsAsync_TheWordsAfterTheFirstMustMatchTheContainerOrThePath()
	{
		var server = new RecordingWorkspaceSymbolProvider(
			Symbol(name: "Util", kind: DocumentSymbolKind.Class, path: InRoot("src", "util.lua"), container: "Core"),
			Symbol(name: "Utility", kind: DocumentSymbolKind.Class, path: InRoot("lib", "foo.lua"), container: "Other")
		);

		WorkspaceSymbolQuickPickProvider provider = ProviderOver([server]);

		IReadOnlyList<QuickPickItem> byContainer = await ListAsync(provider, "util core", TestContext.CancellationToken);
		IReadOnlyList<QuickPickItem> byPath = await ListAsync(provider, "util foo", TestContext.CancellationToken);
		IReadOnlyList<QuickPickItem> byNothing = await ListAsync(provider, "util zzz", TestContext.CancellationToken);
		IReadOnlyList<QuickPickItem> byFirstWord = await ListAsync(provider, "util", TestContext.CancellationToken);

		Assert.AreSequenceEqual(["Util"], Labels(byContainer));
		Assert.AreSequenceEqual(["Utility"], Labels(byPath));
		Assert.IsEmpty(byNothing);
		Assert.AreSequenceEqual(["Util", "Utility"], Labels(byFirstWord));
	}

	[TestMethod]
	public async Task GetItemsAsync_ANameThatHoldsTheWholeQueryNeedsNoContainer()
	{
		var server = new RecordingWorkspaceSymbolProvider(Symbol("Util Ity", DocumentSymbolKind.Class, InRoot("a.lua")));

		IReadOnlyList<QuickPickItem> items = await ListAsync(ProviderOver([server]), "util ity", TestContext.CancellationToken);

		Assert.AreSequenceEqual(["Util Ity"], Labels(items));
	}

	[TestMethod]
	public async Task GetItemsAsync_ShowsTheNameTheContainerAndThePathBelowTheRoot()
	{
		var server = new RecordingWorkspaceSymbolProvider(
			Symbol(name: "Util", kind: DocumentSymbolKind.Class, path: InRoot("src", "util.lua"), container: "Core", line: 4),
			Symbol("run", DocumentSymbolKind.Function, InRoot("main.lua"))
		);

		IReadOnlyList<QuickPickItem> items = await ListAsync(ProviderOver([server]), "u", TestContext.CancellationToken);

		Assert.AreEqual("Util", items[0].Label);
		Assert.AreEqual("Core • src/util.lua", items[0].Description);
		Assert.AreEqual("symbol-class", items[0].IconKey);
		Assert.AreEqual("run", items[1].Label);
		Assert.AreEqual("main.lua", items[1].Description);
		Assert.AreEqual("symbol-function", items[1].IconKey);
	}

	[TestMethod]
	public async Task GetItemsAsync_JoinsTheContainerAndThePathWithTheSeparatorOfTheOptions()
	{
		var server = new RecordingWorkspaceSymbolProvider(
			Symbol(name: "Util", kind: DocumentSymbolKind.Class, path: InRoot("util.lua"), container: "Core")
		);

		WorkspaceSymbolQuickPickOptions options = WorkspaceSymbolQuickPickOptions.Default with { RootPaths = [s_root], ContainerSeparator = " in " };

		IReadOnlyList<QuickPickItem> items = await ListAsync(ProviderOver([server], options: options), "u", TestContext.CancellationToken);

		Assert.AreEqual("Core in util.lua", items[0].Description);
	}

	[TestMethod]
	public async Task GetItemsAsync_WithSeveralRoots_PutsTheNameOfTheRootInFront()
	{
		string other = LocalPath.Normalize(Path.Combine(Path.GetTempPath(), "other-root"));
		var server = new RecordingWorkspaceSymbolProvider(Symbol("Util", DocumentSymbolKind.Class, Path.Combine(other, "lib", "util.lua")));
		WorkspaceSymbolQuickPickOptions options = WorkspaceSymbolQuickPickOptions.Default with { RootPaths = [s_root, other] };

		IReadOnlyList<QuickPickItem> items = await ListAsync(ProviderOver([server], options: options), "u", TestContext.CancellationToken);

		Assert.AreEqual("other-root/lib/util.lua", items[0].Description);
	}

	[TestMethod]
	public async Task GetItemsAsync_ForADocumentOutsideTheRoots_ShowsItsOwnFolder()
	{
		string elsewhere = LocalPath.Normalize(Path.Combine(path1: Path.GetTempPath(), path2: "elsewhere", path3: "lib", path4: "util.lua"));
		var server = new RecordingWorkspaceSymbolProvider(Symbol("Util", DocumentSymbolKind.Class, elsewhere));

		IReadOnlyList<QuickPickItem> items = await ListAsync(ProviderOver([server]), "u", TestContext.CancellationToken);

		Assert.AreEqual(elsewhere.Replace('\\', '/'), items[0].Description!.Replace('\\', '/'));
	}

	[TestMethod]
	public async Task GetItemsAsync_MergesTheSymbolsOfSeveralProvidersInProviderOrder()
	{
		var lua = new RecordingWorkspaceSymbolProvider(
			Symbol("fromLua", DocumentSymbolKind.Function, InRoot("a.lua")),
			Symbol("alsoLua", DocumentSymbolKind.Variable, InRoot("a.lua"))
		);

		var csharp = new RecordingWorkspaceSymbolProvider(Symbol("FromCSharp", DocumentSymbolKind.Class, InRoot("A.cs")));
		WorkspaceSymbolQuickPickProvider provider = ProviderOver([csharp, lua]);

		IReadOnlyList<QuickPickItem> items = await ListAsync(provider, "from", TestContext.CancellationToken);

		Assert.AreSequenceEqual(["FromCSharp", "fromLua", "alsoLua"], Labels(items));
	}

	[TestMethod]
	public async Task GetItemsAsync_ListsASymbolThatTwoProvidersListAlikeOnceUnderTheFirstProvider()
	{
		var first = new RecordingWorkspaceSymbolProvider(
			Symbol(name: "Util", kind: DocumentSymbolKind.Class, path: InRoot("util.lua"), container: "Core")
		);

		var second = new RecordingWorkspaceSymbolProvider(
			Symbol(name: "Util", kind: DocumentSymbolKind.Class, path: InRoot("util.lua"), container: "Core")
		);

		WorkspaceSymbolQuickPickProvider provider = ProviderOver([first, second]);

		IReadOnlyList<QuickPickItem> items = await ListAsync(provider, "u", TestContext.CancellationToken);
		await provider.AcceptAsync(new QuickPickAcceptRequest(items.Single(), "u"), TestContext.CancellationToken);

		Assert.HasCount(1, items);
		Assert.HasCount(1, first.ResolveCalls);
		Assert.IsEmpty(second.ResolveCalls);
	}

	[TestMethod]
	public async Task GetItemsAsync_ASymbolThatOneServerListsTwice_IsListedOnce()
	{
		var server = new RecordingWorkspaceSymbolProvider(
			Symbol(name: "helperRun", kind: DocumentSymbolKind.Variable, path: InRoot("main.lua"), line: 4),
			Symbol(name: "helperRun", kind: DocumentSymbolKind.Variable, path: InRoot("main.lua"), line: 4)
		);

		IReadOnlyList<QuickPickItem> items = await ListAsync(ProviderOver([server]), "helper", TestContext.CancellationToken);

		Assert.HasCount(1, items);
	}

	[TestMethod]
	public async Task GetItemsAsync_SymbolsThatDifferInTheStartTheKindTheContainerOrTheDocument_AreNotDuplicates()
	{
		var server = new RecordingWorkspaceSymbolProvider(
			Symbol(name: "Util", kind: DocumentSymbolKind.Class, path: InRoot("util.lua"), line: 3),
			Symbol(name: "Util", kind: DocumentSymbolKind.Class, path: InRoot("util.lua"), line: 9),
			Symbol(name: "Util", kind: DocumentSymbolKind.Function, path: InRoot("util.lua"), line: 3),
			Symbol(name: "Util", kind: DocumentSymbolKind.Class, path: InRoot("util.lua"), container: "Core", line: 3),
			Symbol(name: "Util", kind: DocumentSymbolKind.Class, path: InRoot("other.lua"), line: 3)
		);

		IReadOnlyList<QuickPickItem> items = await ListAsync(ProviderOver([server]), "u", TestContext.CancellationToken);

		Assert.HasCount(5, items.Select(item => item.Key).Distinct());
	}

	[TestMethod]
	public async Task GetItemsAsync_AKeyStaysTheSameFromQueryToQuery()
	{
		var server = new RecordingWorkspaceSymbolProvider(Symbol(name: "Util", kind: DocumentSymbolKind.Class, path: InRoot("util.lua"), line: 3));
		WorkspaceSymbolQuickPickProvider provider = ProviderOver([server]);

		IReadOnlyList<QuickPickItem> first = await ListAsync(provider, "u", TestContext.CancellationToken);
		IReadOnlyList<QuickPickItem> second = await ListAsync(provider, "ut", TestContext.CancellationToken);

		Assert.AreEqual(first[0].Key, second[0].Key);
	}

	[TestMethod]
	public async Task GetItemsAsync_AProviderThatThrowsCostsOnlyItsOwnSymbols()
	{
		var broken = new RecordingWorkspaceSymbolProvider { Failure = new InvalidOperationException("The server fell over.") };
		var healthy = new RecordingWorkspaceSymbolProvider(Symbol("Util", DocumentSymbolKind.Class, InRoot("util.lua")));

		List<Exception> faults = [];

		IReadOnlyList<QuickPickItem> items = await ListAsync(
			ProviderOver([broken, healthy], reportFault: faults.Add),
			"u",
			TestContext.CancellationToken
		);

		Assert.AreSequenceEqual(["Util"], Labels(items));
		Assert.HasCount(1, faults);
		Assert.AreEqual("The server fell over.", faults[0].Message);
	}

	[TestMethod]
	public async Task GetItemsAsync_APrivateCancellationOfAProviderIsAFailureOfThatProviderOnly()
	{
		var timedOut = new RecordingWorkspaceSymbolProvider { Failure = new OperationCanceledException("The request timed out.") };
		var healthy = new RecordingWorkspaceSymbolProvider(Symbol("Util", DocumentSymbolKind.Class, InRoot("util.lua")));

		List<Exception> faults = [];

		IReadOnlyList<QuickPickItem> items = await ListAsync(
			ProviderOver([timedOut, healthy], reportFault: faults.Add),
			"u",
			TestContext.CancellationToken
		);

		Assert.HasCount(1, items);
		Assert.HasCount(1, faults);
	}

	[TestMethod]
	public void GetItemsAsync_TheFailuresOfOneListingReachReportFaultInProviderOrderOneAfterAnotherOnTheDispatcher()
	{
		// The first provider answers last, so a report in the order of the answers would put the second one first.
		var slowest = new RecordingWorkspaceSymbolProvider
		{
			Delay = TimeSpan.FromMilliseconds(120),
			Failure = new InvalidOperationException("first"),
		};

		var fastest = new RecordingWorkspaceSymbolProvider
		{
			Delay = TimeSpan.FromMilliseconds(5),
			Failure = new InvalidOperationException("second"),
		};

		var healthy = new RecordingWorkspaceSymbolProvider(Symbol("Util", DocumentSymbolKind.Class, InRoot("util.lua")))
		{
			Delay = TimeSpan.FromMilliseconds(30),
		};

		List<(string Message, int ThreadId, bool Overlapped)> faults = [];
		int running = 0;

		void Report(Exception exception)
		{
			bool overlapped = Interlocked.Increment(ref running) > 1;

			faults.Add((exception.Message, Environment.CurrentManagedThreadId, overlapped));
			Thread.Sleep(20);
			Interlocked.Decrement(ref running);
		}

		using var host = new QuickPickTestHost(ProviderOver([slowest, fastest, healthy], reportFault: Report));

		host.Controller.Open("#u");
		host.Dispatcher.PumpUntil(() => host.Controller.State.Rows.Count > 0);

		Assert.AreSequenceEqual(["first", "second"], [.. faults.Select(fault => fault.Message)]);
		Assert.IsTrue(faults.All(fault => fault.ThreadId == Environment.CurrentManagedThreadId));
		Assert.DoesNotContain(fault => fault.Overlapped, faults);
		Assert.AreSequenceEqual(["Util"], [.. host.Labels]);
	}

	[TestMethod]
	public async Task GetItemsAsync_CancelsTheRequestOfEveryProviderWithTheQuery()
	{
		var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var first = new RecordingWorkspaceSymbolProvider { Gate = gate };
		var second = new RecordingWorkspaceSymbolProvider { Gate = gate };
		using var query = new CancellationTokenSource();

		Task<IReadOnlyList<QuickPickItem>> listing = ListAsync(ProviderOver([first, second]), "u", query.Token);

		Assert.IsFalse(first.Tokens[0].IsCancellationRequested);

		await query.CancelAsync();

		Assert.IsTrue(first.Tokens[0].IsCancellationRequested);
		Assert.IsTrue(second.Tokens[0].IsCancellationRequested);
		await Assert.ThrowsAsync<OperationCanceledException>(() => listing);
	}

	[TestMethod]
	public void GetItemsAsync_ASupersededQueryCancelsItsRequestsAndOnlyTheNewestAnswerShows()
	{
		var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var server = new RecordingWorkspaceSymbolProvider(Symbol("Util", DocumentSymbolKind.Class, InRoot("util.lua"))) { Gate = gate };
		using var host = new QuickPickTestHost(ProviderOver([server]));

		host.Controller.Open("#ut");

		Assert.AreSequenceEqual(["ut"], [.. server.Queries]);

		host.Controller.SetText("#uti");

		Assert.IsTrue(server.Tokens[0].IsCancellationRequested);

		host.Dispatcher.Advance(TimeSpan.FromMilliseconds(200));
		gate.SetResult(true);
		host.Dispatcher.PumpUntil(() => host.Controller.State.Rows.Count > 0);

		Assert.AreSequenceEqual(["ut", "uti"], [.. server.Queries]);
		Assert.IsFalse(server.Tokens[1].IsCancellationRequested);
		Assert.AreSequenceEqual(["Util"], [.. host.Labels]);
	}

	[TestMethod]
	public void QueryDebounceDelay_IsTwoHundredMillisecondsAndTheTypedTextWaitsForIt()
	{
		var server = new RecordingWorkspaceSymbolProvider(Symbol("Util", DocumentSymbolKind.Class, InRoot("util.lua")));
		IQuickPickProvider mode = ProviderOver([server]);

		Assert.AreEqual(TimeSpan.FromMilliseconds(200), mode.QueryDebounceDelay);

		using var host = new QuickPickTestHost(ProviderOver([server]));

		host.Controller.Open("#u");
		host.Controller.SetText("#ut");
		host.Dispatcher.Advance(QuickPickControllerOptions.Default.QueryDebounceDelay);

		Assert.HasCount(1, server.Queries);

		host.Dispatcher.Advance(TimeSpan.FromMilliseconds(149));

		Assert.HasCount(1, server.Queries);

		host.Dispatcher.Advance(TimeSpan.FromMilliseconds(1));

		Assert.HasCount(2, server.Queries);
	}

	[TestMethod]
	public void QueryDebounceDelay_FollowsTheOptions()
	{
		IQuickPickProvider mode = ProviderOver(
			[],

			options: WorkspaceSymbolQuickPickOptions.Default with
			{
				QueryDebounceDelay = TimeSpan.FromMilliseconds(75),
			}
		);

		Assert.AreEqual(TimeSpan.FromMilliseconds(75), mode.QueryDebounceDelay);
	}

	[TestMethod]
	public async Task GetItemsAsync_AsksForTheProvidersAgainForEveryListing()
	{
		var first = new RecordingWorkspaceSymbolProvider(Symbol("One", DocumentSymbolKind.Class, InRoot("a.lua")));
		var second = new RecordingWorkspaceSymbolProvider(Symbol("Two", DocumentSymbolKind.Class, InRoot("b.lua")));

		IReadOnlyList<IWorkspaceSymbolProvider> running = [first];
		var provider = new WorkspaceSymbolQuickPickProvider(() => running, (_, _) => Task.CompletedTask);

		IReadOnlyList<QuickPickItem> before = await ListAsync(provider, "o", TestContext.CancellationToken);
		running = [first, second];
		IReadOnlyList<QuickPickItem> after = await ListAsync(provider, "o", TestContext.CancellationToken);

		Assert.AreSequenceEqual(["One"], Labels(before));
		Assert.AreSequenceEqual(["One", "Two"], Labels(after));
	}

	[TestMethod]
	public void RankOnly_RanksTheMatchesFirstAndKeepsEverySymbolTheServerListed()
	{
		var server = new RecordingWorkspaceSymbolProvider(
			Symbol("Zebra", DocumentSymbolKind.Class, InRoot("z.lua")),
			Symbol("Utility", DocumentSymbolKind.Class, InRoot("u.lua"))
		);

		using var host = new QuickPickTestHost(ProviderOver([server]));

		host.Controller.Open("#");

		Assert.IsEmpty(host.Labels);

		host.Type("#util", s_symbolRest);

		Assert.AreSequenceEqual(["Utility", "Zebra"], [.. host.Labels]);
	}

	[TestMethod]
	public void Mode_NamesItselfWithTheWorkspacePrefix()
	{
		IQuickPickProvider provider = ProviderOver([]);

		Assert.AreEqual("#", provider.Prefix);
		Assert.AreEqual(QuickPickFilterMode.RankOnly, provider.FilterMode);
		Assert.AreEqual("Go to symbol in workspace", provider.Name);
		Assert.AreEqual("No matching workspace symbols", provider.EmptyText);
	}

	[TestMethod]
	public void Texts_FollowTheOptions()
	{
		WorkspaceSymbolQuickPickOptions options = WorkspaceSymbolQuickPickOptions.Default with
		{
			Name = "Symbole",
			Placeholder = "Name eingeben",
			EmptyText = "Keine Symbole",
		};

		IQuickPickProvider provider = ProviderOver([], options: options);

		Assert.AreEqual("Symbole", provider.Name);
		Assert.AreEqual("Name eingeben", provider.Placeholder);
		Assert.AreEqual("Keine Symbole", provider.EmptyText);
	}

	[TestMethod]
	public async Task AcceptAsync_ResolvesASymbolWithoutARangeBeforeItOpens()
	{
		WorkspaceSymbol lost = Symbol("Util", DocumentSymbolKind.Class, InRoot("util.lua"));
		WorkspaceSymbol found = Symbol(name: "Util", kind: DocumentSymbolKind.Class, path: InRoot("util.lua"), line: 7);

		var server = new RecordingWorkspaceSymbolProvider(lost) { ResolvedSymbol = found };

		List<WorkspaceSymbol> opened = [];
		WorkspaceSymbolQuickPickProvider provider = ProviderOver([server], Collect(opened));

		IReadOnlyList<QuickPickItem> items = await ListAsync(provider, "u", TestContext.CancellationToken);
		await provider.AcceptAsync(new QuickPickAcceptRequest(items[0], "u"), TestContext.CancellationToken);

		Assert.HasCount(1, server.ResolveCalls);
		Assert.AreSame(lost, server.ResolveCalls[0]);
		Assert.HasCount(1, opened);
		Assert.AreSame(found, opened[0]);
	}

	[TestMethod]
	public async Task AcceptAsync_DoesNotResolveASymbolThatHasARange()
	{
		WorkspaceSymbol known = Symbol(name: "Util", kind: DocumentSymbolKind.Class, path: InRoot("util.lua"), line: 3);

		var server = new RecordingWorkspaceSymbolProvider(known)
		{
			ResolvedSymbol = Symbol(name: "Other", kind: DocumentSymbolKind.Class, path: InRoot("other.lua"), line: 1),
		};

		List<WorkspaceSymbol> opened = [];
		WorkspaceSymbolQuickPickProvider provider = ProviderOver([server], Collect(opened));

		IReadOnlyList<QuickPickItem> items = await ListAsync(provider, "u", TestContext.CancellationToken);
		await provider.AcceptAsync(new QuickPickAcceptRequest(items[0], "u"), TestContext.CancellationToken);

		Assert.IsEmpty(server.ResolveCalls);
		Assert.AreSame(known, opened.Single());
	}

	[TestMethod]
	public async Task AcceptAsync_ResolvesWithTheProviderThatListedTheSymbol()
	{
		WorkspaceSymbol fromFirst = Symbol("One", DocumentSymbolKind.Class, InRoot("a.lua"));
		WorkspaceSymbol fromSecond = Symbol("Two", DocumentSymbolKind.Class, InRoot("b.lua"));

		var first = new RecordingWorkspaceSymbolProvider(fromFirst);
		var second = new RecordingWorkspaceSymbolProvider(fromSecond);

		WorkspaceSymbolQuickPickProvider provider = ProviderOver([first, second]);

		IReadOnlyList<QuickPickItem> items = await ListAsync(provider, "o", TestContext.CancellationToken);
		await provider.AcceptAsync(new QuickPickAcceptRequest(items.Single(item => item.Label == "Two"), "o"), TestContext.CancellationToken);

		Assert.IsEmpty(first.ResolveCalls);
		Assert.AreSame(fromSecond, second.ResolveCalls.Single());
	}

	[TestMethod]
	public async Task AcceptAsync_OpensASymbolWithoutARangeAsItIsWhenTheProviderCannotResolve()
	{
		WorkspaceSymbol lost = Symbol("Util", DocumentSymbolKind.Class, InRoot("util.lua"));
		List<WorkspaceSymbol> opened = [];
		WorkspaceSymbolQuickPickProvider provider = ProviderOver([new StubWorkspaceSymbolProvider(lost)], Collect(opened));

		IReadOnlyList<QuickPickItem> items = await ListAsync(provider, "u", TestContext.CancellationToken);
		await provider.AcceptAsync(new QuickPickAcceptRequest(items[0], "u"), TestContext.CancellationToken);

		Assert.AreSame(lost, opened.Single());
	}

	[TestMethod]
	public async Task AcceptAsync_OpensTheSymbolAsItWasWhenTheResolutionThrows()
	{
		WorkspaceSymbol lost = Symbol("Util", DocumentSymbolKind.Class, InRoot("util.lua"));

		var server = new RecordingWorkspaceSymbolProvider(lost) { ResolveFailure = new InvalidOperationException("No answer.") };

		List<WorkspaceSymbol> opened = [];
		List<Exception> faults = [];
		WorkspaceSymbolQuickPickProvider provider = ProviderOver([server], Collect(opened), reportFault: faults.Add);

		IReadOnlyList<QuickPickItem> items = await ListAsync(provider, "u", TestContext.CancellationToken);
		await provider.AcceptAsync(new QuickPickAcceptRequest(items[0], "u"), TestContext.CancellationToken);

		Assert.AreSame(lost, opened.Single());
		Assert.HasCount(1, faults);
	}

	[TestMethod]
	public void AcceptAsync_AResolverThatYields_OpensTheSymbolOnTheDispatcherThread()
	{
		WorkspaceSymbol lost = Symbol("Util", DocumentSymbolKind.Class, InRoot("util.lua"));
		WorkspaceSymbol found = Symbol(name: "Util", kind: DocumentSymbolKind.Class, path: InRoot("util.lua"), line: 7);

		var server = new RecordingWorkspaceSymbolProvider(lost) { ResolvedSymbol = found };
		int openThread = -1;

		using var host = new QuickPickTestHost(
			ProviderOver(
				[server],

				(_, _) =>
				{
					openThread = Environment.CurrentManagedThreadId;
					return Task.CompletedTask;
				}
			)
		);

		host.Controller.Open("#");
		host.Type("#util", s_symbolRest);

		// The resolver answers after a delay on a pool thread, as a provider does that waits for a server.
		server.Delay = TimeSpan.FromMilliseconds(30);

		Assert.IsTrue(host.Accept());
		Assert.AreEqual(Environment.CurrentManagedThreadId, openThread);
		Assert.HasCount(1, server.ResolveCalls);
	}

	[TestMethod]
	public void AcceptAsync_AResolverThatThrowsAfterYielding_ReportsTheFaultOnTheDispatcherThread()
	{
		WorkspaceSymbol lost = Symbol("Util", DocumentSymbolKind.Class, InRoot("util.lua"));
		var server = new RecordingWorkspaceSymbolProvider(lost) { ResolveFailure = new InvalidOperationException("No answer.") };
		int faultThread = -1;

		using var host = new QuickPickTestHost(ProviderOver([server], reportFault: _ => faultThread = Environment.CurrentManagedThreadId));

		host.Controller.Open("#");
		host.Type("#util", s_symbolRest);

		server.Delay = TimeSpan.FromMilliseconds(30);

		Assert.IsTrue(host.Accept());
		Assert.AreEqual(Environment.CurrentManagedThreadId, faultThread);
	}

	[TestMethod]
	public void AcceptAsync_ThroughTheController_OpensTheActiveSymbol()
	{
		WorkspaceSymbol known = Symbol(name: "Utility", kind: DocumentSymbolKind.Class, path: InRoot("u.lua"), line: 5);
		var server = new RecordingWorkspaceSymbolProvider(known);
		List<WorkspaceSymbol> opened = [];
		using var host = new QuickPickTestHost(ProviderOver([server], Collect(opened)));

		host.Controller.Open("#");
		host.Type("#util", s_symbolRest);

		Assert.IsTrue(host.Accept());
		Assert.AreSame(known, opened.Single());
		Assert.IsFalse(host.Controller.State.IsOpen);
	}

	[TestMethod]
	public async Task Members_RejectNullArguments()
	{
		WorkspaceSymbolQuickPickProvider provider = ProviderOver([]);

		Assert.ThrowsExactly<ArgumentNullException>(() => new WorkspaceSymbolQuickPickProvider(null!, (_, _) => Task.CompletedTask));
		Assert.ThrowsExactly<ArgumentNullException>(() => new WorkspaceSymbolQuickPickProvider(() => [], null!));
		await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => provider.GetItemsAsync(null!, TestContext.CancellationToken));
		await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => provider.AcceptAsync(null!, TestContext.CancellationToken));
	}

	[TestMethod]
	public void Options_RejectNullRootsNullTextsAndANegativeDelay()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => WorkspaceSymbolQuickPickOptions.Default with { RootPaths = null! });
		Assert.ThrowsExactly<ArgumentNullException>(() => WorkspaceSymbolQuickPickOptions.Default with { RootPaths = [null!] });
		Assert.ThrowsExactly<ArgumentNullException>(() => WorkspaceSymbolQuickPickOptions.Default with { Name = null! });
		Assert.ThrowsExactly<ArgumentNullException>(() => WorkspaceSymbolQuickPickOptions.Default with { Placeholder = null! });
		Assert.ThrowsExactly<ArgumentNullException>(() => WorkspaceSymbolQuickPickOptions.Default with { EmptyText = null! });
		Assert.ThrowsExactly<ArgumentNullException>(() => WorkspaceSymbolQuickPickOptions.Default with { ContainerSeparator = null! });

		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
			WorkspaceSymbolQuickPickOptions.Default with
			{
				QueryDebounceDelay = TimeSpan.FromMilliseconds(-1),
			}
		);
	}

	public TestContext TestContext { get; set; } = null!;
}
