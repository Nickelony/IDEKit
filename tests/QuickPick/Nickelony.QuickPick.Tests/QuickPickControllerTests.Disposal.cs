using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;

namespace Nickelony.QuickPick.Tests;

public sealed partial class QuickPickControllerTests
{
	[TestMethod]
	public void Dispose_OnAnotherThread_RunsTheTeardownOnTheDispatcherThreadOnlyWhenItPumps()
	{
		ScriptedQuickPickProvider provider = ProviderOf("", "a");

		using var host = new QuickPickTestHost(provider);
		var changedThreads = new List<int>();

		host.Controller.Changed += (_, _) => changedThreads.Add(Environment.CurrentManagedThreadId);
		host.Open();
		host.Dispatcher.PumpUntilIdle();

		int published = host.States.Count;

		changedThreads.Clear();
		RunOnAnotherThread(host.Controller.Dispose);

		Assert.HasCount(published, host.States);
		Assert.IsEmpty(changedThreads);
		Assert.IsEmpty(provider.DeactivationLog);
		Assert.AreSequenceEqual(["preview:a"], provider.PreviewLog);

		host.Dispatcher.PumpUntilIdle();

		Assert.HasCount(published + 1, host.States);
		Assert.IsFalse(host.States[^1].IsOpen);
		Assert.AreSequenceEqual([host.Dispatcher.OwnerThreadId], changedThreads);
		Assert.AreSequenceEqual([host.Dispatcher.OwnerThreadId], provider.DeactivationThreadIds);
		Assert.AreSequenceEqual(["preview:a", "cancel"], provider.PreviewLog);
		Assert.AreEqual(host.Dispatcher.OwnerThreadId, provider.PreviewThreadIds[^1]);
	}

	[TestMethod]
	public void Dispose_OnAnotherThread_CancelsTheQueryInFlightAndMakesEveryMemberInertAtOnce()
	{
		ScriptedQuickPickProvider provider = ProviderOf("");

		var answers = new GatedAnswers(provider);
		using var host = new QuickPickTestHost(provider);

		QuickPickController controller = host.Controller;

		controller.Open();

		Assert.IsFalse(provider.Tokens[0].IsCancellationRequested);

		int published = host.States.Count;

		RunOnAnotherThread(controller.Dispose);

		// The dispatcher thread has not pumped, so the teardown has not run.
		Assert.IsTrue(provider.Tokens[0].IsCancellationRequested);

		controller.Open("x");
		controller.SetText("y");
		controller.Refresh();
		controller.Dismiss();

		Assert.IsFalse(controller.MoveActive(1));
		Assert.IsFalse(controller.SetActiveRow(0));
		Assert.IsFalse(controller.AcceptAsync().Result);
		Assert.HasCount(1, provider.Requests);
		Assert.HasCount(published, host.States);

		answers.Release("", "late");
		host.Dispatcher.PumpUntilIdle();

		// Only the closed state of the teardown is published, and the late answer is dropped.
		Assert.HasCount(published + 1, host.States);
		Assert.IsFalse(host.States[^1].IsOpen);
		Assert.HasCount(1, provider.Requests);
	}

	[TestMethod]
	public void Dispose_OnAnotherThreadWhileTheProviderAccepts_CancelsTheTokenItWasGivenAtOnce()
	{
		ScriptedQuickPickProvider provider = ProviderOf("", "a");

		var release = new TaskCompletionSource();
		using var host = new QuickPickTestHost(provider);

		provider.OnAccept = _ => release.Task;

		host.Controller.Open();
		Task<bool> accept = host.Controller.AcceptAsync();

		Assert.IsFalse(provider.AcceptTokens[0].IsCancellationRequested);

		RunOnAnotherThread(host.Controller.Dispose);

		Assert.IsTrue(provider.AcceptTokens[0].IsCancellationRequested);

		release.SetResult();
		host.Dispatcher.PumpUntil(() => accept.IsCompleted);

		Assert.IsTrue(accept.Result);
	}

	[TestMethod]
	public void Dispose_FromTwoThreadsAtOnce_TearsDownOnce()
	{
		ScriptedQuickPickProvider provider = ProviderOf("", "a");
		using var host = new QuickPickTestHost(provider);

		host.Open();
		host.Dispatcher.PumpUntilIdle();

		int published = host.States.Count;

		RunOnAnotherThreads(count: 2, host.Controller.Dispose);
		host.Dispatcher.PumpUntilIdle();

		Assert.HasCount(published + 1, host.States);
		Assert.HasCount(1, provider.DeactivationLog);
		Assert.AreSequenceEqual(["preview:a", "cancel"], provider.PreviewLog);
	}

