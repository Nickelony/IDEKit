using Nickelony.IDEKit.LanguageFeatures.Controllers.SignatureHelp;
using Nickelony.IDEKit.LanguageFeatures.SignatureHelp;
using Nickelony.Testing;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Tests;

public sealed partial class SignatureHelpControllerTests
{
	[TestMethod]
	public async Task ScheduleRequest_PastDebounce_RunsTheRequestOnTheDebouncePath()
	{
		using var host = new SignatureHelpTestHost
		{
			GetCurrentCaretOffset = () => 5,
			RequestSignatureHelpAsync = (_, _, _) => Task.FromResult<SignatureHelpInformation?>(CreateDefaultSignatureHelp()),
		};

		SignatureHelpController controller = host.CreateController(
			SignatureHelpControllerOptions.Default with
			{
				RequestDebounceDelay = TimeSpan.FromMilliseconds(1.0),
			}
		);

		await controller.RequestAsync(5, cancellationToken: TestContext.CancellationToken);
		Assert.HasCount(1, host.RequestOffsets);

		controller.ScheduleRequest();
		Assert.IsTrue(controller.CurrentPresentation.IsRequestScheduled);

		host.Dispatcher.Advance(TimeSpan.FromMilliseconds(1.0));

		// The debounced request reached the provider and cleared the scheduled state.
		Assert.HasCount(2, host.RequestOffsets);
		Assert.IsFalse(controller.CurrentPresentation.IsRequestScheduled);

		controller.Dispose();
	}

	[TestMethod]
	public void ScheduleRequest_BeforeTheDelayElapsed_DoesNotRequest()
	{
		using var host = new SignatureHelpTestHost
		{
			GetCurrentCaretOffset = () => 5,
			RequestSignatureHelpAsync = (_, _, _) => Task.FromResult<SignatureHelpInformation?>(null),
		};

		using SignatureHelpController controller = host.CreateController(
			SignatureHelpControllerOptions.Default with
			{
				RequestDebounceDelay = TimeSpan.FromMilliseconds(100.0),
			}
		);

		controller.ScheduleRequest();

		// The virtual clock states the wait: the request is held back until the whole delay has elapsed.
		host.Dispatcher.Advance(TimeSpan.FromMilliseconds(99.0));

		Assert.AreSequenceEqual([], host.RequestOffsets);
		Assert.IsTrue(controller.CurrentPresentation.IsRequestScheduled);

		host.Dispatcher.Advance(TimeSpan.FromMilliseconds(1.0));

		Assert.AreSequenceEqual([5], host.RequestOffsets);
		Assert.IsFalse(controller.CurrentPresentation.IsRequestScheduled);
	}

	[TestMethod]
	public void ScheduleRequest_RearmedBeforeTheDelayElapsed_RestartsTheDelay()
	{
		using var host = new SignatureHelpTestHost
		{
			GetCurrentCaretOffset = () => 5,
			RequestSignatureHelpAsync = (_, _, _) => Task.FromResult<SignatureHelpInformation?>(null),
		};

		using SignatureHelpController controller = host.CreateController(
			SignatureHelpControllerOptions.Default with
			{
				RequestDebounceDelay = TimeSpan.FromMilliseconds(100.0),
			}
		);

		controller.ScheduleRequest();
		host.Dispatcher.Advance(TimeSpan.FromMilliseconds(60.0));

		// Arming again restarts the whole delay, so the first arming's due time passes without a request.
		controller.ScheduleRequest();
		host.Dispatcher.Advance(TimeSpan.FromMilliseconds(60.0));

		Assert.AreSequenceEqual([], host.RequestOffsets);

		host.Dispatcher.Advance(TimeSpan.FromMilliseconds(40.0));

		Assert.AreSequenceEqual([5], host.RequestOffsets);
	}

