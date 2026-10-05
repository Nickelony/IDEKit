namespace Nickelony.QuickPick.Tests;

public sealed partial class QuickPickControllerTests
{
	/// <summary>Holds the answer to each request back until a test releases it, and ignores the cancellation of the request.</summary>
	/// <remarks>
	/// Each request gets a gate of its own, in the order of the requests, because two awaiters of one task do not both run at once.
	/// </remarks>
	private sealed class GatedAnswers
	{
		private readonly List<(string Query, TaskCompletionSource<IReadOnlyList<QuickPickItem>> Gate)> _requests = [];

		public GatedAnswers(ScriptedQuickPickProvider provider)
		{
			provider.Answer = (request, _) =>
			{
				var gate = new TaskCompletionSource<IReadOnlyList<QuickPickItem>>();

				_requests.Add((request.Query, gate));

				return gate.Task;
			};
		}

		public bool IsHeld(string query) => _requests.Any(request => request.Query == query);

		/// <summary>Answers the newest request for the query.</summary>
		public void Release(string query, params string[] keys) =>
			CompleteWithoutContext(() => Newest(query).SetResult([.. keys.Select(key => ScriptedQuickPickProvider.Item(key))]));

		/// <summary>Answers the oldest request for the query.</summary>
		public void ReleaseOldest(string query, params string[] keys) =>
			CompleteWithoutContext(() => Oldest(query).SetResult([.. keys.Select(key => ScriptedQuickPickProvider.Item(key))]));

		public void Fail(string query, Exception exception) => CompleteWithoutContext(() => Newest(query).SetException(exception));

		/// <summary>Cancels the newest request for the query with the given token, or with a token the provider made itself.</summary>
		public void Cancel(string query, CancellationToken token = default)
		{
			using var own = new CancellationTokenSource();

			own.Cancel();

			CompleteWithoutContext(() => Newest(query).SetCanceled(token.CanBeCanceled ? token : own.Token));
		}

		// The controller awaits a provider's task without capturing a context. A task completed on a thread that has a context hands such a
		// continuation to the thread pool, which makes the moment the answer is ranked a race. Completing it on a thread without a context runs the
		// continuation at once, so the pumped dispatcher then holds the rest of the work and a test pumps it to the end.
		private static void CompleteWithoutContext(Action complete)
		{
			SynchronizationContext? saved = SynchronizationContext.Current;

			SynchronizationContext.SetSynchronizationContext(null);

			try
			{
				complete();
			}
			finally
			{
				SynchronizationContext.SetSynchronizationContext(saved);
			}
		}

		private TaskCompletionSource<IReadOnlyList<QuickPickItem>> Newest(string query) => _requests.Last(request => request.Query == query).Gate;

		private TaskCompletionSource<IReadOnlyList<QuickPickItem>> Oldest(string query) => _requests.First(request => request.Query == query).Gate;
	}

	[TestMethod]
	public void Open_TheFirstQuery_StartsWithoutWaitingForTheDebounce()
	{
		ScriptedQuickPickProvider provider = ProviderOf("", "a");
		using var host = new QuickPickTestHost(provider);

		host.Controller.Open();

		// The clock has not moved, so the query did not wait for a timer.
		Assert.HasCount(1, provider.Requests);
		Assert.AreEqual(0, host.Dispatcher.PendingTimerCount);
	}

	[TestMethod]
	public void SetText_ARunOfKeystrokes_AsksTheProviderOnceForTheLastText()
	{
		ScriptedQuickPickProvider provider = ProviderOf("", "abc");
		using var host = new QuickPickTestHost(provider);

		host.Controller.Open();
		host.Controller.SetText("a");
		host.Dispatcher.Advance(TimeSpan.FromMilliseconds(20.0));
		host.Controller.SetText("ab");
		host.Dispatcher.Advance(TimeSpan.FromMilliseconds(20.0));
		host.Controller.SetText("abc");

		// The delay restarts with each keystroke, so no query has started for the typed text yet.
		Assert.HasCount(1, provider.Requests);

		host.PassDebounce();

		Assert.AreSequenceEqual(["", "abc"], [.. provider.Requests.Select(request => request.Query)]);
	}

