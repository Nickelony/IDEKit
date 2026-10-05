namespace Nickelony.QuickPick.Tests;

public sealed partial class QuickPickControllerTests
{
	[TestMethod]
	public void AcceptAsync_TheSessionEndedAndAnotherOpenedWhileWaiting_AcceptsNothingForTheOldCall()
	{
		ScriptedQuickPickProvider provider = ProviderOf("");

		var answers = new GatedAnswers(provider);
		using var host = new QuickPickTestHost(provider);

		host.Controller.Open();
		answers.Release("", "first");
		host.Dispatcher.PumpUntilIdle();
		host.Controller.SetText("x");

		Task<bool> stale = host.Controller.AcceptAsync();

		host.Controller.Dismiss();
		host.Controller.Open();
		answers.Release("", "second");
		host.Dispatcher.PumpUntilIdle();

		// The user presses Enter in the new session before the slow answer of the old session arrives.
		Task<bool> fresh = host.Controller.AcceptAsync();

		host.Dispatcher.PumpUntil(() => fresh.IsCompleted);

		Assert.IsTrue(fresh.Result);
		Assert.AreSequenceEqual(["second"], [.. provider.Accepted.Select(item => item.Key)]);

		answers.Release("x", "late");
		host.Dispatcher.PumpUntil(() => stale.IsCompleted);

		Assert.IsFalse(stale.Result);
		Assert.AreSequenceEqual(["second"], [.. provider.Accepted.Select(item => item.Key)]);
	}

	[TestMethod]
	public void AcceptAsync_TheOldAnswerArrivesBeforeTheNewSessionAccepts_TheNewSessionStillAcceptsItsOwnItem()
	{
		ScriptedQuickPickProvider provider = ProviderOf("");

		var answers = new GatedAnswers(provider);
		using var host = new QuickPickTestHost(provider);

		host.Controller.Open();
		answers.Release("", "first");
		host.Dispatcher.PumpUntilIdle();
		host.Controller.SetText("x");

		Task<bool> stale = host.Controller.AcceptAsync();

		host.Controller.Dismiss();
		host.Controller.Open();
		answers.Release("", "second");
		host.Dispatcher.PumpUntilIdle();
		answers.Release("x", "late");
		host.Dispatcher.PumpUntil(() => stale.IsCompleted);

		Assert.IsFalse(stale.Result);
		Assert.IsTrue(host.Controller.State.IsOpen);
		Assert.IsEmpty(provider.Accepted);
		Assert.IsTrue(host.Accept());
		Assert.AreSequenceEqual(["second"], [.. provider.Accepted.Select(item => item.Key)]);
	}

	[TestMethod]
	public void AcceptAsync_TheSessionIsDisposedWhileWaiting_AcceptsNothing()
	{
		ScriptedQuickPickProvider provider = ProviderOf("");

		var answers = new GatedAnswers(provider);
		using var host = new QuickPickTestHost(provider);

		host.Controller.Open();

		Task<bool> accept = host.Controller.AcceptAsync();

		host.Controller.Dispose();
		answers.Release("", "late");
		host.Dispatcher.PumpUntil(() => accept.IsCompleted);

		Assert.IsFalse(accept.Result);
		Assert.IsEmpty(provider.Accepted);
	}

