#if AVALONIAEDIT
using Avalonia.Threading;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Signatures;
using Nickelony.IDEKit.IntelliSense.Signatures;
using STATestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestHost = Nickelony.IDEKit.Testing.AvaloniaTestHost;
namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;
#else
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Signatures;
using Nickelony.IDEKit.IntelliSense.Signatures;
using System.Windows.Threading;
using TestHost = Nickelony.IDEKit.Testing.WPFTestHost;
namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;
#endif

public sealed partial class TextSignatureHelpControllerTests
{
	[TestMethod]
	public void ScheduleRefresh_ControllerCreatedOffThreadWithExplicitDispatcher_RunsTheRefreshOnTheDispatcher()
	{
#if AVALONIAEDIT
		Dispatcher hostDispatcher = Dispatcher.UIThread;
#else
		Dispatcher hostDispatcher = Dispatcher.CurrentDispatcher;
#endif
		var host = new SignatureHelpTestHost
		{
			GetCurrentCaretOffset = () => 5,
			RequestSignatureHelpAsync = (_, _, _) => Task.FromResult<TextSignatureHelp?>(CreateDefaultSignatureHelp())
		};

		TextSignatureHelpController? controller = null;

#if AVALONIAEDIT
		// The controller is created on a worker thread against the host dispatcher; its refresh timer must be
		// bound to that dispatcher, so the armed refresh reaches the provider once the dispatcher is pumped
		// instead of silently waiting on a dispatcher nobody runs. Avalonia divergence: a Task.Run work item
		// can run inline on the headless dispatcher thread, so a dedicated thread is used to actually create
		// the controller off the dispatcher thread.
		var thread = new Thread(() =>
#else
		// The controller is created on a thread-pool thread against the host dispatcher; its refresh timer
		// must be bound to that dispatcher, so the armed refresh reaches the provider once the dispatcher is
		// pumped instead of silently waiting on a dispatcher nobody runs.
		Task.Run(() =>
#endif
		{
			controller = host.CreateController(
				TextSignatureHelpControllerOptions.Default with { RefreshDebounceDelay = TimeSpan.FromMilliseconds(1.0) },
				dispatcher: hostDispatcher);

			controller.ScheduleRefresh();
#if AVALONIAEDIT
		});

		thread.Start();
		thread.Join();
#else
		}).GetAwaiter().GetResult();
#endif

		DispatcherTestUtils.PumpUntil(() => host.RequestOffsets.Count >= 1);

		Assert.AreEqual(5, host.RequestOffsets[0]);
		Assert.AreEqual(1, host.ShownSignatures.Count);

