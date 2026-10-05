namespace Nickelony.QuickPick.Tests;

public sealed partial class QuickPickControllerTests
{
	[TestMethod]
	public void Open_PublishesAnOpenStateWithTheText()
	{
		ScriptedQuickPickProvider provider = ProviderOf(">", "a");
		using var host = new QuickPickTestHost(provider);

		host.Controller.Open(">x");

		Assert.IsTrue(host.States[0].IsOpen);
		Assert.AreEqual(">x", host.States[0].Text);
		Assert.IsEmpty(host.States[0].Rows);
	}

	[TestMethod]
	public void Open_WhileOpen_ReplacesTheSession()
	{
		ScriptedQuickPickProvider provider = ProviderOf("", "a", "b");
		using var host = new QuickPickTestHost(provider);

		host.Controller.Open("a");
		host.Controller.MoveActive(1);
		host.Controller.Open("b");

		Assert.AreEqual("b", host.Controller.State.Text);
		Assert.HasCount(2, provider.Requests);
	}

	[TestMethod]
	public void Dismiss_ClosesTheSessionAndRaisesChangedOnce()
	{
		ScriptedQuickPickProvider provider = ProviderOf("", "a");
		using var host = new QuickPickTestHost(provider);

		host.Controller.Open();
		int before = host.States.Count;

		host.Controller.Dismiss();
		host.Controller.Dismiss();

		Assert.HasCount(before + 1, host.States);
		Assert.AreSame(QuickPickPresentationState.Closed, host.Controller.State);
	}

	[TestMethod]
	public void Dismiss_ThenOpen_StartsAFreshSession()
	{
		ScriptedQuickPickProvider files = ProviderOf("", "a");
		ScriptedQuickPickProvider commands = ProviderOf(">", "b");

		using var host = new QuickPickTestHost(files, commands);

		host.Controller.Open(">");
		host.Controller.Dismiss();
		host.Controller.Open();

		Assert.AreEqual(string.Empty, host.Controller.State.Text);
		Assert.AreSequenceEqual(["a"], host.Keys);
	}

	[TestMethod]
	public void Changed_AHandlerThrows_IsContainedAndReported()
	{
		ScriptedQuickPickProvider provider = ProviderOf("", "a");
		using var host = new QuickPickTestHost(provider);

		host.Controller.Changed += (_, _) => throw new InvalidOperationException("handler failed");
		host.Controller.Open();

		Assert.IsTrue(host.Controller.State.IsOpen);
		Assert.IsNotEmpty(host.Faults);
	}

	[TestMethod]
	public void Members_OffTheDispatcherThread_Throw()
	{
		ScriptedQuickPickProvider provider = ProviderOf("", "a");
		using var host = new QuickPickTestHost(provider);
		QuickPickController controller = host.Controller;
		var failures = new List<Exception>();

		var thread = new Thread(() =>
		{
			Probe(() => controller.Open());
			Probe(() => controller.SetText("a"));
			Probe(() => controller.Refresh());
			Probe(() => controller.MoveActive(1));
			Probe(() => controller.SetActiveRow(0));
			Probe(() => controller.AcceptAsync());
			Probe(() => controller.Dismiss());

			void Probe(Action action)
			{
				try
				{
					action();
				}
				catch (Exception exception)
				{
					failures.Add(exception);
				}
			}
		});

		thread.Start();
		thread.Join();

		Assert.HasCount(7, failures);
		Assert.IsTrue(failures.All(failure => failure is InvalidOperationException));
	}

	[TestMethod]
	public void Members_AfterDisposal_DoNothing()
	{
		ScriptedQuickPickProvider provider = ProviderOf("", "a");
		using var host = new QuickPickTestHost(provider);
		QuickPickController controller = host.Controller;

		controller.Dispose();
		controller.Dispose();

		controller.Open();
		controller.SetText("a");
		controller.Refresh();
		controller.Dismiss();

		Assert.IsFalse(controller.MoveActive(1));
		Assert.IsFalse(controller.SetActiveRow(0));
		Assert.IsFalse(controller.AcceptAsync().Result);
		Assert.IsEmpty(provider.Requests);
		Assert.IsFalse(controller.State.IsOpen);
	}

	[TestMethod]
	public void Dispose_WhileOpen_PublishesTheClosedStateAndDropsTheAnswer()
	{
		ScriptedQuickPickProvider provider = ProviderOf("");

		var answers = new GatedAnswers(provider);
		using var host = new QuickPickTestHost(provider);

		QuickPickController controller = host.Controller;

		controller.Open();
		controller.Dispose();

		Assert.IsTrue(provider.Tokens[0].IsCancellationRequested);
		Assert.IsFalse(controller.State.IsOpen);

		int published = host.States.Count;

		answers.Release("", "late");
		host.Dispatcher.PumpUntilIdle();

		Assert.HasCount(published, host.States);
	}

	[TestMethod]
	public void Dispose_WhileTheProviderAccepts_CancelsTheTokenItWasGiven()
	{
		ScriptedQuickPickProvider provider = ProviderOf("", "a");

		var release = new TaskCompletionSource();
		using var host = new QuickPickTestHost(provider);

		provider.OnAccept = _ => release.Task;

		host.Controller.Open();
		Task<bool> accept = host.Controller.AcceptAsync();

		Assert.IsFalse(provider.AcceptTokens[0].IsCancellationRequested);

		host.Controller.Dispose();

		Assert.IsTrue(provider.AcceptTokens[0].IsCancellationRequested);

		release.SetResult();
		host.Dispatcher.PumpUntil(() => accept.IsCompleted);

		Assert.IsTrue(accept.Result);
	}
}