	[TestMethod]
	public void Dispose_AgainOnAnotherThread_PostsNothingMore()
	{
		ScriptedQuickPickProvider provider = ProviderOf("", "a");
		using var host = new QuickPickTestHost(provider);

		host.Open();
		host.Dispatcher.PumpUntilIdle();

		RunOnAnotherThread(host.Controller.Dispose);

		Assert.AreEqual(1, host.Dispatcher.QueuedCount);

		RunOnAnotherThread(host.Controller.Dispose);

		Assert.AreEqual(1, host.Dispatcher.QueuedCount);
	}

	[TestMethod]
	public void Dispose_OnTheDispatcherThreadAfterAnotherThreadPostedTheTeardown_FinishesItAtOnceAndTheRestRunsNothing()
	{
		ScriptedQuickPickProvider provider = ProviderOf("", "a");
		using var host = new QuickPickTestHost(provider);

		host.Open();
		host.Dispatcher.PumpUntilIdle();

		int published = host.States.Count;

		RunOnAnotherThread(host.Controller.Dispose);
		host.Controller.Dispose();

		// The dispatcher thread did not pump, yet its own disposal ended the session.
		Assert.HasCount(published + 1, host.States);
		Assert.IsFalse(host.States[^1].IsOpen);
		Assert.HasCount(1, provider.DeactivationLog);
		Assert.AreSequenceEqual(["preview:a", "cancel"], provider.PreviewLog);

		host.Dispatcher.PumpUntilIdle();

		Assert.HasCount(published + 1, host.States);
		Assert.HasCount(1, provider.DeactivationLog);
		Assert.AreSequenceEqual(["preview:a", "cancel"], provider.PreviewLog);
	}

	[TestMethod]
	public void Dispose_OnAnotherThreadAndAModeFailsToDeactivate_ReportsTheFaultOnTheDispatcherThread()
	{
		ScriptedQuickPickProvider provider = ProviderOf("", "a");

		provider.DeactivationFault = new InvalidOperationException("release failed");

		using var host = new QuickPickTestHost(provider);
		var faultThreads = new List<int>();

		QuickPickController controller = new(
			Modes(provider),
			reportFault: _ => faultThreads.Add(Environment.CurrentManagedThreadId),
			timeProvider: host.Dispatcher.TimeProvider
		);

		controller.Open();
		host.Dispatcher.PumpUntilIdle();

		RunOnAnotherThread(controller.Dispose);

		Assert.IsEmpty(faultThreads);

		host.Dispatcher.PumpUntilIdle();

		Assert.AreSequenceEqual([host.Dispatcher.OwnerThreadId], faultThreads);
	}

	[TestMethod]
	public void Dispose_OnAnotherThreadWhenTheContextRejectsThePost_DropsTheTeardownWithoutAFault()
	{
		ScriptedQuickPickProvider provider = ProviderOf("", "a");

		var context = new RecordingSynchronizationContext();
		var faults = new List<Exception>();

		SynchronizationContext? previous = SynchronizationContext.Current;
		QuickPickController controller;

		SynchronizationContext.SetSynchronizationContext(context);

		try
		{
			controller = new QuickPickController(Modes(provider), reportFault: faults.Add, timeProvider: new FakeTimeProvider());
			controller.Open();
		}
		finally
		{
			SynchronizationContext.SetSynchronizationContext(previous);
		}

		context.RunPosted();
		context.PostFault = new InvalidOperationException("The dispatcher shut down.");

		RunOnAnotherThread(controller.Dispose);

		Assert.AreEqual(0, context.PostedCount);
		Assert.IsEmpty(faults);
		Assert.IsEmpty(provider.DeactivationLog);

		// The controller is disposed all the same.
		int requests = provider.Requests.Count;

		controller.Open();

		Assert.HasCount(requests, provider.Requests);
	}

	private static void RunOnAnotherThread(Action action) => RunOnAnotherThreads(count: 1, action);

	// Starts the threads together and joins them, then rethrows the first failure, so a failing call fails the test instead of ending the process.
	private static void RunOnAnotherThreads(int count, Action action)
	{
		using var start = new Barrier(count);
		var failures = new ConcurrentQueue<Exception>();

		Thread[] threads =
		[
			.. Enumerable
				.Range(0, count)
				.Select(_ => new Thread(() =>
				{
					start.SignalAndWait();

					try
					{
						action();
					}
					catch (Exception exception)
					{
						failures.Enqueue(exception);
					}
				})),
		];

		foreach (Thread thread in threads)
			thread.Start();

		foreach (Thread thread in threads)
			thread.Join();

		if (failures.TryDequeue(out Exception? failure))
			ExceptionDispatchInfo.Capture(failure).Throw();
	}
}
