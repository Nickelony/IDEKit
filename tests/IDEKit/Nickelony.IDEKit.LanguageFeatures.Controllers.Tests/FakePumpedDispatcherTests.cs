using System.Diagnostics;
using Nickelony.IDEKit.Core.Threading;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Tests;

/// <summary>Verifies the pumped fake dispatcher that the controller tests rely on: affinity, ordering, the virtual clock, and the contexts.</summary>
[TestClass]
public sealed class FakePumpedDispatcherTests
{
	private static readonly TimeSpan s_waitLimit = TimeSpan.FromSeconds(5.0);

	[TestMethod]
	public void CheckAccess_OnTheOwnerThread_IsTrueAndOnAnotherThreadFalse()
	{
		using var dispatcher = new FakePumpedDispatcher(installSynchronizationContext: false);

		bool onOwner = dispatcher.CheckAccess();
		bool onAnotherThread = RunOnDedicatedThread(dispatcher.CheckAccess);

		Assert.IsTrue(onOwner);
		Assert.IsFalse(onAnotherThread);
		Assert.AreEqual(Environment.CurrentManagedThreadId, dispatcher.OwnerThreadId);
	}

	[TestMethod]
	public void VerifyAccess_OnAnotherThread_ThrowsInvalidOperationException()
	{
		using var dispatcher = new FakePumpedDispatcher(installSynchronizationContext: false);

		dispatcher.VerifyAccess();

		Exception? failure = RunOnDedicatedThread(() => CaptureFailure(dispatcher.VerifyAccess));

		Assert.IsInstanceOfType<InvalidOperationException>(failure);
	}

	[TestMethod]
	public void Invoke_OnTheOwnerThread_RunsInlineAndReturnsTheResult()
	{
		using var dispatcher = new FakePumpedDispatcher(installSynchronizationContext: false);

		bool ran = false;

		dispatcher.Invoke(() =>
		{
			ran = true;
		});

		int result = dispatcher.Invoke(static () => 42);

		Assert.IsTrue(ran);
		Assert.AreEqual(42, result);
		Assert.AreEqual(0, dispatcher.QueuedCount);
	}

	[TestMethod]
	public void Invoke_OnAnotherThread_BlocksUntilTheOwnerPumps()
	{
		using var dispatcher = new FakePumpedDispatcher(installSynchronizationContext: false);

		int runThreadId = 0;

		Task<bool> caller = StartOnDedicatedThread(() =>
		{
			dispatcher.Invoke(() =>
			{
				runThreadId = Environment.CurrentManagedThreadId;
			});

			return true;
		});

		Assert.IsTrue(SpinWait.SpinUntil(() => dispatcher.QueuedCount == 1, s_waitLimit));
		Assert.IsFalse(caller.IsCompleted);
		Assert.AreEqual(0, runThreadId);

		dispatcher.PumpUntil(() => caller.IsCompleted);

		Assert.AreEqual(dispatcher.OwnerThreadId, runThreadId);
	}

	[TestMethod]
	public void Invoke_OnAnotherThreadWithAResult_ReturnsItAfterTheOwnerPumps()
	{
		using var dispatcher = new FakePumpedDispatcher(installSynchronizationContext: false);

		Task<int> caller = StartOnDedicatedThread(() => dispatcher.Invoke(static () => Environment.CurrentManagedThreadId));

		Assert.IsTrue(SpinWait.SpinUntil(() => dispatcher.QueuedCount == 1, s_waitLimit));
		Assert.IsFalse(caller.IsCompleted);

		dispatcher.PumpUntil(() => caller.IsCompleted);

		Assert.AreEqual(dispatcher.OwnerThreadId, caller.GetAwaiter().GetResult());
	}