	[TestMethod]
	public void SetText_BeforeTheDelayElapsed_HoldsTheQueryBack()
	{
		ScriptedQuickPickProvider provider = ProviderOf("", "abc");
		using var host = new QuickPickTestHost(provider);

		host.Controller.Open();
		host.Controller.SetText("a");
		host.Dispatcher.Advance(QuickPickControllerOptions.Default.QueryDebounceDelay - TimeSpan.FromMilliseconds(1.0));

		Assert.HasCount(1, provider.Requests);

		host.Dispatcher.Advance(TimeSpan.FromMilliseconds(1.0));

		Assert.HasCount(2, provider.Requests);
	}

	[TestMethod]
	public void SetText_WhileAQueryIsInFlight_CancelsItsTokenAndNeverShowsItsAnswer()
	{
		ScriptedQuickPickProvider provider = ProviderOf("");

		var answers = new GatedAnswers(provider);
		using var host = new QuickPickTestHost(provider);

		host.Controller.Open();
		Assert.IsFalse(provider.Tokens[0].IsCancellationRequested);

		host.Controller.SetText("a");

		Assert.IsTrue(provider.Tokens[0].IsCancellationRequested);

		host.PassDebounce();
		answers.Release("a", "a-item");
		host.Dispatcher.PumpUntilIdle();

		// The provider ignored the cancellation and answers the older text late.
		answers.Release("", "stale-item");
		host.Dispatcher.PumpUntilIdle();

		Assert.AreSequenceEqual(["a-item"], host.Keys);
		Assert.DoesNotContain(state => state.Rows.Any(row => row.Item?.Key == "stale-item"), host.States);
	}

	[TestMethod]
	public void SetText_AnAnswerOfAnOlderQueryArrivingFirst_IsStillDropped()
	{
		ScriptedQuickPickProvider provider = ProviderOf("");

		var answers = new GatedAnswers(provider);
		using var host = new QuickPickTestHost(provider);

		host.Controller.Open();
		host.Controller.SetText("a");
		host.PassDebounce();

		answers.Release("", "stale-item");
		host.Dispatcher.PumpUntilIdle();

		Assert.IsEmpty(host.Keys);

		answers.Release("a", "a-item");
		host.Dispatcher.PumpUntilIdle();

		Assert.AreSequenceEqual(["a-item"], host.Keys);
	}

	[TestMethod]
	public void Refresh_WhileAQueryIsInFlight_DropsTheOlderAnswer()
	{
		ScriptedQuickPickProvider provider = ProviderOf("");

		var answers = new GatedAnswers(provider);
		using var host = new QuickPickTestHost(provider);

		host.Controller.Open();
		host.Controller.Refresh();

		Assert.HasCount(2, provider.Requests);

		Assert.IsTrue(provider.Tokens[0].IsCancellationRequested);

		answers.Release("", "fresh");
		answers.ReleaseOldest("", "stale");
		host.Dispatcher.PumpUntilIdle();

		// The older request answers last with other items, and its answer is dropped.
		Assert.AreSequenceEqual(["fresh"], host.Keys);
		Assert.DoesNotContain(state => state.Rows.Any(row => row.Item?.Key == "stale"), host.States);
	}

	[TestMethod]
	public void Dismiss_WhileAQueryIsInFlight_DropsItsAnswer()
	{
		ScriptedQuickPickProvider provider = ProviderOf("");

		var answers = new GatedAnswers(provider);
		using var host = new QuickPickTestHost(provider);

		host.Controller.Open();
		host.Controller.Dismiss();

		Assert.IsTrue(provider.Tokens[0].IsCancellationRequested);

		answers.Release("", "late-item");
		host.Dispatcher.PumpUntilIdle();

		Assert.IsFalse(host.Controller.State.IsOpen);
		Assert.IsEmpty(host.Controller.State.Rows);
	}

	[TestMethod]
	public void GetItemsAsync_Throws_ShowsNoRowsAndReportsTheFailure()
	{
		ScriptedQuickPickProvider provider = ProviderOf("", "a");
		using var host = new QuickPickTestHost(provider);

		host.Controller.Open();
		Assert.AreSequenceEqual(["a"], host.Keys);

		provider.Answer = (_, _) => throw new InvalidOperationException("provider failed");
		host.Controller.SetText("b");
		host.PassDebounce();

		Assert.IsEmpty(host.Controller.State.Rows);
		Assert.IsTrue(host.Controller.State.IsOpen);
		Assert.HasCount(1, host.Faults);
		Assert.AreEqual("provider failed", host.Faults[0].Message);
	}