	[TestMethod]
	public void ScheduleRequest_CaretMovesDuringDebounce_RequestsTheOffsetAtRequestTime()
	{
		int caretOffset = 5;

		using var host = new SignatureHelpTestHost
		{
			GetCurrentCaretOffset = () => caretOffset,
			RequestSignatureHelpAsync = (_, _, _) => Task.FromResult<SignatureHelpInformation?>(CreateDefaultSignatureHelp()),
		};

		SignatureHelpController controller = host.CreateController(
			SignatureHelpControllerOptions.Default with
			{
				RequestDebounceDelay = TimeSpan.FromMilliseconds(1.0),
			}
		);

		controller.ScheduleRequest();

		// The caret moves before the debounce elapses; the request must carry the position at request time
		// (the LSP rule), not the offset read when the request was armed.
		caretOffset = 9;

		host.Dispatcher.Advance(TimeSpan.FromMilliseconds(1.0));

		Assert.AreEqual(9, host.RequestOffsets[0]);

		controller.Dispose();
	}

	[TestMethod]
	public void ScheduleRequest_RearmedTwice_RunsOneRequest()
	{
		using var host = new SignatureHelpTestHost
		{
			GetCurrentCaretOffset = () => 5,
			RequestSignatureHelpAsync = (_, _, _) => Task.FromResult<SignatureHelpInformation?>(null),
		};

		SignatureHelpController controller = host.CreateController(
			SignatureHelpControllerOptions.Default with
			{
				RequestDebounceDelay = TimeSpan.FromMilliseconds(1.0),
			}
		);

		// Arming again restarts the debounce delay, so only the last schedule reaches the provider.
		controller.ScheduleRequest();
		controller.ScheduleRequest();

		Assert.AreEqual(1, host.Dispatcher.PendingTimerCount);

		host.Dispatcher.Advance(TimeSpan.FromMilliseconds(21.0));

		Assert.HasCount(1, host.RequestOffsets);

		controller.Dispose();
	}

	[TestMethod]
	public void RequestAsync_NegativeOffset_ThrowsArgumentOutOfRangeException()
	{
		using var host = new SignatureHelpTestHost();
		using SignatureHelpController controller = host.CreateController();

		ArgumentOutOfRangeException exception = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
			controller.RequestAsync(-1, cancellationToken: TestContext.CancellationToken)
		);