	[TestMethod]
	public void Invoke_OnAnotherThread_RethrowsTheCallbackFailureToTheCaller()
	{
		using var dispatcher = new FakePumpedDispatcher(installSynchronizationContext: false);

		Task<Exception?> caller = StartOnDedicatedThread(() =>
			CaptureFailure(() => dispatcher.Invoke(static () => throw new FormatException("Failed on the owner.")))
		);

		dispatcher.PumpUntil(() => caller.IsCompleted);

		Assert.IsInstanceOfType<FormatException>(caller.GetAwaiter().GetResult());
	}

	[TestMethod]
	public void Invoke_OnAnotherThread_RunsBeforeNormalWorkThatWasPostedEarlier()
	{
		using var dispatcher = new FakePumpedDispatcher(installSynchronizationContext: false);

		var order = new List<string>();

		dispatcher.Post(() => order.Add("posted"));

		Task<bool> caller = StartOnDedicatedThread(() =>
		{
			dispatcher.Invoke(() => order.Add("invoked"));
			return true;
		});

		Assert.IsTrue(SpinWait.SpinUntil(() => dispatcher.QueuedCount == 2, s_waitLimit));

		dispatcher.PumpUntil(() => caller.IsCompleted);
		dispatcher.PumpUntilIdle();

		// A blocking call outranks normal work, like the engines' send priority.
		Assert.AreSequenceEqual(["invoked", "posted"], order);
	}

	[TestMethod]
	public void InvokeAsync_OnTheOwnerThread_RunsInlineAndFaultsTheTaskOnAFailure()
	{
		using var dispatcher = new FakePumpedDispatcher(installSynchronizationContext: false);

		bool ran = false;

		Task completed = dispatcher.InvokeAsync(() => ran = true);
		Task faulted = dispatcher.InvokeAsync(static () => throw new FormatException("Failed inline."));

		Assert.IsTrue(ran);
		Assert.IsTrue(completed.IsCompletedSuccessfully);
		Assert.IsTrue(faulted.IsFaulted);
		Assert.IsInstanceOfType<FormatException>(faulted.Exception!.InnerException);
	}

	[TestMethod]
	public void InvokeAsync_OnAnotherThread_CompletesAfterThePump()
	{
		using var dispatcher = new FakePumpedDispatcher(installSynchronizationContext: false);

		int runThreadId = 0;

		Task invocation = RunOnDedicatedThread(() => dispatcher.InvokeAsync(() => runThreadId = Environment.CurrentManagedThreadId));

		Assert.IsFalse(invocation.IsCompleted);
		Assert.AreEqual(0, runThreadId);

		dispatcher.PumpUntil(() => invocation.IsCompleted);

		Assert.AreEqual(dispatcher.OwnerThreadId, runThreadId);
	}

	[TestMethod]
	public void Post_RunsOnlyWhenPumped_ByPriorityThenArrival()
	{
		using var dispatcher = new FakePumpedDispatcher(installSynchronizationContext: false);

		var order = new List<string>();

		dispatcher.Post(() => order.Add("idle"), UiDispatcherPriority.ContextIdle);
		dispatcher.Post(() => order.Add("background 1"), UiDispatcherPriority.Background);
		dispatcher.Post(() => order.Add("normal 1"));
		dispatcher.Post(() => order.Add("render"), UiDispatcherPriority.Render);
		dispatcher.Post(() => order.Add("background 2"), UiDispatcherPriority.Background);
		dispatcher.Post(() => order.Add("normal 2"), UiDispatcherPriority.Normal);

		Assert.AreEqual(6, dispatcher.QueuedCount);
		Assert.AreSequenceEqual([], order);

		dispatcher.PumpUntilIdle();

		Assert.AreSequenceEqual(["normal 1", "normal 2", "render", "background 1", "background 2", "idle"], order);
		Assert.AreEqual(0, dispatcher.QueuedCount);
	}

	[TestMethod]
	public void Post_CallbackFailure_PropagatesFromThePump()
	{
		using var dispatcher = new FakePumpedDispatcher(installSynchronizationContext: false);

		dispatcher.Post(static () => throw new FormatException("Failed in a posted callback."));

		Assert.ThrowsExactly<FormatException>(dispatcher.PumpUntilIdle);
		Assert.AreEqual(0, dispatcher.QueuedCount);
	}