	[TestMethod]
	public void GetItemsAsync_ReturnsAFaultedTask_ShowsNoRowsAndReportsTheFailure()
	{
		ScriptedQuickPickProvider provider = ProviderOf("");
		provider.Answer = (_, _) => Task.FromException<IReadOnlyList<QuickPickItem>>(new InvalidOperationException("provider failed"));
		using var host = new QuickPickTestHost(provider);

		host.Controller.Open();

		Assert.IsEmpty(host.Controller.State.Rows);
		Assert.HasCount(1, host.Faults);
	}

	[TestMethod]
	public void GetItemsAsync_ReturnsNull_ShowsNoRows()
	{
		ScriptedQuickPickProvider provider = ProviderOf("");
		provider.Answer = (_, _) => Task.FromResult<IReadOnlyList<QuickPickItem>>(null!);
		using var host = new QuickPickTestHost(provider);

		host.Controller.Open();

		Assert.IsEmpty(host.Controller.State.Rows);
		Assert.IsEmpty(host.Faults);
	}

	[TestMethod]
	public void GetItemsAsync_CanceledWithATokenOfItsOwn_ReportsTheFailureAndClearsBusy()
	{
		ScriptedQuickPickProvider provider = ProviderOf("", "a");

		var answers = new GatedAnswers(provider);
		using var host = new QuickPickTestHost(provider);

		host.Controller.Open();
		answers.Release("", "a");
		host.Dispatcher.PumpUntilIdle();
		host.Controller.SetText("b");
		host.PassDebounce();
		host.Dispatcher.Advance(QuickPickControllerOptions.Default.BusyDelay);

		Assert.IsTrue(host.Controller.State.IsBusy);

		answers.Cancel("b");
		host.Dispatcher.PumpUntil(() => !host.Controller.State.IsBusy);

		Assert.IsEmpty(host.Controller.State.Rows);
		Assert.IsTrue(host.Controller.State.IsOpen);
		Assert.HasCount(1, host.Faults);
		Assert.IsInstanceOfType<OperationCanceledException>(host.Faults[0]);
	}

	[TestMethod]
	public void GetItemsAsync_CanceledWithTheTokenOfTheController_IsNotAFailure()
	{
		ScriptedQuickPickProvider provider = ProviderOf("");

		var answers = new GatedAnswers(provider);
		using var host = new QuickPickTestHost(provider);

		host.Controller.Open();
		host.Controller.SetText("b");
		host.PassDebounce();

		Assert.IsTrue(provider.Tokens[0].IsCancellationRequested);

		answers.Cancel("", provider.Tokens[0]);
		answers.Release("b", "b1");
		host.Dispatcher.PumpUntilIdle();

		Assert.IsEmpty(host.Faults);
		Assert.AreSequenceEqual(["b1"], host.Keys);
	}

	[TestMethod]
	public void Query_PastTheBusyDelay_ReportsBusyUntilTheAnswerArrives()
	{
		ScriptedQuickPickProvider provider = ProviderOf("");

		var answers = new GatedAnswers(provider);
		using var host = new QuickPickTestHost(provider);

		host.Controller.Open();
		host.Dispatcher.Advance(QuickPickControllerOptions.Default.BusyDelay - TimeSpan.FromMilliseconds(1.0));

		Assert.IsFalse(host.Controller.State.IsBusy);

		host.Dispatcher.Advance(TimeSpan.FromMilliseconds(1.0));

		Assert.IsTrue(host.Controller.State.IsBusy);

		answers.Release("", "a");
		host.Dispatcher.PumpUntilIdle();

		Assert.IsFalse(host.Controller.State.IsBusy);
		Assert.AreSequenceEqual(["a"], host.Keys);
	}