	[TestMethod]
	public void AcceptAsync_AnotherSessionAccepts_StartsTheProviderAcceptOnlyAfterTheEarlierOneEnded()
	{
		ScriptedQuickPickProvider provider = ProviderOf("", "a");

		var release = new TaskCompletionSource();

		int running = 0;
		int mostRunning = 0;

		using var host = new QuickPickTestHost(provider);

		provider.OnAccept = async item =>
		{
			mostRunning = Math.Max(mostRunning, ++running);

			if (item.Key == "a" && provider.Accepted.Count == 1)
				await release.Task;

			running--;
		};

		host.Controller.Open();

		Task<bool> first = host.Controller.AcceptAsync();

		host.Controller.Open();

		Task<bool> second = host.Controller.AcceptAsync();

		host.Dispatcher.PumpUntilIdle();

		Assert.HasCount(1, provider.Accepted);
		Assert.IsFalse(first.IsCompleted);
		Assert.IsFalse(second.IsCompleted);

		release.SetResult();
		host.Dispatcher.PumpUntil(() => first.IsCompleted && second.IsCompleted);

		Assert.HasCount(2, provider.Accepted);
		Assert.AreEqual(1, mostRunning);
		Assert.IsTrue(first.Result);
		Assert.IsTrue(second.Result);
	}

	[TestMethod]
	public void Refresh_TheUserMovesWhileTheQueryRuns_KeepsTheItemThatIsActiveWhenTheAnswerArrives()
	{
		ScriptedQuickPickProvider provider = ProviderOf("");

		var answers = new GatedAnswers(provider);
		using var host = new QuickPickTestHost(provider);

		host.Controller.Open();
		answers.Release("", "a", "b", "c");
		host.Dispatcher.PumpUntilIdle();
		host.Controller.Refresh();

		Assert.IsTrue(host.Controller.MoveActive(1));
		Assert.AreEqual("b", host.Controller.State.ActiveItem!.Key);

		answers.Release("", "a", "b", "c");
		host.Dispatcher.PumpUntilIdle();

		Assert.AreEqual("b", host.Controller.State.ActiveItem!.Key);
	}

	[TestMethod]
	public void GetItemsAsync_EveryQuery_StartsOnTheDispatcherThread()
	{
		ScriptedQuickPickProvider provider = ProviderOf("", "a", "b");
		using var host = new QuickPickTestHost(provider);

		host.Controller.Open();
		host.Controller.SetText("a");
		host.PassDebounce();
		host.Controller.Refresh();
		host.Controller.SetText("ab");

		Task<bool> accept = host.Controller.AcceptAsync();

		host.Dispatcher.PumpUntil(() => accept.IsCompleted);

		Assert.IsGreaterThan(3, provider.RequestThreadIds.Count);
		Assert.IsTrue(provider.RequestThreadIds.All(id => id == host.Dispatcher.OwnerThreadId));
	}

	[TestMethod]
	public void AcceptAsync_WhileAnAnswerFromAPoolThreadIsPending_AcceptsPublishesAndReportsOnTheOwnerThread()
	{
		ScriptedQuickPickProvider provider = ProviderOf("");

		provider.Answer = async (_, cancellationToken) =>
		{
			// The provider leaves the owner thread for good, as a client that reads a socket does.
			await Task.Delay(TimeSpan.FromMilliseconds(30.0), cancellationToken).ConfigureAwait(false);
			return [ScriptedQuickPickProvider.Item("a")];
		};

		using var host = new QuickPickTestHost(provider);
		List<int> changedThreadIds = [];

		host.Controller.Changed += (_, _) => changedThreadIds.Add(Environment.CurrentManagedThreadId);
		host.Controller.Open();

		Task<bool> accept = host.Controller.AcceptAsync();

		host.Dispatcher.PumpUntil(() => accept.IsCompleted);

		Assert.IsTrue(accept.Result);
		Assert.AreSequenceEqual(["a"], [.. provider.Accepted.Select(item => item.Key)]);
		Assert.AreSequenceEqual([host.Dispatcher.OwnerThreadId], [.. provider.AcceptThreadIds]);
		Assert.IsNotEmpty(changedThreadIds);
		Assert.IsTrue(changedThreadIds.All(id => id == host.Dispatcher.OwnerThreadId));
		Assert.IsEmpty(host.Faults);
	}

