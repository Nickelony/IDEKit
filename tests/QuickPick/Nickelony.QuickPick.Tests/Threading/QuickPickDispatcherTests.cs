namespace Nickelony.QuickPick.Tests;

/// <summary>
/// Pins what the controller relies on from its dispatcher: the creating thread owns it and its current context is the one route back, and a callback
/// scheduled for later reaches the owner thread through that context unless its handle was disposed first. A callback posted at once reaches
/// the owner thread the same way, unless the context rejects the post.
/// </summary>
[TestClass]
public sealed class QuickPickDispatcherTests
{
	private static readonly TimeSpan s_delay = TimeSpan.FromMilliseconds(50.0);

	[TestMethod]
	public void Constructor_WithoutASynchronizationContext_Throws()
	{
		using var scope = new ContextScope(null);
		Assert.ThrowsExactly<InvalidOperationException>(() => new QuickPickDispatcher(TimeProvider.System));
	}

	[TestMethod]
	public void Constructor_OnlyTheBaseSynchronizationContext_Throws()
	{
		using var scope = new ContextScope(new SynchronizationContext());
		Assert.ThrowsExactly<InvalidOperationException>(() => new QuickPickDispatcher(TimeProvider.System));
	}

	[TestMethod]
	public void CheckAccess_OnTheCreatingThread_IsTrueAndOnAnotherThreadFalse()
	{
		using var scope = new ContextScope(new RecordingSynchronizationContext());
		var dispatcher = new QuickPickDispatcher(TimeProvider.System);

		bool onAnotherThread = true;

		RunOnDedicatedThread(() => onAnotherThread = dispatcher.CheckAccess());

		Assert.IsTrue(dispatcher.CheckAccess());
		Assert.IsFalse(onAnotherThread);
	}

	[TestMethod]
	public void VerifyAccess_OnAnotherThread_ThrowsInvalidOperationException()
	{
		using var scope = new ContextScope(new RecordingSynchronizationContext());
		var dispatcher = new QuickPickDispatcher(TimeProvider.System);

		Exception? failure = null;

		dispatcher.VerifyAccess();

		RunOnDedicatedThread(() =>
		{
			try
			{
				dispatcher.VerifyAccess();
			}
			catch (Exception exception)
			{
				failure = exception;
			}
		});

		Assert.IsInstanceOfType<InvalidOperationException>(failure);
	}

	[TestMethod]
	public void Schedule_AfterTheDelay_PostsThroughTheContextAndRunsTheCallbackWhenTheContextRunsIt()
	{
		var context = new RecordingSynchronizationContext();
		using var scope = new ContextScope(context);
		var time = new FakeTimeProvider();
		var dispatcher = new QuickPickDispatcher(time);

		int ran = 0;

		using IDisposable handle = dispatcher.Schedule(s_delay, () => ran++);

		time.Advance(s_delay - TimeSpan.FromMilliseconds(1.0));

		Assert.AreEqual(0, context.PostedCount);

		time.Advance(TimeSpan.FromMilliseconds(1.0));

		Assert.AreEqual(1, context.PostedCount);
		Assert.AreEqual(0, ran);

		context.RunPosted();

		Assert.AreEqual(1, ran);
	}

	[TestMethod]
	public void Schedule_AfterTheContextOfTheThreadChanged_StillPostsThroughTheContextOfTheCreation()
	{
		var created = new RecordingSynchronizationContext();
		var replacement = new RecordingSynchronizationContext();
		var time = new FakeTimeProvider();
		using var scope = new ContextScope(created);
		var dispatcher = new QuickPickDispatcher(time);

		SynchronizationContext.SetSynchronizationContext(replacement);

		using IDisposable handle = dispatcher.Schedule(s_delay, static () => { });

		time.Advance(s_delay);

		Assert.AreEqual(1, created.PostedCount);
		Assert.AreEqual(0, replacement.PostedCount);
	}

	[TestMethod]
	public void Schedule_DisposedBeforeTheTimerFires_PostsNothing()
	{
		var context = new RecordingSynchronizationContext();
		using var scope = new ContextScope(context);
		var time = new FakeTimeProvider();
		var dispatcher = new QuickPickDispatcher(time);

		int ran = 0;

		dispatcher.Schedule(s_delay, () => ran++).Dispose();
		time.Advance(s_delay);
		context.RunPosted();

		Assert.AreEqual(0, context.PostedCount);
		Assert.AreEqual(0, ran);
	}

	[TestMethod]
	public void Schedule_DisposedAfterTheTimerFiredAndBeforeThePostRan_RunsNothing()
	{
		var context = new RecordingSynchronizationContext();
		using var scope = new ContextScope(context);
		var time = new FakeTimeProvider();
		var dispatcher = new QuickPickDispatcher(time);

		int ran = 0;

		IDisposable handle = dispatcher.Schedule(s_delay, () => ran++);

		time.Advance(s_delay);

		Assert.AreEqual(1, context.PostedCount);

		handle.Dispose();
		context.RunPosted();

		// The tick is already queued on the owner thread when the handle is disposed, and it still runs nothing.
		Assert.AreEqual(0, ran);
	}

	[TestMethod]
	public void Schedule_TheContextRejectsThePost_KeepsTheFaultOffTheTimerThread()
	{
		var context = new RecordingSynchronizationContext { PostFault = new InvalidOperationException("The dispatcher shut down.") };
		using var scope = new ContextScope(context);
		var time = new FakeTimeProvider();
		var dispatcher = new QuickPickDispatcher(time);

		int ran = 0;

		using IDisposable handle = dispatcher.Schedule(s_delay, () => ran++);

		time.Advance(s_delay);

		Assert.AreEqual(0, ran);
	}

	[TestMethod]
	public void TryPost_OnAnotherThread_PostsThroughTheContextAndRunsTheCallbackWhenTheContextRunsIt()
	{
		var context = new RecordingSynchronizationContext();
		using var scope = new ContextScope(context);
		var dispatcher = new QuickPickDispatcher(TimeProvider.System);

		int ran = 0;
		bool posted = false;

		RunOnDedicatedThread(() => posted = dispatcher.TryPost(() => ran++));

		Assert.IsTrue(posted);
		Assert.AreEqual(1, context.PostedCount);
		Assert.AreEqual(0, ran);

		context.RunPosted();

		Assert.AreEqual(1, ran);
	}

	[TestMethod]
	public void TryPost_TheContextRejectsThePost_ReportsFalseAndRunsNothing()
	{
		var context = new RecordingSynchronizationContext { PostFault = new InvalidOperationException("The dispatcher shut down.") };
		using var scope = new ContextScope(context);
		var dispatcher = new QuickPickDispatcher(TimeProvider.System);

		int ran = 0;

		Assert.IsFalse(dispatcher.TryPost(() => ran++));
		Assert.AreEqual(0, ran);
	}

	private static void RunOnDedicatedThread(Action action)
	{
		var thread = new Thread(() => action());

		thread.Start();
		thread.Join();
	}

	// Makes a context the current one of the test thread and puts the previous one back, because the test host reuses its threads.
	private sealed class ContextScope : IDisposable
	{
		private readonly SynchronizationContext? _previous = SynchronizationContext.Current;

		public ContextScope(SynchronizationContext? context) => SynchronizationContext.SetSynchronizationContext(context);

		public void Dispose() => SynchronizationContext.SetSynchronizationContext(_previous);
	}
}