	[TestMethod]
	public void Query_AnsweringBeforeTheBusyDelay_NeverReportsBusy()
	{
		ScriptedQuickPickProvider provider = ProviderOf("", "a");
		using var host = new QuickPickTestHost(provider);

		host.Controller.Open();
		host.Dispatcher.Advance(TimeSpan.FromSeconds(1.0));

		Assert.DoesNotContain(state => state.IsBusy, host.States);
	}

	[TestMethod]
	public void Query_FailingAfterTheBusyDelay_ClearsTheBusyState()
	{
		ScriptedQuickPickProvider provider = ProviderOf("");

		var answers = new GatedAnswers(provider);
		using var host = new QuickPickTestHost(provider);

		host.Controller.Open();
		host.Dispatcher.Advance(QuickPickControllerOptions.Default.BusyDelay);

		Assert.IsTrue(host.Controller.State.IsBusy);

		answers.Fail("", new InvalidOperationException("provider failed"));
		host.Dispatcher.PumpUntilIdle();

		Assert.IsFalse(host.Controller.State.IsBusy);
	}

	[TestMethod]
	public void Query_MoreItemsThanTheBound_ShowsTheBoundedNumber()
	{
		ScriptedQuickPickProvider provider = ProviderOf("", [.. Enumerable.Range(0, 20).Select(index => $"i{index}")]);
		using var host = new QuickPickTestHost(provider);

		host.CreateController(QuickPickControllerOptions.Default with { MaxItemCount = 5 }).Open();

		Assert.HasCount(5, host.Keys);
	}

	[TestMethod]
	public void Query_MoreItemsThanTheOffThreadBound_ShowsTheRankedRowsOnceTheRankingReturnsToTheDispatcher()
	{
		ScriptedQuickPickProvider provider = ProviderOf("", [.. Enumerable.Range(0, 800).Select(index => $"item{index}")]);
		using var host = new QuickPickTestHost(provider);

		host.CreateController(QuickPickControllerOptions.Default with { MaxItemCount = 5 }).Open("item79");

		// The ranking of a long list runs on a thread of the pool, and its result is published from the dispatcher.
		host.Dispatcher.PumpUntil(() => host.Controller.State.Rows.Count > 0);

		Assert.HasCount(5, host.Keys);
		Assert.AreEqual("item79", host.Keys[0]);
	}

	[TestMethod]
	public void Open_ForAProviderWithARecentGroup_PassesTheRecentKeysInTheRequest()
	{
		ScriptedQuickPickProvider provider = ProviderOf(">", "a", "b");
		provider.RecentGroup = new QuickPickGroup("recent", "recently used");

		var store = new RecordingQuickPickRecentStore(new Dictionary<string, IReadOnlyList<string>> { [">"] = ["b"] });
		using var host = new QuickPickTestHost(provider);

		host.CreateController(recentStore: store).Open(">");

		Assert.AreSequenceEqual(["b"], [.. provider.Requests[0].RecentKeys]);
	}

	[TestMethod]
	public void Open_ForAProviderWithoutARecentGroup_PassesNoRecentKeys()
	{
		ScriptedQuickPickProvider provider = ProviderOf(">", "a");

		var store = new RecordingQuickPickRecentStore(new Dictionary<string, IReadOnlyList<string>> { [">"] = ["a"] });
		using var host = new QuickPickTestHost(provider);

		host.CreateController(recentStore: store).Open(">");

		Assert.IsEmpty(provider.Requests[0].RecentKeys);
	}

	[TestMethod]
	public void Open_TheRecentStoreThrows_ListsTheItemsWithoutRecency()
	{
		ScriptedQuickPickProvider provider = ProviderOf(">", "a", "b");
		provider.RecentGroup = new QuickPickGroup("recent", "recently used");
		using var host = new QuickPickTestHost(provider);

		host.CreateController(recentStore: new ThrowingRecentStore()).Open(">");

		Assert.AreSequenceEqual(["a", "b"], host.Keys);
		Assert.IsNotEmpty(host.Faults);
	}

	private sealed class ThrowingRecentStore : IQuickPickRecentStore
	{
		public IReadOnlyDictionary<string, IReadOnlyList<string>> Load() => throw new InvalidOperationException("store failed");
		public bool Save(IReadOnlyDictionary<string, IReadOnlyList<string>> keysByPrefix) => throw new InvalidOperationException("store failed");
	}
}