		Assert.AreEqual("offset", exception.ParamName);
	}

	[TestMethod]
	[DataRow(true, DisplayName = "WithTheSynchronizationContext")]
	[DataRow(false, DisplayName = "WithoutTheSynchronizationContext")]
	public void RequestAsync_NegativeOffsetWhileRequestInFlight_ThrowsWithoutCorruptingPendingState(bool installSynchronizationContext)
	{
		var completion = new TaskCompletionSource<SignatureHelpInformation?>(TaskCreationOptions.RunContinuationsAsynchronously);
		using var host = new SignatureHelpTestHost(installSynchronizationContext) { RequestSignatureHelpAsync = (_, _, _) => completion.Task };

		SignatureHelpController controller = host.CreateController();

		Task firstRequest = controller.RequestAsync(5, cancellationToken: TestContext.CancellationToken);

		Assert.IsTrue(controller.CurrentPresentation.IsRequestInFlight);

		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => controller.RequestAsync(-1, cancellationToken: TestContext.CancellationToken));

		// The invalid request must neither disturb the in-flight request nor arm a stuck scheduled request.
		Assert.IsTrue(controller.CurrentPresentation.IsRequestInFlight);
		Assert.IsFalse(controller.CurrentPresentation.IsRequestScheduled);
		Assert.IsFalse(host.RequestTokens[0].IsCancellationRequested);

		completion.TrySetResult(null);
		host.Dispatcher.PumpUntil(() => firstRequest.IsCompleted);
		firstRequest.GetAwaiter().GetResult();

		Assert.IsFalse(controller.CurrentPresentation.IsPresentationVisibleOrRequestScheduled);
		Assert.IsFalse(controller.CurrentPresentation.IsVisible);

		controller.Dispose();
	}

	[TestMethod]
	[DataRow(-1, DisplayName = "WithANegativeOffset")]
	[DataRow(null, DisplayName = "WithoutAnOffset")]
	public void ScheduleRequest_NoCaretReported_CancelsTheScheduledRequest(int? noCaretOffset)
	{
		int? caretOffset = 0;

		using var host = new SignatureHelpTestHost
		{
			GetCurrentCaretOffset = () => caretOffset,
			RequestSignatureHelpAsync = (_, _, _) => Task.FromResult<SignatureHelpInformation?>(null),
		};

		SignatureHelpController controller = host.CreateController(
			SignatureHelpControllerOptions.Default with
			{
				RequestDebounceDelay = TimeSpan.FromMilliseconds(1.0),
			}
		);

		controller.ScheduleRequest();
		Assert.IsTrue(controller.CurrentPresentation.IsRequestScheduled);

		// An editor that reports no caret (a negative offset, or none at all) cancels the armed request instead of
		// leaving it pending or starting another one.
		caretOffset = noCaretOffset;
		controller.ScheduleRequest();

		Assert.IsFalse(controller.CurrentPresentation.IsRequestScheduled);
		Assert.IsFalse(controller.CurrentPresentation.IsPresentationVisibleOrRequestScheduled);
		Assert.AreEqual(0, host.Dispatcher.PendingTimerCount);

		// The canceled request must not reach the provider after the debounce elapses.
		host.Dispatcher.Advance(TimeSpan.FromMilliseconds(50.0));

		Assert.AreSequenceEqual([], host.RequestOffsets);

		controller.Dispose();
	}

	[TestMethod]
	public void ScheduleRequest_CaretLostDuringDebounce_RequestsNothing()
	{
		int? caretOffset = 5;

		using var host = new SignatureHelpTestHost
		{
			GetCurrentCaretOffset = () => caretOffset,
			RequestSignatureHelpAsync = (_, _, _) => Task.FromResult<SignatureHelpInformation?>(null),
		};

		using SignatureHelpController controller = host.CreateController(
			SignatureHelpControllerOptions.Default with
			{
				RequestDebounceDelay = TimeSpan.FromMilliseconds(1.0),
			}
		);

		controller.ScheduleRequest();

		// The editor loses its document before the delay elapses; the debounced request reads the caret again and finds none.
		caretOffset = null;
		host.Dispatcher.Advance(TimeSpan.FromMilliseconds(5.0));

		Assert.AreSequenceEqual([], host.BuildOffsets);
		Assert.AreSequenceEqual([], host.RequestOffsets);
		Assert.IsFalse(controller.CurrentPresentation.IsRequestInFlight);
		Assert.IsFalse(controller.CurrentPresentation.IsRequestScheduled);
	}

	[TestMethod]
	public void ScheduleRequest_CaretReaderThrowsOnTheDebouncePath_IsLoggedAndRequestsNothing()
	{
		var logger = new RecordingLogger();

		int reads = 0;

		using var host = new SignatureHelpTestHost
		{
			GetCurrentCaretOffset = () => reads++ == 0 ? 5 : throw new InvalidOperationException("The caret reader failed."),
			RequestSignatureHelpAsync = (_, _, _) => Task.FromResult<SignatureHelpInformation?>(null),
		};

		using SignatureHelpController controller = host.CreateController(
			SignatureHelpControllerOptions.Default with
			{
				RequestDebounceDelay = TimeSpan.FromMilliseconds(1.0),
			},

			logger
		);

		controller.ScheduleRequest();

		// The timer callback must not let the host's failure escape: it is logged as a host-callback failure and counts as no caret.
		host.Dispatcher.Advance(TimeSpan.FromMilliseconds(5.0));

		Assert.AreSequenceEqual([], host.BuildOffsets);
		Assert.AreSequenceEqual([], host.RequestOffsets);
		Assert.HasCount(1, logger.Entries);
		Assert.AreEqual(1021, logger.Entries[0].EventId.Id);
		Assert.IsFalse(controller.CurrentPresentation.IsRequestScheduled);
	}

	[TestMethod]
	public void Options_NegativeRequestDebounceDelay_IsRejectedAtAssignment()
	{
		ArgumentOutOfRangeException exception = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
			SignatureHelpControllerOptions.Default with
			{
				RequestDebounceDelay = TimeSpan.FromMilliseconds(-1.0),
			}
		);

		Assert.AreEqual("RequestDebounceDelay", exception.ParamName);
	}

	[TestMethod]
	[DataRow(true, DisplayName = "WithTheSynchronizationContext")]
	[DataRow(false, DisplayName = "WithoutTheSynchronizationContext")]
	public void ScheduleRequest_WhileRequestInFlight_SupersedesTheInFlightRequest(bool installSynchronizationContext)
	{
		var completion = new TaskCompletionSource<SignatureHelpInformation?>(TaskCreationOptions.RunContinuationsAsynchronously);

		using var host = new SignatureHelpTestHost(installSynchronizationContext)
		{
			GetCurrentCaretOffset = () => 9,
			RequestSignatureHelpAsync = (_, _, _) => completion.Task,
		};

		SignatureHelpController controller = host.CreateController(
			SignatureHelpControllerOptions.Default with
			{
				RequestDebounceDelay = TimeSpan.FromMilliseconds(1.0),
			}
		);

		Task firstRequest = controller.RequestAsync(5, cancellationToken: TestContext.CancellationToken);

		controller.ScheduleRequest();

		// The debounced request runs as the newest request and supersedes the in-flight one instead of
		// waiting for it.
		host.Dispatcher.Advance(TimeSpan.FromMilliseconds(1.0));

		Assert.HasCount(2, host.RequestTokens);
		Assert.IsTrue(host.RequestTokens[0].IsCancellationRequested);
		Assert.IsFalse(controller.CurrentPresentation.IsRequestScheduled);
		Assert.AreEqual(9, host.RequestOffsets[1]);

		completion.TrySetResult(null);
		host.Dispatcher.PumpUntil(() => firstRequest.IsCompleted);
		firstRequest.GetAwaiter().GetResult();
		host.Dispatcher.PumpUntil(() => !controller.CurrentPresentation.IsRequestInFlight);

		Assert.IsFalse(controller.CurrentPresentation.IsPresentationVisibleOrRequestScheduled);

		controller.Dispose();
	}

	[TestMethod]
	public async Task RequestAsync_ImmediateRequestWhileRequestScheduled_DiscardsStaleScheduledRequest()
	{
		using var host = new SignatureHelpTestHost
		{
			GetCurrentCaretOffset = () => 5,

			RequestSignatureHelpAsync = (_, _, _) =>
				Task.FromResult<SignatureHelpInformation?>(
					new SignatureHelpInformation([new SignatureInformation("sample(alpha)", "Sample signature documentation.")])
				),
		};

		SignatureHelpController controller = host.CreateController(
			SignatureHelpControllerOptions.Default with
			{
				RequestDebounceDelay = TimeSpan.FromMilliseconds(1.0),
			}
		);

		controller.ScheduleRequest();
		Assert.IsTrue(controller.CurrentPresentation.IsRequestScheduled);

		await controller.RequestAsync(9, cancellationToken: TestContext.CancellationToken);

		// The immediate request supersedes the scheduled request; the stale offset must not be
		// requested afterwards.
		Assert.IsFalse(controller.CurrentPresentation.IsRequestScheduled);

		host.Dispatcher.Advance(TimeSpan.FromMilliseconds(100.0));

		Assert.AreSequenceEqual([9], host.RequestOffsets);

		controller.Dispose();
	}

	[TestMethod]
	public void CancelScheduledRequest_BeforeDebounce_PreventsTheRequestFromRunning()
	{
		using var host = new SignatureHelpTestHost
		{
			GetCurrentCaretOffset = () => 5,
			RequestSignatureHelpAsync = (_, _, _) => Task.FromResult<SignatureHelpInformation?>(CreateDefaultSignatureHelp()),
		};

		using SignatureHelpController controller = host.CreateController(
			SignatureHelpControllerOptions.Default with
			{
				RequestDebounceDelay = TimeSpan.FromMilliseconds(1.0),
			}
		);

		controller.ScheduleRequest();
		Assert.IsTrue(controller.CurrentPresentation.IsRequestScheduled);

		controller.CancelScheduledRequest();

		// The debounce was canceled and must not reach the provider.
		host.Dispatcher.Advance(TimeSpan.FromMilliseconds(50.0));

		Assert.AreSequenceEqual([], host.RequestOffsets);
		Assert.IsFalse(controller.CurrentPresentation.IsRequestScheduled);
	}
}
