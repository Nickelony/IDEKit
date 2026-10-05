using System.Diagnostics;
using Nickelony.IDEKit.Core.Threading;
#if AVALONIAEDIT
using Avalonia.Threading;
using Nickelony.IDEKit.AvaloniaEdit.Threading;

namespace Nickelony.IDEKit.AvaloniaEdit.Tests;

#else
using System.Windows.Threading;
using Nickelony.IDEKit.AvalonEdit.Threading;

namespace Nickelony.IDEKit.AvalonEdit.Tests;

#endif

/// <summary>
/// Verifies the adapter that maps the neutral dispatcher interface and its priorities onto the engine's dispatcher: the argument guards,
/// the thread rules, the priority mapping, the scheduled callbacks, and posting from another thread.
/// </summary>
/// <remarks>
/// The callbacks that must bind to the void overloads are block-bodied, because an expression-bodied assignment binds to the generic overload.
/// </remarks>
[STATestClass]
public sealed class UiDispatcherTests
{
#if AVALONIAEDIT
	private static Dispatcher EngineDispatcher => Dispatcher.UIThread;
#else
	private static Dispatcher EngineDispatcher => Dispatcher.CurrentDispatcher;
#endif

	[TestMethod]
	public void Constructor_NullDispatcher_ThrowsArgumentNullException()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => new UiDispatcher(null!));
		Assert.ThrowsExactly<ArgumentNullException>(() => UiDispatcher.From(null!));
	}

	[TestMethod]
	public void FromCurrent_AdaptsTheCurrentThreadsDispatcher()
	{
		var dispatcher = UiDispatcher.FromCurrent();

		Assert.IsTrue(dispatcher.CheckAccess());
		dispatcher.VerifyAccess();
	}

	[TestMethod]
	public void Members_NullCallback_ThrowArgumentNullException()
	{
		var dispatcher = UiDispatcher.From(EngineDispatcher);

		Assert.ThrowsExactly<ArgumentNullException>(() => dispatcher.Invoke((Action)null!));
		Assert.ThrowsExactly<ArgumentNullException>(() => dispatcher.Invoke((Func<int>)null!));
		Assert.ThrowsExactly<ArgumentNullException>(() => dispatcher.InvokeAsync(null!));
		Assert.ThrowsExactly<ArgumentNullException>(() => dispatcher.Post(null!));
		Assert.ThrowsExactly<ArgumentNullException>(() => dispatcher.Schedule(TimeSpan.Zero, null!));
	}

	[TestMethod]
	public void Schedule_NegativeDelay_ThrowsArgumentOutOfRangeException()
	{
		var dispatcher = UiDispatcher.From(EngineDispatcher);

		ArgumentOutOfRangeException exception = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
			dispatcher.Schedule(TimeSpan.FromMilliseconds(-1.0), static () => { })
		);

		Assert.AreEqual("delay", exception.ParamName);
	}

	[TestMethod]
	public void CheckAccess_IsTrueOnTheDispatcherThreadAndFalseOnAnotherThread()
	{
		var dispatcher = UiDispatcher.From(EngineDispatcher);

		bool onAnotherThread = RunOnAnotherThread(dispatcher.CheckAccess);

		Assert.IsTrue(dispatcher.CheckAccess());
		Assert.IsFalse(onAnotherThread);
	}

	[TestMethod]
	public void VerifyAccess_OnAnotherThread_ThrowsInvalidOperationException()
	{
		var dispatcher = UiDispatcher.From(EngineDispatcher);

		dispatcher.VerifyAccess();

		Exception? failure = RunOnAnotherThread(() => Capture(dispatcher.VerifyAccess));

		Assert.IsInstanceOfType<InvalidOperationException>(failure);
	}

	[TestMethod]
	public void Invoke_OnTheDispatcherThread_RunsInlineAndReturnsTheResult()
	{
		var dispatcher = UiDispatcher.From(EngineDispatcher);

		bool ran = false;

		dispatcher.Invoke(() =>
		{
			ran = true;
		});

		Assert.IsTrue(ran);
		Assert.AreEqual(42, dispatcher.Invoke(static () => 42));
	}

	[TestMethod]
	public void Invoke_FromAnotherThread_RunsOnTheDispatcherThreadAndWaitsForIt()
	{
		var dispatcher = UiDispatcher.From(EngineDispatcher);

		int dispatcherThreadId = Environment.CurrentManagedThreadId;
		int runThreadId = 0;

		Task<bool> caller = StartOnAnotherThread(() =>
		{
			dispatcher.Invoke(() =>
			{
				runThreadId = Environment.CurrentManagedThreadId;
			});

			return true;
		});

		// The caller blocks in the void overload until this thread pumps; a short sleep does not pump.
		Thread.Sleep(50);

		Assert.IsFalse(caller.IsCompleted);
		Assert.AreEqual(0, runThreadId);

		PumpUntil(() => caller.IsCompleted);

		Assert.AreEqual(dispatcherThreadId, runThreadId);
	}

	[TestMethod]
	public void Invoke_FromAnotherThreadWithAResult_ReturnsItToTheCaller()
	{
		var dispatcher = UiDispatcher.From(EngineDispatcher);

		int dispatcherThreadId = Environment.CurrentManagedThreadId;

		Task<int> caller = StartOnAnotherThread(() => dispatcher.Invoke(static () => Environment.CurrentManagedThreadId));

		Thread.Sleep(50);

		Assert.IsFalse(caller.IsCompleted);

		PumpUntil(() => caller.IsCompleted);

		Assert.AreEqual(dispatcherThreadId, caller.GetAwaiter().GetResult());
	}

	[TestMethod]
	public void InvokeAsync_OnTheDispatcherThread_RunsInlineAndFaultsTheTaskOnAFailure()
	{
		var dispatcher = UiDispatcher.From(EngineDispatcher);

		bool ran = false;

		Task completed = dispatcher.InvokeAsync(() => ran = true);
		Task faulted = dispatcher.InvokeAsync(static () => throw new FormatException("Failed inline."));

		Assert.IsTrue(ran);
		Assert.IsTrue(completed.IsCompletedSuccessfully);
		Assert.IsTrue(faulted.IsFaulted);
		Assert.IsInstanceOfType<FormatException>(faulted.Exception!.InnerException);
	}

	[TestMethod]
	public void InvokeAsync_FromAnotherThread_CompletesAfterTheDispatcherThreadRunsIt()
	{
		var dispatcher = UiDispatcher.From(EngineDispatcher);

		int dispatcherThreadId = Environment.CurrentManagedThreadId;
		int runThreadId = 0;

		Task invocation = RunOnAnotherThread(() => dispatcher.InvokeAsync(() => runThreadId = Environment.CurrentManagedThreadId));

		Assert.IsFalse(invocation.IsCompleted);
		Assert.AreEqual(0, runThreadId);

		PumpUntil(() => invocation.IsCompleted);

		Assert.AreEqual(dispatcherThreadId, runThreadId);
	}

	[TestMethod]
	public void Post_FromAnotherThread_RunsOnTheDispatcherThreadOnceItPumps()
	{
		var dispatcher = UiDispatcher.From(EngineDispatcher);

		int dispatcherThreadId = Environment.CurrentManagedThreadId;
		int runThreadId = 0;

		RunOnAnotherThread(() =>
		{
			dispatcher.Post(() => runThreadId = Environment.CurrentManagedThreadId);
			return true;
		});

		Assert.AreEqual(0, runThreadId);

		PumpUntil(() => runThreadId != 0);

		Assert.AreEqual(dispatcherThreadId, runThreadId);
	}

	[TestMethod]
	public void Post_QueuedBeforeThePump_NeverRunsInline()
	{
		var dispatcher = UiDispatcher.From(EngineDispatcher);

		bool ran = false;

		dispatcher.Post(() => ran = true);

		Assert.IsFalse(ran);

		PumpUntil(() => ran);
	}

	[TestMethod]
	public void Post_Priorities_RunInTheOrderOfTheEngineMapping()
	{
		var dispatcher = UiDispatcher.From(EngineDispatcher);

		var order = new List<string>();

		dispatcher.Post(() => order.Add("context idle"), UiDispatcherPriority.ContextIdle);
		dispatcher.Post(() => order.Add("background"), UiDispatcherPriority.Background);
		dispatcher.Post(() => order.Add("render"), UiDispatcherPriority.Render);
		dispatcher.Post(() => order.Add("normal"), UiDispatcherPriority.Normal);

		// A sentinel at the lowest priority the neutral set maps runs after everything queued above it.
		bool drained = false;
		dispatcher.Post(() => drained = true, UiDispatcherPriority.ContextIdle);

		PumpUntil(() => drained, DispatcherPriority.ContextIdle);

		Assert.AreSequenceEqual(["normal", "render", "background", "context idle"], order);
	}

	[TestMethod]
	public void Schedule_RunsTheCallbackOnceOnTheDispatcherThreadAfterTheDelay()
	{
		var dispatcher = UiDispatcher.From(EngineDispatcher);

		int dispatcherThreadId = Environment.CurrentManagedThreadId;
		int runCount = 0;
		int runThreadId = 0;

		dispatcher.Schedule(
			TimeSpan.FromMilliseconds(1.0),

			() =>
			{
				runCount++;
				runThreadId = Environment.CurrentManagedThreadId;
			}
		);

		PumpUntil(() => runCount >= 1);
		PumpFor(TimeSpan.FromMilliseconds(30.0));

		Assert.AreEqual(1, runCount);
		Assert.AreEqual(dispatcherThreadId, runThreadId);
	}

	[TestMethod]
	public void Schedule_DisposedBeforeTheDelay_NeverRuns()
	{
		var dispatcher = UiDispatcher.From(EngineDispatcher);

		bool ran = false;
		IDisposable handle = dispatcher.Schedule(TimeSpan.FromMilliseconds(1.0), () => ran = true);

		handle.Dispose();
		handle.Dispose();

		PumpFor(TimeSpan.FromMilliseconds(50.0));

		Assert.IsFalse(ran);
	}

	[TestMethod]
	public void Schedule_DisposedAfterItRan_IsHarmless()
	{
		var dispatcher = UiDispatcher.From(EngineDispatcher);

		bool ran = false;
		IDisposable handle = dispatcher.Schedule(TimeSpan.FromMilliseconds(1.0), () => ran = true);

		PumpUntil(() => ran);

		handle.Dispose();
	}

	private static void PumpUntil(Func<bool> condition) => PumpUntil(condition, DispatcherPriority.Background);

	// A wall-clock bounded pump: the engine dispatcher has no virtual clock, so the wait ends when the condition holds or after five seconds.
	private static void PumpUntil(Func<bool> condition, DispatcherPriority priority)
	{
		var stopwatch = Stopwatch.StartNew();

		while (!condition() && stopwatch.Elapsed < TimeSpan.FromSeconds(5.0))
			TestHost.PumpDispatcher(EngineDispatcher, priority);

		Assert.IsTrue(condition(), "The expected dispatcher work did not complete within the allotted time.");
	}

	// The last resort for a negative assertion: a bounded pump, after which the test checks the counter the guarded work would have changed.
	private static void PumpFor(TimeSpan duration)
	{
		var stopwatch = Stopwatch.StartNew();

		while (stopwatch.Elapsed < duration)
			TestHost.PumpDispatcher(EngineDispatcher, DispatcherPriority.Background);
	}

	private static Exception? Capture(Action action)
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

	// A dedicated thread is genuinely off the dispatcher thread, whatever the engine's test session does with the thread pool: a Task.Run work
	// item can be inlined on the thread that waits for it.
	private static Task<T> StartOnAnotherThread<T>(Func<T> function)
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

	private static T RunOnAnotherThread<T>(Func<T> function) => StartOnAnotherThread(function).GetAwaiter().GetResult();
}