	[TestMethod]
	public void Schedule_RunsAtBackgroundPriorityOnceTheClockReachesTheDelay()
	{
		using var dispatcher = new FakePumpedDispatcher(installSynchronizationContext: false);

		var order = new List<string>();

		dispatcher.Schedule(
			TimeSpan.FromMilliseconds(10.0),

			() =>
			{
				order.Add("first timer");

				dispatcher.Post(() => order.Add("idle"), UiDispatcherPriority.ContextIdle);
				dispatcher.Post(() => order.Add("normal"));
			}
		);

		dispatcher.Schedule(TimeSpan.FromMilliseconds(10.0), () => order.Add("second timer"));

		dispatcher.Advance(TimeSpan.FromMilliseconds(9.0));

		Assert.AreSequenceEqual([], order);
		Assert.AreEqual(2, dispatcher.PendingTimerCount);

		dispatcher.Advance(TimeSpan.FromMilliseconds(1.0));

		// Timers due at the same instant queue together at background priority: the work the first one posts at normal priority runs before
		// the second timer, and the context-idle work runs after both.
		Assert.AreSequenceEqual(["first timer", "normal", "second timer", "idle"], order);
		Assert.AreEqual(0, dispatcher.PendingTimerCount);
		Assert.AreEqual(TimeSpan.FromMilliseconds(10.0), dispatcher.Now);
	}

	[TestMethod]
	public void Schedule_DueTimers_RunInDueOrderThenInArrivalOrder()
	{
		using var dispatcher = new FakePumpedDispatcher(installSynchronizationContext: false);

		var order = new List<string>();

		dispatcher.Schedule(TimeSpan.FromMilliseconds(30.0), () => order.Add("late"));
		dispatcher.Schedule(TimeSpan.FromMilliseconds(10.0), () => order.Add("early 1"));
		dispatcher.Schedule(TimeSpan.FromMilliseconds(10.0), () => order.Add("early 2"));

		dispatcher.Advance(TimeSpan.FromMilliseconds(30.0));

		Assert.AreSequenceEqual(["early 1", "early 2", "late"], order);
	}

	[TestMethod]
	public void Schedule_CallbackThatSchedulesWithinTheWindow_RunsInTheSameAdvance()
	{
		using var dispatcher = new FakePumpedDispatcher(installSynchronizationContext: false);

		var order = new List<string>();

		dispatcher.Schedule(
			TimeSpan.FromMilliseconds(10.0),

			() =>
			{
				order.Add("first");
				dispatcher.Schedule(TimeSpan.FromMilliseconds(10.0), () => order.Add("second"));
			}
		);

		dispatcher.Advance(TimeSpan.FromMilliseconds(15.0));
		Assert.AreSequenceEqual(["first"], order);

		dispatcher.Advance(TimeSpan.FromMilliseconds(5.0));
		Assert.AreSequenceEqual(["first", "second"], order);
	}

	[TestMethod]
	public void Schedule_DisposedHandle_NeverRuns()
	{
		using var dispatcher = new FakePumpedDispatcher(installSynchronizationContext: false);

		bool ran = false;
		IDisposable handle = dispatcher.Schedule(TimeSpan.FromMilliseconds(1.0), () => ran = true);

		Assert.AreEqual(1, dispatcher.PendingTimerCount);

		handle.Dispose();
		handle.Dispose();

		dispatcher.Advance(TimeSpan.FromMilliseconds(100.0));

		Assert.IsFalse(ran);
		Assert.AreEqual(0, dispatcher.PendingTimerCount);
	}