		controller!.Dispose();
	}

	[TestMethod]
	public async Task ScheduleRefresh_PastDebounce_RunsTheRequestOnTheDebouncePath()
	{
		var host = new SignatureHelpTestHost
		{
			GetCurrentCaretOffset = () => 5,
			RequestSignatureHelpAsync = (_, _, _) => Task.FromResult<TextSignatureHelp?>(CreateDefaultSignatureHelp())
		};

		var controller = host.CreateController(
			TextSignatureHelpControllerOptions.Default with { RefreshDebounceDelay = TimeSpan.FromMilliseconds(1.0) });

		await controller.RequestAsync(5);
		Assert.AreEqual(1, host.RequestOffsets.Count);

		controller.ScheduleRefresh();
		Assert.IsTrue(controller.CurrentPresentation.IsRefreshPending);

		DispatcherTestUtils.PumpUntil(() => host.RequestOffsets.Count >= 2);

		// The debounced refresh reached the provider and cleared the pending state.
		Assert.AreEqual(2, host.RequestOffsets.Count);
		Assert.IsFalse(controller.CurrentPresentation.IsRefreshPending);

		controller.Dispose();
	}

	[TestMethod]
	public void ScheduleRefresh_RearmedTwice_RunsOneRequest()
	{
		var host = new SignatureHelpTestHost
		{
			GetCurrentCaretOffset = () => 5,
			RequestSignatureHelpAsync = (_, _, _) => Task.FromResult<TextSignatureHelp?>(null)
		};

		var controller = host.CreateController(
			TextSignatureHelpControllerOptions.Default with { RefreshDebounceDelay = TimeSpan.FromMilliseconds(1.0) });

		// Arming again restarts the debounce delay, so only the last schedule reaches the provider.
		controller.ScheduleRefresh();
		controller.ScheduleRefresh();

		DispatcherTestUtils.PumpUntil(() => host.RequestOffsets.Count >= 1);
		DispatcherTestUtils.PumpFrames(TimeSpan.FromMilliseconds(20.0));

		Assert.AreEqual(1, host.RequestOffsets.Count);

		controller.Dispose();
	}

	[TestMethod]
	public void RequestAsync_NegativeOffset_ThrowsArgumentOutOfRangeException()
	{
		var host = new SignatureHelpTestHost();
		using var controller = host.CreateController();

		var exception = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => controller.RequestAsync(-1));

		Assert.AreEqual("offset", exception.ParamName);
	}

	[TestMethod]
	public void RequestAsync_NegativeOffsetWhileRequestInFlight_ThrowsWithoutCorruptingPendingState()
	{
		var completion = new TaskCompletionSource<TextSignatureHelp?>(TaskCreationOptions.RunContinuationsAsynchronously);
		var host = new SignatureHelpTestHost
		{
			RequestSignatureHelpAsync = (_, _, _) => completion.Task
		};

		var controller = host.CreateController();

		Task firstRequest = controller.RequestAsync(5);

		Assert.IsTrue(controller.CurrentPresentation.IsRequestInFlight);

		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => controller.RequestAsync(-1));

		// The invalid request must neither disturb the in-flight request nor arm a stuck refresh.
		Assert.IsTrue(controller.CurrentPresentation.IsRequestInFlight);
		Assert.IsFalse(controller.CurrentPresentation.IsRefreshPending);
		Assert.IsFalse(host.RequestTokens[0].IsCancellationRequested);

		completion.TrySetResult(null);
		DispatcherTestUtils.PumpUntil(() => firstRequest.IsCompleted);
		firstRequest.GetAwaiter().GetResult();

		Assert.IsFalse(controller.CurrentPresentation.IsPresentationVisibleOrRequestPending);
		Assert.IsFalse(controller.CurrentPresentation.IsVisible);

		controller.Dispose();
	}

	[TestMethod]
	public void ScheduleRefresh_NegativeCaretOffset_CancelsThePendingRefresh()
	{
		int caretOffset = 0;
		var host = new SignatureHelpTestHost
		{
			GetCurrentCaretOffset = () => caretOffset,
			RequestSignatureHelpAsync = (_, _, _) => Task.FromResult<TextSignatureHelp?>(null)
		};

		var controller = host.CreateController(
			TextSignatureHelpControllerOptions.Default with { RefreshDebounceDelay = TimeSpan.FromMilliseconds(1.0) });

		controller.ScheduleRefresh();
		Assert.IsTrue(controller.CurrentPresentation.IsRefreshPending);

		// A negative caret offset (the host reports no caret) cancels the armed refresh instead of
		// leaving it pending or starting another one.
		caretOffset = -1;
		controller.ScheduleRefresh();

		Assert.IsFalse(controller.CurrentPresentation.IsRefreshPending);
		Assert.IsFalse(controller.CurrentPresentation.IsPresentationVisibleOrRequestPending);

		// The canceled refresh must not reach the provider after the debounce elapses.
		DispatcherTestUtils.PumpFrames(TimeSpan.FromMilliseconds(50.0));

		CollectionAssert.AreEqual(Array.Empty<int>(), host.RequestOffsets);

		controller.Dispose();
	}

	[TestMethod]
	public void ScheduleRefresh_ThrowingCaretHook_IsContainedAndCancelsThePendingRefresh()
	{
		var logger = new CapturingLogger();
		bool throwFromCaretHook = false;

		var host = new SignatureHelpTestHost
		{
			GetCurrentCaretOffset = () =>
			{
				if (throwFromCaretHook)
					throw new InvalidOperationException("The caret getter failed.");

				return 0;
			}
		};

		var controller = host.CreateController(
			TextSignatureHelpControllerOptions.Default with { RefreshDebounceDelay = TimeSpan.FromMilliseconds(1.0) },
			logger: logger);

		controller.ScheduleRefresh();
		Assert.IsTrue(controller.CurrentPresentation.IsRefreshPending);

		// A throwing caret getter is contained like the other host callbacks: the pending refresh is
		// canceled, the failure carries the documented host-callback event id, and it does not escape
		// the scheduling call.
		throwFromCaretHook = true;
		controller.ScheduleRefresh();

		Assert.IsFalse(controller.CurrentPresentation.IsRefreshPending);
		Assert.IsFalse(controller.CurrentPresentation.IsPresentationVisibleOrRequestPending);
		Assert.AreEqual(1, logger.Entries.Count);
		Assert.AreEqual(1021, logger.Entries[0].EventId.Id);

		// The canceled refresh must not reach the provider after the debounce elapses.
		DispatcherTestUtils.PumpFrames(TimeSpan.FromMilliseconds(50.0));

		CollectionAssert.AreEqual(Array.Empty<int>(), host.RequestOffsets);

		controller.Dispose();
	}

	[TestMethod]
	public void Options_NegativeRefreshDebounceDelay_IsRejectedAtAssignment()
	{
		var exception = Assert.ThrowsExactly<ArgumentOutOfRangeException>(
			() => TextSignatureHelpControllerOptions.Default with { RefreshDebounceDelay = TimeSpan.FromMilliseconds(-1.0) });

		Assert.AreEqual("RefreshDebounceDelay", exception.ParamName);
	}

	[TestMethod]
	public void ScheduleRefresh_WhileRequestInFlight_SupersedesTheInFlightRequest()
	{
		var completion = new TaskCompletionSource<TextSignatureHelp?>(TaskCreationOptions.RunContinuationsAsynchronously);
		var host = new SignatureHelpTestHost
		{
			GetCurrentCaretOffset = () => 9,
			RequestSignatureHelpAsync = (_, _, _) => completion.Task
		};

		var controller = host.CreateController(
			TextSignatureHelpControllerOptions.Default with { RefreshDebounceDelay = TimeSpan.FromMilliseconds(1.0) });

		Task firstRequest = controller.RequestAsync(5);

		controller.ScheduleRefresh();

		// The debounced refresh runs as a newest request and supersedes the in-flight one instead of
		// waiting for it.
		DispatcherTestUtils.PumpUntil(() => host.RequestTokens.Count >= 2);

		Assert.IsTrue(host.RequestTokens[0].IsCancellationRequested);
		Assert.IsFalse(controller.CurrentPresentation.IsRefreshPending);
		Assert.AreEqual(9, host.RequestOffsets[1]);

		completion.TrySetResult(null);
		DispatcherTestUtils.PumpUntil(() => firstRequest.IsCompleted);
		firstRequest.GetAwaiter().GetResult();
		DispatcherTestUtils.PumpUntil(() => !controller.CurrentPresentation.IsRequestInFlight);

		Assert.IsFalse(controller.CurrentPresentation.IsPresentationVisibleOrRequestPending);

		controller.Dispose();
	}

	[TestMethod]
	public async Task RequestAsync_ImmediateRequestWhileRefreshPending_DiscardsStaleRefresh()
	{
		var host = new SignatureHelpTestHost
		{
			GetCurrentCaretOffset = () => 5,
			RequestSignatureHelpAsync = (_, _, _) => Task.FromResult<TextSignatureHelp?>(
				new TextSignatureHelp([new TextSignatureInformation("sample(alpha)", "Sample signature documentation.")]))
		};

		var controller = host.CreateController(
			TextSignatureHelpControllerOptions.Default with { RefreshDebounceDelay = TimeSpan.FromMilliseconds(1.0) });

		controller.ScheduleRefresh();
		Assert.IsTrue(controller.CurrentPresentation.IsRefreshPending);

		await controller.RequestAsync(9);

		// The immediate request supersedes the scheduled refresh; the stale offset must not be
		// requested afterwards.
		Assert.IsFalse(controller.CurrentPresentation.IsRefreshPending);

		DispatcherTestUtils.PumpFrames(TimeSpan.FromMilliseconds(100.0));

		CollectionAssert.AreEqual(new[] { 9 }, host.RequestOffsets);

		controller.Dispose();
	}

	[TestMethod]
	public void CancelScheduledRefresh_BeforeDebounce_PreventsTheRefreshFromRunning()
	{
		var host = new SignatureHelpTestHost
		{
			GetCurrentCaretOffset = () => 5,
			RequestSignatureHelpAsync = (_, _, _) => Task.FromResult<TextSignatureHelp?>(CreateDefaultSignatureHelp())
		};

		using var controller = host.CreateController(
			TextSignatureHelpControllerOptions.Default with { RefreshDebounceDelay = TimeSpan.FromMilliseconds(1.0) });

		controller.ScheduleRefresh();
		Assert.IsTrue(controller.CurrentPresentation.IsRefreshPending);

		controller.CancelScheduledRefresh();

		// The debounce was canceled and must not reach the provider.
		DispatcherTestUtils.PumpFrames(TimeSpan.FromMilliseconds(50.0));

		CollectionAssert.AreEqual(Array.Empty<int>(), host.RequestOffsets);
		Assert.IsFalse(controller.CurrentPresentation.IsRefreshPending);
	}
}
