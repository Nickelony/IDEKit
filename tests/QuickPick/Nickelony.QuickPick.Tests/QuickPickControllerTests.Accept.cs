namespace Nickelony.QuickPick.Tests;

public sealed partial class QuickPickControllerTests
{
	[TestMethod]
	public void AcceptAsync_TheActiveItem_ClosesTheSessionAndCallsTheProviderOnce()
	{
		ScriptedQuickPickProvider provider = ProviderOf("", "a", "b");
		using var host = new QuickPickTestHost(provider);

		host.Controller.Open();
		host.Controller.MoveActive(1);

		Assert.IsTrue(host.Accept());

		Assert.HasCount(1, provider.Accepted);
		Assert.AreEqual("b", provider.Accepted[0].Key);
		Assert.IsFalse(host.Controller.State.IsOpen);
		Assert.AreSame(QuickPickPresentationState.Closed, host.Controller.State);
	}

	[TestMethod]
	public void AcceptAsync_TheProviderRunsAfterTheSessionIsClosed()
	{
		ScriptedQuickPickProvider provider = ProviderOf("", "a");
		bool? wasOpenInAccept = null;

		using var host = new QuickPickTestHost(provider);

		provider.OnAccept = _ =>
		{
			wasOpenInAccept = host.Controller.State.IsOpen;
			return Task.CompletedTask;
		};

		host.Controller.Open();
		host.Accept();

		Assert.IsFalse(wasOpenInAccept);
	}

	[TestMethod]
	public void AcceptAsync_CalledTwiceInARow_AcceptsOnce()
	{
		ScriptedQuickPickProvider provider = ProviderOf("", "a");
		var release = new TaskCompletionSource();
		provider.OnAccept = _ => release.Task;
		using var host = new QuickPickTestHost(provider);

		host.Controller.Open();

		Task<bool> first = host.Controller.AcceptAsync();
		Task<bool> second = host.Controller.AcceptAsync();

		// The first call is inside the provider's accept; the second finds the session closed.
		Assert.IsFalse(first.IsCompleted);
		Assert.IsTrue(second.IsCompleted);
		Assert.IsFalse(second.Result);

		release.SetResult();
		host.Dispatcher.PumpUntil(() => first.IsCompleted);

		Assert.IsTrue(first.Result);
		Assert.HasCount(1, provider.Accepted);
	}

	[TestMethod]
	public void AcceptAsync_CalledTwiceWhileWaitingForTheTypedAnswer_AcceptsOnce()
	{
		ScriptedQuickPickProvider provider = ProviderOf("", "a");

		var answers = new GatedAnswers(provider);
		using var host = new QuickPickTestHost(provider);

		host.Controller.Open();
		answers.Release("", "a");
		host.Dispatcher.PumpUntilIdle();
		host.Controller.SetText("b");

		Task<bool> first = host.Controller.AcceptAsync();
		Task<bool> second = host.Controller.AcceptAsync();

		Assert.IsFalse(first.IsCompleted);
		Assert.IsTrue(second.IsCompleted);
		Assert.IsFalse(second.Result);

		answers.Release("b", "b1");
		host.Dispatcher.PumpUntil(() => first.IsCompleted);

		Assert.IsTrue(first.Result);
		Assert.HasCount(1, provider.Accepted);
		Assert.AreEqual("b1", provider.Accepted[0].Key);
	}

	[TestMethod]
	public void AcceptAsync_AfterTyping_WaitsForTheAnswerToTheTypedText()
	{
		ScriptedQuickPickProvider provider = ProviderOf("");

		var answers = new GatedAnswers(provider);
		using var host = new QuickPickTestHost(provider);

		host.Controller.Open();
		answers.Release("", "old");
		host.Dispatcher.PumpUntilIdle();
		Assert.AreEqual("old", host.Controller.State.ActiveItem!.Key);

		host.Controller.SetText("n");

		// The debounce has not elapsed, yet accepting asks the provider for the typed text and waits for its answer.
		Task<bool> accept = host.Controller.AcceptAsync();

		Assert.IsTrue(answers.IsHeld("n"));
		Assert.IsFalse(accept.IsCompleted);
		Assert.IsEmpty(provider.Accepted);

		answers.Release("n", "new");
		host.Dispatcher.PumpUntil(() => accept.IsCompleted);

		Assert.IsTrue(accept.Result);
		Assert.AreEqual("new", provider.Accepted[0].Key);
	}

	[TestMethod]
	public void AcceptAsync_TheSessionIsDismissedWhileWaiting_AcceptsNothing()
	{
		ScriptedQuickPickProvider provider = ProviderOf("");

		var answers = new GatedAnswers(provider);
		using var host = new QuickPickTestHost(provider);

		host.Controller.Open();
		Task<bool> accept = host.Controller.AcceptAsync();

		host.Controller.Dismiss();
		answers.Release("", "late");
		host.Dispatcher.PumpUntil(() => accept.IsCompleted);

		Assert.IsFalse(accept.Result);
		Assert.IsEmpty(provider.Accepted);
	}