	[TestMethod]
	public void Schedule_HandleDisposedAfterTheTickWasQueued_SuppressesTheTick()
	{
		using var dispatcher = new FakePumpedDispatcher(installSynchronizationContext: false);

		var order = new List<string>();
		IDisposable? secondHandle = null;

		dispatcher.Schedule(
			TimeSpan.FromMilliseconds(10.0),

			() =>
			{
				order.Add("first");
				secondHandle?.Dispose();
			}
		);

		secondHandle = dispatcher.Schedule(TimeSpan.FromMilliseconds(10.0), () => order.Add("second"));

		dispatcher.Advance(TimeSpan.FromMilliseconds(10.0));

		// Both ticks were due together and queued together; the first one disposed the second handle before its tick ran.
		Assert.AreSequenceEqual(["first"], order);
	}

	[TestMethod]
	public void Schedule_NullCallbackOrNegativeDelay_Throws()
	{
		using var dispatcher = new FakePumpedDispatcher(installSynchronizationContext: false);

		Assert.ThrowsExactly<ArgumentNullException>(() => dispatcher.Schedule(TimeSpan.Zero, null!));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => dispatcher.Schedule(TimeSpan.FromMilliseconds(-1.0), static () => { }));
	}

	[TestMethod]
	public void PumpUntil_LeavesTheClockAndTheTimersAlone()
	{
		using var dispatcher = new FakePumpedDispatcher(installSynchronizationContext: false);

		bool timerRan = false;
		bool posted = false;

		dispatcher.Schedule(TimeSpan.FromHours(1.0), () => timerRan = true);
		dispatcher.Post(() => posted = true);

		dispatcher.PumpUntil(() => posted);

		Assert.IsFalse(timerRan);
		Assert.AreEqual(1, dispatcher.PendingTimerCount);
		Assert.AreEqual(TimeSpan.Zero, dispatcher.Now);
	}

	[TestMethod]
	public void PumpUntilIdle_DoesNotMoveTheClock()
	{
		using var dispatcher = new FakePumpedDispatcher(installSynchronizationContext: false);

		bool ran = false;
		dispatcher.Schedule(TimeSpan.FromMilliseconds(1.0), () => ran = true);

		dispatcher.PumpUntilIdle();

		Assert.IsFalse(ran);
		Assert.AreEqual(TimeSpan.Zero, dispatcher.Now);
		Assert.AreEqual(1, dispatcher.PendingTimerCount);
	}

	[TestMethod]
	public void PumpUntil_ConditionThatNeverHolds_FailsAfterTheTimeout()
	{
		using var dispatcher = new FakePumpedDispatcher(installSynchronizationContext: false);
		Assert.ThrowsExactly<AssertFailedException>(() => dispatcher.PumpUntil(static () => false, TimeSpan.FromMilliseconds(30.0)));
	}

	[TestMethod]
	public void PumpUntil_WorkThatKeepsQueuingMoreWork_StillFailsAtTheTimeout()
	{
		using var dispatcher = new FakePumpedDispatcher(installSynchronizationContext: false);

		void Repost() => dispatcher.Post(Repost);

		dispatcher.Post(Repost);

		var stopwatch = Stopwatch.StartNew();

		Assert.ThrowsExactly<AssertFailedException>(() => dispatcher.PumpUntil(static () => false, TimeSpan.FromMilliseconds(100.0)));
		Assert.IsLessThan(TimeSpan.FromSeconds(3.0), stopwatch.Elapsed);
	}

	[TestMethod]
	public void PumpUntilIdle_WorkThatKeepsQueuingMoreWork_ThrowsInsteadOfHanging()
	{
		using var dispatcher = new FakePumpedDispatcher(installSynchronizationContext: false);

		void Repost() => dispatcher.Post(Repost);

		dispatcher.Post(Repost);

		Assert.ThrowsExactly<InvalidOperationException>(dispatcher.PumpUntilIdle);
	}

	[TestMethod]
	public void Advance_CallbacksThatKeepComingDue_ThrowInsteadOfHanging()
	{
		using var dispatcher = new FakePumpedDispatcher(installSynchronizationContext: false);

		void Reschedule() => dispatcher.Schedule(TimeSpan.Zero, Reschedule);

		dispatcher.Schedule(TimeSpan.Zero, Reschedule);

		Assert.ThrowsExactly<InvalidOperationException>(() => dispatcher.Advance(TimeSpan.FromMilliseconds(1.0)));
	}

	[TestMethod]
	public void Advance_NegativeDuration_ThrowsArgumentOutOfRangeException()
	{
		using var dispatcher = new FakePumpedDispatcher(installSynchronizationContext: false);
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => dispatcher.Advance(TimeSpan.FromMilliseconds(-1.0)));
	}

	[TestMethod]
	public void Pump_FromAnotherThread_ThrowsInvalidOperationException()
	{
		using var dispatcher = new FakePumpedDispatcher(installSynchronizationContext: false);

		Exception? idleFailure = RunOnDedicatedThread(() => CaptureFailure(dispatcher.PumpUntilIdle));
		Exception? advanceFailure = RunOnDedicatedThread(() => CaptureFailure(() => dispatcher.Advance(TimeSpan.Zero)));

		Assert.IsInstanceOfType<InvalidOperationException>(idleFailure);
		Assert.IsInstanceOfType<InvalidOperationException>(advanceFailure);
	}

	[TestMethod]
	public void Constructor_InstallingTheContext_ReplacesTheThreadContextUntilDisposed()
	{
		Exception? failure = RunOnDedicatedThread(() =>
			CaptureFailure(() =>
			{
				var previous = new SynchronizationContext();
				SynchronizationContext.SetSynchronizationContext(previous);

				var dispatcher = new FakePumpedDispatcher(installSynchronizationContext: true);

				Assert.AreNotSame(previous, SynchronizationContext.Current);
				Assert.IsNotNull(SynchronizationContext.Current);

				dispatcher.Dispose();
				dispatcher.Dispose();

				Assert.AreSame(previous, SynchronizationContext.Current);
			})
		);

		Assert.IsNull(failure);
	}

	[TestMethod]
	public void Constructor_WithoutTheContext_ClearsTheThreadContextUntilDisposed()
	{
		Exception? failure = RunOnDedicatedThread(() =>
			CaptureFailure(() =>
			{
				var previous = new SynchronizationContext();
				SynchronizationContext.SetSynchronizationContext(previous);

				var dispatcher = new FakePumpedDispatcher(installSynchronizationContext: false);

				Assert.IsNull(SynchronizationContext.Current);

				dispatcher.Dispose();

				Assert.AreSame(previous, SynchronizationContext.Current);
			})
		);

		Assert.IsNull(failure);
	}

	[TestMethod]
	public void SynchronizationContext_PostFromAnotherThread_QueuesToTheOwnerAndRunsWhenPumped()
	{
		using var dispatcher = new FakePumpedDispatcher(installSynchronizationContext: true);

		SynchronizationContext context = SynchronizationContext.Current!;
		bool ran = false;

		RunOnDedicatedThread(() =>
		{
			context.Post(_ => ran = true, state: null);
			return true;
		});

		Assert.AreEqual(1, dispatcher.QueuedCount);
		Assert.IsFalse(ran);

		dispatcher.PumpUntilIdle();

		Assert.IsTrue(ran);
	}

	[TestMethod]
	public void SynchronizationContext_SendOnTheOwnerThread_RunsInline()
	{
		using var dispatcher = new FakePumpedDispatcher(installSynchronizationContext: true);

		SynchronizationContext context = SynchronizationContext.Current!;
		bool ran = false;

		context.Send(_ => ran = true, state: null);

		Assert.IsTrue(ran);
		Assert.AreEqual(0, dispatcher.QueuedCount);
	}

	[TestMethod]
	public void SynchronizationContext_SendFromAnotherThread_BlocksUntilTheOwnerPumps()
	{
		using var dispatcher = new FakePumpedDispatcher(installSynchronizationContext: true);

		SynchronizationContext context = SynchronizationContext.Current!;
		int runThreadId = 0;

		Task<bool> caller = StartOnDedicatedThread(() =>
		{
			context.Send(_ => runThreadId = Environment.CurrentManagedThreadId, state: null);
			return true;
		});

		Assert.IsTrue(SpinWait.SpinUntil(() => dispatcher.QueuedCount == 1, s_waitLimit));
		Assert.IsFalse(caller.IsCompleted);
		Assert.AreEqual(0, runThreadId);

		dispatcher.PumpUntil(() => caller.IsCompleted);

		Assert.AreEqual(dispatcher.OwnerThreadId, runThreadId);
	}

	[TestMethod]
	public void SynchronizationContext_SuspendedAwait_ResumesOnlyWhenTheOwnerPumps()
	{
		using var dispatcher = new FakePumpedDispatcher(installSynchronizationContext: true);

		var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		int resumeThreadId = 0;

		Task awaiter = AwaitAsync(gate.Task, () => resumeThreadId = Environment.CurrentManagedThreadId);

		gate.SetResult();

		Assert.IsFalse(awaiter.IsCompleted);

		dispatcher.PumpUntil(() => awaiter.IsCompleted);

		Assert.AreEqual(dispatcher.OwnerThreadId, resumeThreadId);

		static async Task AwaitAsync(Task task, Action resumed)
		{
			await task;
			resumed();
		}
	}

	[TestMethod]
	public void Dispose_CancelsTheCallsThatWaitForTheOwner_AndRejectsLaterOnes()
	{
		var dispatcher = new FakePumpedDispatcher(installSynchronizationContext: false);

		Task<Exception?> waiting = StartOnDedicatedThread(() => CaptureFailure(() => dispatcher.Invoke(static () => { })));

		Assert.IsTrue(SpinWait.SpinUntil(() => dispatcher.QueuedCount == 1, s_waitLimit));

		dispatcher.Dispose();

		Assert.IsInstanceOfType<OperationCanceledException>(waiting.GetAwaiter().GetResult());

		// A later call from another thread is rejected like a call to a dispatcher that shut down; posting and scheduling have nothing to run.
		Exception? laterInvoke = RunOnDedicatedThread(() => CaptureFailure(() => dispatcher.Invoke(static () => { })));
		Task laterAsync = RunOnDedicatedThread(() => dispatcher.InvokeAsync(static () => { }));

		bool posted = false;
		dispatcher.Post(() => posted = true);

		bool scheduled = false;
		dispatcher.Schedule(TimeSpan.Zero, () => scheduled = true);

		dispatcher.Advance(TimeSpan.FromMilliseconds(10.0));
		dispatcher.PumpUntilIdle();

		Assert.IsInstanceOfType<OperationCanceledException>(laterInvoke);
		Assert.IsTrue(laterAsync.IsCanceled);
		Assert.IsFalse(posted);
		Assert.IsFalse(scheduled);
		Assert.AreEqual(0, dispatcher.PendingTimerCount);
	}

	private static Exception? CaptureFailure(Action action)
	{
		try
		{
			action();
			return null;
		}
		catch (Exception exception)
		{
			return exception;
		}
	}

	// A dedicated thread is genuinely off the owner thread: a Task.Run work item can be inlined on the thread that waits for it.
	private static Task<T> StartOnDedicatedThread<T>(Func<T> function)
	{
		var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

		var thread = new Thread(() =>
		{
			try
			{
				completion.SetResult(function());
			}
			catch (Exception exception)
			{
				completion.SetException(exception);
			}
		});

		thread.Start();
		return completion.Task;
	}

	// Runs the function on a dedicated thread and waits for it, so a test that changes the thread's synchronization context cannot leak into the
	// test host either.
	private static T RunOnDedicatedThread<T>(Func<T> function) => StartOnDedicatedThread(function).GetAwaiter().GetResult();
}