	[TestMethod]
	public void SetText_AnAnswerThatArrivedOnAPoolThreadBeforeTheTextChanged_IsNeverPublished()
	{
		ScriptedQuickPickProvider provider = ProviderOf("");

		var answers = new GatedAnswers(provider);
		using var host = new QuickPickTestHost(provider);

		host.Controller.Open();

		// The answer completes on a thread of the pool, and the continuation of the query returns to the owner thread through the context, which queues it.
		var answered = Task.Run(() => answers.Release("", "stale"));

		Assert.IsTrue(SpinWait.SpinUntil(() => host.Dispatcher.QueuedCount > 0, TimeSpan.FromSeconds(5.0)));

		host.Controller.SetText("b");
		host.Dispatcher.PumpUntil(() => answered.IsCompleted);
		host.Dispatcher.PumpUntilIdle();

		Assert.IsEmpty(host.Keys);
		Assert.DoesNotContain(state => state.Rows.Any(row => row.Item?.Key == "stale"), host.States);
	}

	[TestMethod]
	public void Open_TheProviderThrowsFromPlaceholder_IsContainedAndReported()
	{
		ScriptedQuickPickProvider provider = ProviderOf("", "a");
		provider.PlaceholderFault = new InvalidOperationException("placeholder failed");
		using var host = new QuickPickTestHost(provider);

		host.Controller.Open();

		Assert.IsTrue(host.Controller.State.IsOpen);
		Assert.AreEqual(string.Empty, host.Controller.State.Placeholder);
		Assert.AreSequenceEqual(["a"], host.Keys);
		Assert.Contains(fault => fault.Message == "placeholder failed", host.Faults);
	}

	[TestMethod]
	public void Open_TheProviderThrowsFromFilterMode_ListsTheItemsWithTheFuzzyFilter()
	{
		ScriptedQuickPickProvider provider = ProviderOf("", "alpha", "beta");
		provider.FilterModeFault = new InvalidOperationException("filter mode failed");
		using var host = new QuickPickTestHost(provider);

		host.Controller.Open("alp");

		Assert.AreSequenceEqual(["alpha"], host.Keys);
		Assert.Contains(fault => fault.Message == "filter mode failed", host.Faults);
	}

	[TestMethod]
	public void Open_TheProviderThrowsFromRecentGroup_ListsTheItemsWithoutRecency()
	{
		ScriptedQuickPickProvider provider = ProviderOf("", "a", "b");
		provider.RecentGroupFault = new InvalidOperationException("recent group failed");
		using var host = new QuickPickTestHost(provider);

		host.Controller.Open();

		Assert.AreSequenceEqual(["a", "b"], host.Keys);
		Assert.Contains(fault => fault.Message == "recent group failed", host.Faults);
	}

	[TestMethod]
	public void AcceptAsync_TheProviderThrowsFromRecentGroup_StillAcceptsTheItem()
	{
		ScriptedQuickPickProvider provider = ProviderOf("", "a");
		using var host = new QuickPickTestHost(provider);

		host.Controller.Open();
		provider.RecentGroupFault = new InvalidOperationException("recent group failed");

		Assert.IsTrue(host.Accept());
		Assert.HasCount(1, provider.Accepted);
		Assert.Contains(fault => fault.Message == "recent group failed", host.Faults);
	}

	[TestMethod]
	public void Constructor_TheProviderPrefix_IsReadOnce()
	{
		var provider = new CountingPrefixProvider();
		using var host = new QuickPickTestHost();
		var controller = new QuickPickController(Modes(provider), timeProvider: host.Dispatcher.TimeProvider);

		controller.Open();
		controller.SetText("a");
		controller.Refresh();
		controller.Dismiss();
		controller.Dispose();

		Assert.AreEqual(1, provider.PrefixReads);
	}

