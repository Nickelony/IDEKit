using Nickelony.IDEKit.Core.Navigation;
using Nickelony.IDEKit.Core.Paths;
using Nickelony.IDEKit.FileWatching;
using Nickelony.QuickPick;

namespace Nickelony.IDEKit.Workspace.QuickPick.Tests;

/// <summary>
/// Covers the files mode of the quick pick: what a row shows, the group of the recently opened files and open documents,
/// the unsaved mark, the accept, and the way it works through a controller with a path query and a refresh.
/// </summary>
[TestClass]
public sealed class WorkspaceFileQuickPickProviderTests
{
	private static readonly TimeSpan s_wait = TimeSpan.FromSeconds(10);

	private static async Task<IReadOnlyList<QuickPickItem>> ItemsAsync(
		WorkspaceFileQuickPickProvider provider,
		string query = "",
		params string[] recentKeys
	) => await provider.GetItemsAsync(new QuickPickRequest(query, recentKeys), CancellationToken.None);

	[TestMethod]
	public void Constructor_NullRootsOrNullOpenCallback_Throw()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() =>
			new WorkspaceFileQuickPickProvider(null!, LocalPathComparisonPolicy.ForCurrentPlatform, (_, _) => Task.CompletedTask)
		);

		Assert.ThrowsExactly<ArgumentNullException>(() =>
			new WorkspaceFileQuickPickProvider([Path.GetTempPath()], LocalPathComparisonPolicy.ForCurrentPlatform, null!)
		);
	}

	[TestMethod]
	public void Constructor_ARootThatIsNullOrRelative_Throws()
	{
		Assert.ThrowsExactly<ArgumentException>(() =>
			new WorkspaceFileQuickPickProvider([null!], LocalPathComparisonPolicy.ForCurrentPlatform, (_, _) => Task.CompletedTask)
		);

		Assert.ThrowsExactly<ArgumentException>(() =>
			new WorkspaceFileQuickPickProvider(
				[Path.Combine("relative", "folder")],
				LocalPathComparisonPolicy.ForCurrentPlatform,
				(_, _) => Task.CompletedTask
			)
		);
	}

	[TestMethod]
	public void TheMode_HasNoPrefixAndMatchesPathsAndKeepsRecentFiles()
	{
		using var host = new WorkspaceFileTestHost();
		using WorkspaceFileQuickPickProvider provider = host.CreateProvider();

		Assert.AreEqual(string.Empty, provider.Prefix);
		Assert.AreEqual(QuickPickFilterMode.Path, provider.FilterMode);
		Assert.AreEqual("recently opened", provider.RecentGroup!.Label);
		Assert.AreEqual(0, provider.RecentGroup.Order);
		Assert.AreEqual("recent", provider.RecentGroup.Id);
	}

	[TestMethod]
	public async Task Texts_FollowTheOptions()
	{
		using var host = new WorkspaceFileTestHost();
		host.Write("a.txt");

		var options = new WorkspaceFileQuickPickOptions
		{
			Name = "Dateien",
			Placeholder = "Dateiname eingeben",
			EmptyText = "Keine Datei",
			RecentGroupLabel = "zuletzt geoeffnet",
			FileGroupLabel = "Dateien",
		};

		using WorkspaceFileQuickPickProvider provider = host.CreateProvider(options: options);
		IReadOnlyList<QuickPickItem> items = await ItemsAsync(provider, "", host.PathOf("a.txt"));

		Assert.AreEqual("Dateien", provider.Name);
		Assert.AreEqual("Dateiname eingeben", provider.Placeholder);
		Assert.AreEqual("Keine Datei", provider.EmptyText);
		Assert.AreEqual("zuletzt geoeffnet", provider.RecentGroup!.Label);
		Assert.AreEqual("zuletzt geoeffnet", items.Single().Group!.Label);
	}

	[TestMethod]
	public async Task Texts_ANullGroupLabelDrawsNoSeparator()
	{
		using var host = new WorkspaceFileTestHost();
		host.Write("a.txt");

		using WorkspaceFileQuickPickProvider provider = host.CreateProvider(options: new WorkspaceFileQuickPickOptions { FileGroupLabel = null });
		IReadOnlyList<QuickPickItem> items = await ItemsAsync(provider);

		Assert.IsNull(items.Single().Group!.Label);
	}

	[TestMethod]
	public async Task GetItemsAsync_ShowsEachFileByNameWithItsFolderBelowTheRoot()
	{
		using var host = new WorkspaceFileTestHost();
		string path = host.Write("src/lib/util.lua");

		host.Write("readme.md");

		using WorkspaceFileQuickPickProvider provider = host.CreateProvider();
		IReadOnlyList<QuickPickItem> items = await ItemsAsync(provider);
		QuickPickItem util = items.Single(item => item.Label == "util.lua");

		Assert.AreEqual(path, util.Key);
		Assert.AreEqual("src/lib", util.Description);
		Assert.IsNull(items.Single(item => item.Label == "readme.md").Description);
		Assert.IsTrue(items.All(item => item.Group is { Id: "files", Label: "file results", Order: 1 }));
		Assert.IsTrue(items.All(item => item.TrailingText is null && item.IsEnabled));
	}

	[TestMethod]
	public async Task GetItemsAsync_RecentKeys_ListTheirFilesFirstInTheOrderOfUse()
	{
		using var host = new WorkspaceFileTestHost();

		string a = host.Write("a.txt");
		string c = host.Write("c.txt");

		host.Write("b.txt");

		using WorkspaceFileQuickPickProvider provider = host.CreateProvider();
		IReadOnlyList<QuickPickItem> items = await ItemsAsync(provider, recentKeys: [c, a]);

		Assert.AreSequenceEqual(["c.txt", "a.txt", "b.txt"], items.Select(item => item.Label));
		Assert.AreSequenceEqual(["recent", "recent", "files"], items.Select(item => item.Group!.Id));
	}

	[TestMethod]
	public async Task GetItemsAsync_ARecentKeyOfAFileThatIsGoneOrExcluded_IsDropped()
	{
		using var host = new WorkspaceFileTestHost();

		string kept = host.Write("kept.txt");
		string excluded = host.Write("bin/out.dll");
		string deleted = host.Write("deleted.txt");

		File.Delete(deleted);

		using WorkspaceFileQuickPickProvider provider = host.CreateProvider(excludeGlobs: ["bin"]);
		IReadOnlyList<QuickPickItem> items = await ItemsAsync(provider, recentKeys: [deleted, excluded, kept, "not a path"]);

		Assert.AreSequenceEqual(["kept.txt"], items.Select(item => item.Label));
		Assert.AreEqual("recent", items[0].Group!.Id);
	}

	[TestMethod]
	public async Task GetItemsAsync_OpenDocuments_FollowTheRecentFilesInTheOrderOfTheHostAndShareTheirGroup()
	{
		using var host = new WorkspaceFileTestHost();

		string recent = host.Write("recent.txt");
		string first = host.Write("sub/first.txt");
		string second = host.Write("second.txt");

		host.Write("other.txt");

		OpenEditorDescriptor[] editors = [new("second.txt", second, false), new("first.txt", first, false)];
		using WorkspaceFileQuickPickProvider provider = host.CreateProvider(listOpenEditors: () => editors);
		IReadOnlyList<QuickPickItem> items = await ItemsAsync(provider, recentKeys: [recent]);

		Assert.AreSequenceEqual(["recent.txt", "second.txt", "first.txt", "other.txt"], items.Select(item => item.Label));
		Assert.AreSequenceEqual(["recent", "recent", "recent", "files"], items.Select(item => item.Group!.Id));
		Assert.AreEqual("sub", items[2].Description);
	}

	[TestMethod]
	public async Task GetItemsAsync_ADocumentWithUnsavedChanges_ShowsTheUnsavedText()
	{
		using var host = new WorkspaceFileTestHost();

		string dirty = host.Write("dirty.txt");
		string clean = host.Write("clean.txt");

		OpenEditorDescriptor[] editors = [new("dirty.txt", dirty, true), new("clean.txt", clean, false)];
		using WorkspaceFileQuickPickProvider provider = host.CreateProvider(listOpenEditors: () => editors);
		IReadOnlyList<QuickPickItem> items = await ItemsAsync(provider);

		Assert.AreEqual(WorkspaceFileQuickPickOptions.Default.UnsavedText, items.Single(item => item.Label == "dirty.txt").TrailingText);
		Assert.AreEqual(OpenEditorDescriptor.UnsavedIconKey, items.Single(item => item.Label == "dirty.txt").IconKey);
		Assert.IsNull(items.Single(item => item.Label == "clean.txt").IconKey);
		Assert.IsNull(items.Single(item => item.Label == "clean.txt").TrailingText);
	}

	[TestMethod]
	public async Task GetItemsAsync_TheUnsavedTextOfTheOptions_ReplacesTheDefaultAndNullHidesIt()
	{
		using var host = new WorkspaceFileTestHost();
		string dirty = host.Write("dirty.txt");

		OpenEditorDescriptor[] editors = [new("dirty.txt", dirty, true)];

		using WorkspaceFileQuickPickProvider localized = host.CreateProvider(
			listOpenEditors: () => editors,
			options: new WorkspaceFileQuickPickOptions { UnsavedText = "ungespeichert" }
		);

		using WorkspaceFileQuickPickProvider hidden = host.CreateProvider(
			listOpenEditors: () => editors,
			options: new WorkspaceFileQuickPickOptions { UnsavedText = null }
		);

		Assert.AreEqual("ungespeichert", (await ItemsAsync(localized)).Single().TrailingText);

		QuickPickItem item = (await ItemsAsync(hidden)).Single();

		Assert.IsNull(item.TrailingText);
		Assert.AreEqual(OpenEditorDescriptor.UnsavedIconKey, item.IconKey);
	}

	[TestMethod]
	public async Task GetItemsAsync_AnUnsavedDocumentThatHasNoFileInTheList_TakesTheUnsavedTextAndTheIconKeyToo()
	{
		using var host = new WorkspaceFileTestHost();

		host.Write("a.txt");

		OpenEditorDescriptor[] editors = [new("Untitled-1", "untitled-1", true)];

		using WorkspaceFileQuickPickProvider localized = host.CreateProvider(
			listOpenEditors: () => editors,
			options: new WorkspaceFileQuickPickOptions { UnsavedText = "ungespeichert" }
		);

		using WorkspaceFileQuickPickProvider hidden = host.CreateProvider(
			listOpenEditors: () => editors,
			options: new WorkspaceFileQuickPickOptions { UnsavedText = null }
		);

		QuickPickItem shown = (await ItemsAsync(localized)).Single(item => item.Key == "untitled-1");
		QuickPickItem plain = (await ItemsAsync(hidden)).Single(item => item.Key == "untitled-1");

		Assert.AreEqual("ungespeichert", shown.TrailingText);
		Assert.AreEqual(OpenEditorDescriptor.UnsavedIconKey, shown.IconKey);
		Assert.IsNull(plain.TrailingText);
		Assert.AreEqual(OpenEditorDescriptor.UnsavedIconKey, plain.IconKey);
	}

	[TestMethod]
	public async Task GetItemsAsync_TheMaxFileCountOfTheOptions_BoundsTheList()
	{
		using var host = new WorkspaceFileTestHost();

		for (int index = 0; index < 5; index++)
			host.Write($"f{index}.txt");

		using WorkspaceFileQuickPickProvider provider = host.CreateProvider(options: new WorkspaceFileQuickPickOptions { MaxFileCount = 2 });

		Assert.HasCount(2, await ItemsAsync(provider));
	}

	[TestMethod]
	public async Task GetItemsAsync_TheProviderDisposedWhileAQueryRuns_EndsTheQueryAsCanceled()
	{
		using var host = new WorkspaceFileTestHost();
		using var started = new ManualResetEventSlim();
		using var release = new ManualResetEventSlim();

		host.Write("a.txt");

		host.Enumerator = (directory, root, filter, token) =>
		{
			started.Set();
			release.Wait(s_wait, token);

			return WorkspaceFileWalker.List(directory, root, filter, cancellationToken: token);
		};

		WorkspaceFileQuickPickProvider provider = host.CreateProvider();
		Task<IReadOnlyList<QuickPickItem>> query = ItemsAsync(provider);

		Assert.IsTrue(await Task.Run(() => started.Wait(s_wait)));

		provider.Dispose();
		release.Set();

		await Assert.ThrowsAsync<OperationCanceledException>(() => query);
	}

	[TestMethod]
	public async Task GetItemsAsync_ADocumentThatIsRecentAndOpen_IsListedOnceWithTheUnsavedText()
	{
		using var host = new WorkspaceFileTestHost();
		string path = host.Write("both.txt");

		host.Write("other.txt");

		using WorkspaceFileQuickPickProvider provider = host.CreateProvider(listOpenEditors: () =>
			[new OpenEditorDescriptor("both.txt", path, true)]
		);

		IReadOnlyList<QuickPickItem> items = await ItemsAsync(provider, recentKeys: [path]);

		Assert.AreSequenceEqual(["both.txt", "other.txt"], items.Select(item => item.Label));
		Assert.AreEqual(WorkspaceFileQuickPickOptions.Default.UnsavedText, items[0].TrailingText);
		Assert.AreEqual(OpenEditorDescriptor.UnsavedIconKey, items[0].IconKey);
	}

	[TestMethod]
	public async Task GetItemsAsync_AnOpenDocumentOutsideTheRoots_IsListedByItsOwnPath()
	{
		using var host = new WorkspaceFileTestHost();
		using var elsewhere = new TemporaryDirectory("quickpick-elsewhere-");

		string outside = Path.Combine(elsewhere.Path, "outside.txt");

		host.Write("a.txt");

		OpenEditorDescriptor[] editors = [new("outside.txt", outside, true), new("Untitled-1", "untitled-1", true)];
		using WorkspaceFileQuickPickProvider provider = host.CreateProvider(listOpenEditors: () => editors);
		IReadOnlyList<QuickPickItem> items = await ItemsAsync(provider);

		Assert.AreSequenceEqual(["outside.txt", "untitled-1", "a.txt"], items.Select(item => item.Label));
		Assert.AreEqual(outside, items[0].Key);
		Assert.AreEqual(elsewhere.Path, items[0].Description);
		Assert.AreEqual("untitled-1", items[1].Key);
		Assert.IsNull(items[1].Description);

		Assert.IsTrue(
			items
				.Take(2)
				.All(item =>
					item.Group!.Id == "recent"
					&& item.TrailingText == WorkspaceFileQuickPickOptions.Default.UnsavedText
					&& item.IconKey == OpenEditorDescriptor.UnsavedIconKey
				)
		);
	}

	[TestMethod]
	public async Task GetItemsAsync_ADocumentIdSpelledWithAnotherCase_IsTheSameFileWhenThePolicyIgnoresCase()
	{
		if (!WorkspaceFileTestHost.Comparison.IgnoreCase)
			Assert.Inconclusive("The paths of this platform compare by case.");

		using var host = new WorkspaceFileTestHost();
		string path = host.Write("Sub/File.txt");

		host.Write("other.txt");

		using WorkspaceFileQuickPickProvider provider = host.CreateProvider(listOpenEditors: () =>
			[new OpenEditorDescriptor("File.txt", path.ToUpperInvariant(), false)]
		);

		IReadOnlyList<QuickPickItem> items = await ItemsAsync(provider);

		Assert.AreSequenceEqual(["File.txt", "other.txt"], items.Select(item => item.Label));
		Assert.AreEqual(path, items[0].Key);
	}

	[TestMethod]
	public async Task GetItemsAsync_TheCallbackOfTheOpenDocumentsThrows_IsReportedAndTheFilesStillList()
	{
		using var host = new WorkspaceFileTestHost();

		host.Write("a.txt");

		using WorkspaceFileQuickPickProvider provider = host.CreateProvider(listOpenEditors: () => throw new InvalidOperationException("tabs"));
		IReadOnlyList<QuickPickItem> items = await ItemsAsync(provider);

		Assert.AreSequenceEqual(["a.txt"], items.Select(item => item.Label));
		Assert.AreEqual("tabs", host.Faults.Single().Message);
	}

	[TestMethod]
	public async Task GetItemsAsync_ReadsTheOpenDocumentsOnTheCallingThreadBeforeItAwaits()
	{
		using var host = new WorkspaceFileTestHost();
		int thread = -1;

		host.Write("a.txt");

		using WorkspaceFileQuickPickProvider provider = host.CreateProvider(listOpenEditors: () =>
		{
			thread = Environment.CurrentManagedThreadId;
			return [];
		});

		Task<IReadOnlyList<QuickPickItem>> pending = provider.GetItemsAsync(new QuickPickRequest(string.Empty, []), CancellationToken.None);

		Assert.AreEqual(Environment.CurrentManagedThreadId, thread);

		await pending;
	}

	[TestMethod]
	public async Task GetItemsAsync_WithoutRecentFilesOrDocuments_ReturnsTheListItself()
	{
		using var host = new WorkspaceFileTestHost();

		host.Write("a.txt");

		using WorkspaceFileQuickPickProvider provider = host.CreateProvider();

		IReadOnlyList<QuickPickItem> first = await ItemsAsync(provider);
		IReadOnlyList<QuickPickItem> second = await ItemsAsync(provider);

		Assert.AreSame(first, second);
	}

	[TestMethod]
	public async Task AcceptAsync_OpensTheFileThroughTheHostCallbackWithTheTokenOfTheCall()
	{
		using var host = new WorkspaceFileTestHost();

		string path = host.Write("a.txt");
		WorkspaceFileOpenRequest? opened = null;
		CancellationToken seen = default;

		using var source = new CancellationTokenSource();

		using WorkspaceFileQuickPickProvider provider = host.CreateProvider(
			openFileAsync: (request, token) =>
			{
				opened = request;
				seen = token;
				return Task.CompletedTask;
			}
		);

		QuickPickItem item = (await ItemsAsync(provider)).Single();

		await provider.AcceptAsync(new QuickPickAcceptRequest(item, "a"), source.Token);

		Assert.AreEqual(new WorkspaceFileOpenRequest(path), opened);
		Assert.AreEqual(source.Token, seen);
	}

	[TestMethod]
	[DataRow("a", "a", null, null)]
	[DataRow("", "", null, null)]
	[DataRow("file.cs:12", "file.cs", 12, null)]
	[DataRow("file.cs:12:5", "file.cs", 12, 5)]
	[DataRow("src/file.cs:7", "src/file.cs", 7, null)]
	[DataRow("file.cs: 12 ", "file.cs", 12, null)]
	[DataRow("file.cs:", "file.cs", null, null)]
	[DataRow("file.cs:12:", "file.cs", 12, null)]
	[DataRow("file.cs:x", "file.cs:x", null, null)]
	[DataRow("file.cs:12x", "file.cs:12x", null, null)]
	[DataRow("a:b:12", "a:b", 12, null)]
	[DataRow("c:\\dir\\file.cs", "c:\\dir\\file.cs", null, null)]
	[DataRow("c:\\dir\\file.cs:3", "c:\\dir\\file.cs", 3, null)]
	[DataRow("99999999999:1", "99999999999", 1, null)]
	public async Task AcceptAsync_ALineAndColumnTypedAfterTheName_ReachTheHostCallbackAndLeaveTheFilterPart(
		string query,
		string expectedFilter,
		int? expectedLine,
		int? expectedColumn
	)
	{
		using var host = new WorkspaceFileTestHost();

		string path = host.Write("a.txt");
		WorkspaceFileOpenRequest? opened = null;

		using WorkspaceFileQuickPickProvider provider = host.CreateProvider(
			openFileAsync: (request, _) =>
			{
				opened = request;
				return Task.CompletedTask;
			}
		);

		QuickPickItem item = (await ItemsAsync(provider)).Single();

		await provider.AcceptAsync(new QuickPickAcceptRequest(item, query));

		Assert.AreEqual(expectedFilter, provider.GetFilterText(query));
		Assert.AreEqual(new WorkspaceFileOpenRequest(path, expectedLine, expectedColumn), opened);
	}

	[TestMethod]
	public void OpenRequest_ABadArgument_Throws()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => new WorkspaceFileOpenRequest(null!));
		Assert.ThrowsExactly<ArgumentException>(() => new WorkspaceFileOpenRequest("a.txt", line: null, column: 3));
	}

	[TestMethod]
	public async Task GetItemsAsync_AfterDispose_Throws()
	{
		using var host = new WorkspaceFileTestHost();
		WorkspaceFileQuickPickProvider provider = host.CreateProvider();

		await ItemsAsync(provider);

		provider.Dispose();
		provider.Dispose();

		Assert.IsTrue(((StubFileWatcher)host.Watchers.CreationsFor(host.Root).Single().Watcher!).IsDisposed);
		await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => ItemsAsync(provider));
	}

	[TestMethod]
	public async Task FilesChanged_IsRaisedAfterAFileIsCreated()
	{
		using var host = new WorkspaceFileTestHost();
		int raised = 0;

		using WorkspaceFileQuickPickProvider provider = host.CreateProvider();

		provider.FilesChanged += (sender, _) =>
		{
			Assert.AreSame(provider, sender);
			Interlocked.Increment(ref raised);
		};

		await ItemsAsync(provider);
		await host.DeliverAsync(new FileChange(host.Write("new.txt"), FileChangeKind.Created));

		Assert.AreEqual(1, raised);
	}

	[TestMethod]
	public void Open_TheRecentFilesComeFirstUnderAnyQueryEvenWhenAnotherFileScoresHigher()
	{
		using var host = new WorkspaceFileTestHost();
		string recent = host.Write("lib/my_alpha_helper.cs");

		host.Write("alpha.cs");
		host.Write("beta.cs");

		using WorkspaceFileQuickPickProvider provider = host.CreateProvider();
		using var controller = new QuickPickTestHost(provider);

		controller.CreateController(recentStore: new StubRecentStore(recent));
		controller.Open();

		Assert.AreSequenceEqual(["--recently opened--", "my_alpha_helper.cs", "--file results--", "alpha.cs", "beta.cs"], controller.RowTexts);

		controller.Type("alpha");

		Assert.AreSequenceEqual(["--recently opened--", "my_alpha_helper.cs", "--file results--", "alpha.cs"], controller.RowTexts);
		Assert.AreEqual("my_alpha_helper.cs", controller.Controller.State.ActiveItem!.Label);
	}

	[TestMethod]
	public void Open_AnOpenDocumentComesFirstUnderAnyQueryToo()
	{
		using var host = new WorkspaceFileTestHost();
		string open = host.Write("deep/my_alpha_helper.cs");

		host.Write("alpha.cs");

		using WorkspaceFileQuickPickProvider provider = host.CreateProvider(listOpenEditors: () =>
			[new OpenEditorDescriptor("my_alpha_helper.cs", open, true)]
		);

		using var controller = new QuickPickTestHost(provider);

		controller.Open();
		controller.Type("alpha");

		Assert.AreSequenceEqual(["--recently opened--", "my_alpha_helper.cs", "--file results--", "alpha.cs"], controller.RowTexts);
	}

	[TestMethod]
	public void Open_APathQuery_MatchesTheFolderAndPrefersTheFileNameWithoutASeparator()
	{
		using var host = new WorkspaceFileTestHost();

		host.Write("lib/util.lua");
		host.Write("util/readme.md");
		host.Write("src/main.lua");

		using WorkspaceFileQuickPickProvider provider = host.CreateProvider();
		using var controller = new QuickPickTestHost(provider);

		controller.Open();
		controller.Type("lib/ut");

		Assert.AreSequenceEqual(["--file results--", "util.lua"], controller.RowTexts);
		Assert.IsNotEmpty(controller.Controller.State.ActiveItem!.LabelHighlights);

		controller.Type("util");

		Assert.AreSequenceEqual(["--file results--", "util.lua", "readme.md"], controller.RowTexts);
	}

	[TestMethod]
	public void Open_AQueryWithoutAMatch_SaysSo()
	{
		using var host = new WorkspaceFileTestHost();

		host.Write("a.txt");

		using WorkspaceFileQuickPickProvider provider = host.CreateProvider();
		using var controller = new QuickPickTestHost(provider);

		controller.Open();
		controller.Type("zzzz");

		Assert.AreSequenceEqual(["!No matching files"], controller.RowTexts);
	}

	[TestMethod]
	public void Refresh_AfterAFileWasCreatedAndDeleted_ShowsTheNewListAndKeepsTheActiveFile()
	{
		using var host = new WorkspaceFileTestHost();
		string doomed = host.Write("a_doomed.txt");

		host.Write("b_kept.txt");

		using WorkspaceFileQuickPickProvider provider = host.CreateProvider();
		using var controller = new QuickPickTestHost(provider);

		controller.Open();
		controller.Controller.MoveActive(1);

		Assert.AreEqual("b_kept.txt", controller.Controller.State.ActiveItem!.Label);

		File.Delete(doomed);

		string created = host.Write("c_created.txt");

		host.DeliverAsync(new FileChange(doomed, FileChangeKind.Deleted), new FileChange(created, FileChangeKind.Created)).GetAwaiter().GetResult();
		controller.Refresh();

		Assert.AreSequenceEqual(["--file results--", "b_kept.txt", "c_created.txt"], controller.RowTexts);
		Assert.AreEqual("b_kept.txt", controller.Controller.State.ActiveItem!.Label);
	}

	[TestMethod]
	public void AcceptAsync_ThroughTheController_OpensTheFileAndListsItFirstTheNextTime()
	{
		using var host = new WorkspaceFileTestHost();

		List<string> opened = [];
		string wanted = host.Write("zzz/wanted.txt");

		host.Write("a.txt");

		using WorkspaceFileQuickPickProvider provider = host.CreateProvider(
			openFileAsync: (request, _) =>
			{
				opened.Add(request.Path);
				return Task.CompletedTask;
			}
		);

		using var controller = new QuickPickTestHost(provider);

		controller.Open();
		controller.Type("wanted");

		Assert.IsTrue(controller.Accept());
		Assert.AreSequenceEqual([wanted], opened);

		controller.Open();

		Assert.AreSequenceEqual(["--recently opened--", "wanted.txt", "--file results--", "a.txt"], controller.RowTexts);
	}

	[TestMethod]
	public void AcceptAsync_AFileNameWithALineAndAColumn_RanksByTheNameAndOpensAtThePosition()
	{
		using var host = new WorkspaceFileTestHost();

		List<WorkspaceFileOpenRequest> opened = [];
		string wanted = host.Write("zzz/wanted.txt");

		host.Write("a.txt");

		using WorkspaceFileQuickPickProvider provider = host.CreateProvider(
			openFileAsync: (request, _) =>
			{
				opened.Add(request);
				return Task.CompletedTask;
			}
		);

		using var controller = new QuickPickTestHost(provider);

		controller.Open();
		controller.Type("wanted:12:5");

		Assert.AreSequenceEqual(["--file results--", "wanted.txt"], controller.RowTexts);
		Assert.IsTrue(controller.Accept());
		Assert.AreSequenceEqual([new WorkspaceFileOpenRequest(wanted, 12, 5)], opened);
	}

	[TestMethod]
	public void Open_AFileNameAndAColonWithoutADigit_StillRanksByTheName()
	{
		using var host = new WorkspaceFileTestHost();

		host.Write("wanted.txt");
		host.Write("a.txt");

		using WorkspaceFileQuickPickProvider provider = host.CreateProvider();
		using var controller = new QuickPickTestHost(provider);

		controller.Open();
		controller.Type("wanted:");

		Assert.AreSequenceEqual(["--file results--", "wanted.txt"], controller.RowTexts);
	}

	[TestMethod]
	public void Open_TheFirstWalk_DoesNotBlockTheDispatcherAndRunsOnAnotherThread()
	{
		using var host = new WorkspaceFileTestHost();
		using var started = new ManualResetEventSlim();
		using var release = new ManualResetEventSlim();

		int walkThread = -1;

		host.Write("a.txt");

		host.Enumerator = (directory, root, filter, token) =>
		{
			walkThread = Environment.CurrentManagedThreadId;
			started.Set();
			release.Wait(s_wait, token);

			return WorkspaceFileWalker.List(directory, root, filter, cancellationToken: token);
		};

		using WorkspaceFileQuickPickProvider provider = host.CreateProvider();
		using var controller = new QuickPickTestHost(provider);

		controller.Controller.Open();

		Assert.IsTrue(started.Wait(s_wait));
		Assert.AreNotEqual(controller.Dispatcher.OwnerThreadId, walkThread);

		// The dispatcher is free while the walk is held: it keeps running its work and the state shows no rows yet.
		controller.Dispatcher.PumpUntilIdle();

		Assert.IsTrue(controller.Controller.State.IsOpen);
		Assert.IsEmpty(controller.Controller.State.Rows);

		release.Set();
		controller.Dispatcher.PumpUntil(() => controller.Controller.State.Rows.Count > 0);

		Assert.AreSequenceEqual(["--file results--", "a.txt"], controller.RowTexts);
	}

	[TestMethod]
	[TestCategory(TestCategories.Integration)]
	public async Task TheFileWatcher_FollowsACreatedAndADeletedFileInTheFolder()
	{
		using var folder = new TemporaryDirectory("quickpick-live-");

		File.WriteAllText(Path.Combine(folder.Path, "first.txt"), "text");

		using var provider = new WorkspaceFileQuickPickProvider([folder.Path], LocalPathComparisonPolicy.ForCurrentPlatform, (_, _) => Task.CompletedTask);
		using var changed = new ManualResetEventSlim();

		provider.FilesChanged += (_, _) => changed.Set();

		Assert.AreSequenceEqual(["first.txt"], (await ItemsAsync(provider)).Select(item => item.Label));

		string created = Path.Combine(folder.Path, "second.txt");

		File.WriteAllText(created, "text");

		await TestPolling.UntilAsync(() => ItemsAsync(provider).GetAwaiter().GetResult().Any(item => item.Label == "second.txt"), s_wait);
		await TestPolling.UntilAsync(() => changed.IsSet, s_wait);

		File.Delete(created);

		await TestPolling.UntilAsync(() => ItemsAsync(provider).GetAwaiter().GetResult().All(item => item.Label != "second.txt"), s_wait);
	}

	[TestMethod]
	[TestCategory(TestCategories.Integration)]
	[DataRow(true, 1)]
	[DataRow(false, 2)]
	public async Task TheGlobCasing_FollowsThePathComparison(bool ignoreCase, int expectedFileCount)
	{
		using var folder = new TemporaryDirectory("quickpick-casing-");

		Directory.CreateDirectory(Path.Combine(folder.Path, "bin"));

		File.WriteAllText(Path.Combine(folder.Path, "bin", "inner.txt"), "text");
		File.WriteAllText(Path.Combine(folder.Path, "keep.txt"), "text");

		using var provider = new WorkspaceFileQuickPickProvider(
			rootPaths: [folder.Path],
			pathComparison: new LocalPathComparisonPolicy(ignoreCase),
			openFileAsync: (_, _) => Task.CompletedTask,
			options: new WorkspaceFileQuickPickOptions { ExcludeGlobs = ["BIN"] }
		);

		Assert.HasCount(expectedFileCount, await ItemsAsync(provider));
	}

	[TestMethod]
	[TestCategory(TestCategories.Integration)]
	[DataRow(true, 1)]
	[DataRow(false, 2)]
	public async Task ARecentFileAndAnOpenDocumentSpelledInAnotherCase_AreOneRowWhereThePathComparisonIgnoresCase(
		bool ignoreCase,
		int expectedRowCount
	)
	{
		using var folder = new TemporaryDirectory("quickpick-casing-");
		string path = Path.Combine(folder.Path, "a.txt");

		File.WriteAllText(path, "text");

		using var provider = new WorkspaceFileQuickPickProvider(
			rootPaths: [folder.Path],
			pathComparison: new LocalPathComparisonPolicy(ignoreCase),
			openFileAsync: (_, _) => Task.CompletedTask,
			listOpenEditors: () => [new OpenEditorDescriptor("A.TXT", Path.Combine(folder.Path, "A.TXT"), isUnsaved: false)]
		);

		Assert.HasCount(expectedRowCount, await ItemsAsync(provider, "", path));
	}

	private sealed class StubRecentStore(params string[] keys) : IQuickPickRecentStore
	{
		public IReadOnlyDictionary<string, IReadOnlyList<string>> Load() => new Dictionary<string, IReadOnlyList<string>> { [string.Empty] = keys };
		public bool Save(IReadOnlyDictionary<string, IReadOnlyList<string>> keysByPrefix) => true;
	}
}