	[TestMethod]
	public void AcceptAsync_NoSessionIsOpen_ReturnsFalse()
	{
		ScriptedQuickPickProvider provider = ProviderOf("", "a");
		using var host = new QuickPickTestHost(provider);

		Assert.IsFalse(host.Accept());
		Assert.IsEmpty(provider.Accepted);
	}

	[TestMethod]
	public void AcceptAsync_NoItemIsActive_ReturnsFalseAndKeepsTheSessionOpen()
	{
		ScriptedQuickPickProvider provider = ProviderOf("");
		provider.Items.Add(ScriptedQuickPickProvider.Item("off", isEnabled: false));
		using var host = new QuickPickTestHost(provider);

		host.Controller.Open();

		Assert.IsFalse(host.Accept());
		Assert.IsTrue(host.Controller.State.IsOpen);
		Assert.IsEmpty(provider.Accepted);
	}

	[TestMethod]
	public void AcceptAsync_ForAProviderWithARecentGroup_RecordsTheKeyAndListsItFirstNextTime()
	{
		ScriptedQuickPickProvider provider = ProviderOf(">", "a", "b", "c");
		provider.RecentGroup = new QuickPickGroup("recent", "recently used");

		using var host = new QuickPickTestHost(provider);
		var store = new RecordingQuickPickRecentStore();

		QuickPickController controller = host.CreateController(recentStore: store);

		controller.Open(">");
		controller.SetActiveRow(2);
		Assert.IsTrue(host.Accept());

		Assert.AreSequenceEqual(["c"], [.. store.KeysOf(">")]);

		controller.Open(">");

		Assert.AreSequenceEqual(["--recently used--", "c", "a", "b"], host.RowTexts);
		Assert.AreEqual("c", controller.State.ActiveItem!.Key);
	}

	[TestMethod]
	public void AcceptAsync_ForAProviderWithoutARecentGroup_RecordsNothing()
	{
		ScriptedQuickPickProvider provider = ProviderOf(">", "a");

		using var host = new QuickPickTestHost(provider);
		var store = new RecordingQuickPickRecentStore();

		host.CreateController(recentStore: store).Open(">");
		host.Accept();

		Assert.IsEmpty(store.Saves);
	}

	[TestMethod]
	public void AcceptAsync_AnItemOfTheListOfModes_RecordsNothing()
	{
		ScriptedQuickPickProvider provider = ProviderOf(">", "a");
		provider.RecentGroup = new QuickPickGroup("recent");

		using var host = new QuickPickTestHost(provider);
		var store = new RecordingQuickPickRecentStore();

		host.CreateController(recentStore: store).Open("?");
		host.Accept();

		Assert.IsEmpty(store.Saves);
	}

	[TestMethod]
	public void AcceptAsync_TheProviderThrows_IsContainedAndReported()
	{
		ScriptedQuickPickProvider provider = ProviderOf("", "a");
		provider.OnAccept = _ => throw new InvalidOperationException("accept failed");
		using var host = new QuickPickTestHost(provider);

		host.Controller.Open();

		Assert.IsTrue(host.Accept());
		Assert.HasCount(1, host.Faults);
		Assert.AreEqual("accept failed", host.Faults[0].Message);
		Assert.IsFalse(host.Controller.State.IsOpen);
	}

	[TestMethod]
	public void AcceptAsync_TheStoreThrows_StillAcceptsTheItem()
	{
		ScriptedQuickPickProvider provider = ProviderOf(">", "a");
		provider.RecentGroup = new QuickPickGroup("recent");
		using var host = new QuickPickTestHost(provider);

		host.CreateController(recentStore: new ThrowingRecentStore()).Open(">");

		Assert.IsTrue(host.Accept());
		Assert.HasCount(1, provider.Accepted);
		Assert.IsNotEmpty(host.Faults);
	}

	[TestMethod]
	public void AcceptAsync_TheProviderOpensTheNextSession_StartsItWithoutInterference()
	{
		ScriptedQuickPickProvider files = ProviderOf("", "a");
		ScriptedQuickPickProvider commands = ProviderOf(">", "go");

		using var host = new QuickPickTestHost(files, commands);

		commands.OnAccept = _ =>
		{
			host.Controller.Open();
			return Task.CompletedTask;
		};

		host.Controller.Open(">");
		Assert.IsTrue(host.Accept());

		Assert.IsTrue(host.Controller.State.IsOpen);
		Assert.AreSequenceEqual(["a"], host.Keys);
	}
}