	[TestMethod]
	public void Accept_EachAcceptedKey_BecomesTheLatestAndTheListIsTrimmedToTheCapacity()
	{
		ScriptedQuickPickProvider provider = ProviderOf(">", "a", "b", "c");
		provider.RecentGroup = new QuickPickGroup("recent", "recently used");

		var store = new RecordingQuickPickRecentStore();
		using var host = new QuickPickTestHost(provider);

		QuickPickController controller = host.CreateController(QuickPickControllerOptions.Default with { MaxRecentCount = 2 }, store);

		foreach (string key in new[] { "a", "b", "a", "c" })
		{
			controller.Open(">");
			controller.SetActiveRow(ActiveRowOf(controller, key));

			Assert.AreEqual(key, controller.State.ActiveItem!.Key);
			Assert.IsTrue(host.Accept());
		}

		Assert.AreSequenceEqual(["c", "a"], [.. store.KeysOf(">")]);
		Assert.HasCount(4, store.Saves);
	}

	[TestMethod]
	public void Constructor_AStoreWithKeys_ListsTheRecentItemsFirstWithoutRepeats()
	{
		ScriptedQuickPickProvider provider = ProviderOf(">", "a", "b", "c");
		provider.RecentGroup = new QuickPickGroup("recent", "recently used");

		var store = new RecordingQuickPickRecentStore(new Dictionary<string, IReadOnlyList<string>> { [">"] = ["b", "b", "c"] });
		using var host = new QuickPickTestHost(provider);

		host.CreateController(recentStore: store).Open(">");

		Assert.AreSequenceEqual(["b", "c"], [.. provider.Requests[0].RecentKeys]);
		Assert.AreSequenceEqual(["--recently used--", "b", "c", "a"], host.RowTexts);
	}

	[TestMethod]
	public void Accept_ASavedDocument_KeepsTheKeysOfTheOtherModes()
	{
		ScriptedQuickPickProvider provider = ProviderOf(">", "a");
		provider.RecentGroup = new QuickPickGroup("recent", "recently used");

		var store = new RecordingQuickPickRecentStore(new Dictionary<string, IReadOnlyList<string>> { ["@"] = ["x"] });
		using var host = new QuickPickTestHost(provider);

		host.CreateController(recentStore: store).Open(">");
		host.Accept();

		Assert.AreSequenceEqual(["x"], [.. store.KeysOf("@")]);
		Assert.AreSequenceEqual(["a"], [.. store.KeysOf(">")]);
	}

	[TestMethod]
	public void Accept_TheStoreFailsToSave_ReportsTheFaultAndKeepsTheKeysForTheSession()
	{
		ScriptedQuickPickProvider provider = ProviderOf(">", "a", "b");
		provider.RecentGroup = new QuickPickGroup("recent", "recently used");
		using var host = new QuickPickTestHost(provider);
		QuickPickController controller = host.CreateController(recentStore: new ThrowingRecentStore());

		controller.Open(">");
		controller.SetActiveRow(ActiveRowOf(controller, "b"));

		Assert.IsTrue(host.Accept());
		Assert.IsNotEmpty(host.Faults);

		controller.Open(">");

		Assert.AreSequenceEqual(["--recently used--", "b", "a"], host.RowTexts);
	}

	private static int ActiveRowOf(QuickPickController controller, string key)
	{
		for (int index = 0; index < controller.State.Rows.Count; index++)
		{
			if (controller.State.Rows[index].Item?.Key == key)
				return index;
		}

		return -1;
	}

	private sealed class CountingPrefixProvider : IQuickPickProvider
	{
		public int PrefixReads { get; private set; }

		public string Prefix
		{
			get
			{
				PrefixReads++;
				return string.Empty;
			}
		}

		public string Name => "Counting";

		public string Placeholder => string.Empty;

		public Task<IReadOnlyList<QuickPickItem>> GetItemsAsync(QuickPickRequest request, CancellationToken cancellationToken = default) =>
			Task.FromResult<IReadOnlyList<QuickPickItem>>([new QuickPickItem("a", "a")]);

		public Task AcceptAsync(QuickPickAcceptRequest request, CancellationToken cancellationToken = default) => Task.CompletedTask;
	}
}
