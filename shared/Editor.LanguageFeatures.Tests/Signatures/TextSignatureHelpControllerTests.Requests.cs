#if AVALONIAEDIT
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Signatures;
using Nickelony.IDEKit.IntelliSense.Signatures;
using STATestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestHost = Nickelony.IDEKit.Testing.AvaloniaTestHost;
namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;
#else
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Signatures;
using Nickelony.IDEKit.IntelliSense.Signatures;
using TestHost = Nickelony.IDEKit.Testing.WPFTestHost;
namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;
#endif

public sealed partial class TextSignatureHelpControllerTests
{
	[TestMethod]
	public async Task RequestAsync_ThrowingProvider_LeavesSignatureHelpInactive()
	{
		var logger = new CapturingLogger();
		var host = new SignatureHelpTestHost
		{
			RequestSignatureHelpAsync = (_, _, _) => throw new InvalidOperationException("Signature help failed.")
		};

		using var controller = host.CreateController(logger: logger);
		await controller.RequestAsync(5);

		// A provider failure is contained and leaves signature help hidden with no request pending, and it
		// carries the documented request event id.
		Assert.IsFalse(controller.CurrentPresentation.IsVisible);
		Assert.IsFalse(controller.CurrentPresentation.IsPresentationVisibleOrRequestPending);
		CollectionAssert.AreEqual(Array.Empty<TextSignatureHelp>(), host.ShownSignatures);
		Assert.AreEqual(1, logger.Entries.Count);
		Assert.AreEqual(1020, logger.Entries[0].EventId.Id);
	}

	[TestMethod]
	public void RequestAsync_AsyncProviderWithoutSynchronizationContext_ShowsOnTheDispatcherThread()
	{
		var providerGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var providerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		int showThreadId = 0;
		int dispatcherThreadId = Environment.CurrentManagedThreadId;

		var host = new SignatureHelpTestHost
		{
			RequestSignatureHelpAsync = async (_, _, _) =>
			{
				providerStarted.TrySetResult();
				await providerGate.Task.ConfigureAwait(false);
				return CreateDefaultSignatureHelp();
			},
			ShowCallback = _ => showThreadId = Environment.CurrentManagedThreadId
		};

		using var controller = host.CreateController();

		// The test thread carries no synchronization context, so the provider continuation resumes on a
		// thread-pool thread; the host show callback and the state bookkeeping must still run on the
		// dispatcher thread instead of touching dispatcher-bound state from there.
		Task requestTask = controller.RequestAsync(5);

		// Wait on the provider's start signal instead of a fixed pump window, so the gate is released only
		// once the request is genuinely in flight.
		DispatcherTestUtils.PumpUntil(() => providerStarted.Task.IsCompleted);
		providerGate.SetResult();
		DispatcherTestUtils.PumpUntil(() => requestTask.IsCompleted);

		Assert.AreEqual(1, host.ShownSignatures.Count);
		Assert.AreEqual(dispatcherThreadId, showThreadId);
	}

	[TestMethod]
	public async Task RequestAsync_StaleRequestAfterInvalidation_DoesNotShowSignatureHelp()
	{
		TextSignatureHelpController? controller = null;
		var host = new SignatureHelpTestHost
		{
			// Invalidate the request before returning its result so the controller must ignore it.
			RequestSignatureHelpAsync = (_, _, _) =>
			{
				controller!.InvalidateRequests();
				return Task.FromResult<TextSignatureHelp?>(CreateDefaultSignatureHelp());
			}
		};

		controller = host.CreateController();
		await controller.RequestAsync(5);

		CollectionAssert.AreEqual(Array.Empty<TextSignatureHelp>(), host.ShownSignatures);
		Assert.IsFalse(controller.CurrentPresentation.IsVisible);
	}

	[TestMethod]
	public void RequestAsync_AfterDisposal_ReturnsCompletedTaskAndDoesNotShow()
	{
		var host = new SignatureHelpTestHost
		{
			RequestSignatureHelpAsync = (_, _, _) => Task.FromResult<TextSignatureHelp?>(CreateDefaultSignatureHelp())
		};

		var controller = host.CreateController();
		controller.Dispose();

		Task requestTask = controller.RequestAsync(5);

		Assert.IsTrue(requestTask.IsCompletedSuccessfully);
		CollectionAssert.AreEqual(Array.Empty<TextSignatureHelp>(), host.ShownSignatures);
		Assert.IsFalse(controller.CurrentPresentation.IsVisible);
	}

	[TestMethod]
	public void RequestAsync_InFlightRequestCompletingAfterDisposal_DoesNotShowSignatureHelp()
	{
		var completion = new TaskCompletionSource<TextSignatureHelp?>(TaskCreationOptions.RunContinuationsAsynchronously);
		var host = new SignatureHelpTestHost
		{
			RequestSignatureHelpAsync = (_, _, _) => completion.Task
		};

		var controller = host.CreateController();

		Task requestTask = controller.RequestAsync(5);
		controller.Dispose();
		completion.TrySetResult(CreateDefaultSignatureHelp());

		DispatcherTestUtils.PumpUntil(() => requestTask.IsCompleted);
		requestTask.GetAwaiter().GetResult();

		// A request that completes after disposal must not show signature help.
		CollectionAssert.AreEqual(Array.Empty<TextSignatureHelp>(), host.ShownSignatures);
		Assert.IsFalse(controller.CurrentPresentation.IsVisible);
	}

	[TestMethod]
	public void PublicOperations_AfterDisposal_LeaveSignatureHelpInactive()
	{
		var host = new SignatureHelpTestHost
		{
			RequestSignatureHelpAsync = (_, _, _) => Task.FromResult<TextSignatureHelp?>(CreateDefaultSignatureHelp())
		};

		var controller = host.CreateController();
		controller.Dispose();

		controller.Dismiss();
		controller.ScheduleRefresh();
		controller.CancelScheduledRefresh();
		controller.InvalidateRequests();
		controller.CancelInFlightRequest();

		Assert.IsFalse(controller.SelectNextSignature());
		Assert.IsFalse(controller.SelectPreviousSignature());
		CollectionAssert.AreEqual(Array.Empty<TextSignatureHelp>(), host.ShownSignatures);
		Assert.IsFalse(controller.CurrentPresentation.IsVisible);
		Assert.IsFalse(controller.CurrentPresentation.IsPresentationVisibleOrRequestPending);
	}

	[TestMethod]
	public async Task RequestAsync_ProviderReturnsNull_WhenNotVisible_DismissesSignatureHelp()
	{
		var host = new SignatureHelpTestHost
		{
			RequestSignatureHelpAsync = (_, _, _) => Task.FromResult<TextSignatureHelp?>(null)
		};

		using var controller = host.CreateController();
		await controller.RequestAsync(5);

		// A null result with nothing visible dismisses signature help.
		Assert.AreEqual(1, host.DismissCount);
		CollectionAssert.AreEqual(Array.Empty<TextSignatureHelp>(), host.ShownSignatures);
		Assert.IsFalse(controller.CurrentPresentation.IsVisible);
		Assert.IsNull(controller.CurrentPresentation.SignatureHelp);
	}

	[TestMethod]
	public async Task RequestAsync_ProviderReturnsNull_WhenVisible_DismissesStaleSignatureHelp()
	{
		int servedResponses = 0;
		var host = new SignatureHelpTestHost
		{
			RequestSignatureHelpAsync = (_, _, _) => Task.FromResult<TextSignatureHelp?>(servedResponses++ == 0
				? CreateDefaultSignatureHelp()
				: null)
		};

		using var controller = host.CreateController();

		await controller.RequestAsync(5);
		await controller.RequestAsync(9);

		// A null result means there is nothing to present; the visible popup is dismissed instead of
		// leaving a stale signature on screen.
		Assert.AreEqual(1, host.ShownSignatures.Count);
		Assert.AreEqual(1, host.DismissCount);
		Assert.IsFalse(controller.CurrentPresentation.IsVisible);
		Assert.IsNull(controller.CurrentPresentation.SignatureHelp);
	}

	[TestMethod]
	public void Dismiss_WhileRequestInFlight_CancelsTheProviderToken()
	{
		var completion = new TaskCompletionSource<TextSignatureHelp?>(TaskCreationOptions.RunContinuationsAsynchronously);
		var host = new SignatureHelpTestHost
		{
			RequestSignatureHelpAsync = (_, _, _) => completion.Task
		};

		using var controller = host.CreateController();

		Task request = controller.RequestAsync(5);
		controller.Dismiss();

		// Dismissing cancels the in-flight provider request's token.
		Assert.IsTrue(host.RequestTokens[0].IsCancellationRequested);

		completion.TrySetResult(null);
		DispatcherTestUtils.PumpUntil(() => request.IsCompleted);
		request.GetAwaiter().GetResult();

		Assert.IsFalse(controller.CurrentPresentation.IsPresentationVisibleOrRequestPending);
	}

	[TestMethod]
	public void Dispose_WhileRequestInFlight_CancelsTheProviderToken()
	{
		var completion = new TaskCompletionSource<TextSignatureHelp?>(TaskCreationOptions.RunContinuationsAsynchronously);
		var host = new SignatureHelpTestHost
		{
			RequestSignatureHelpAsync = (_, _, _) => completion.Task
		};

		var controller = host.CreateController();

		Task request = controller.RequestAsync(5);
		controller.Dispose();

		// Disposal cancels the in-flight provider request's token.
		Assert.IsTrue(host.RequestTokens[0].IsCancellationRequested);

		completion.TrySetResult(null);
		DispatcherTestUtils.PumpUntil(() => request.IsCompleted);
		request.GetAwaiter().GetResult();

		Assert.IsFalse(controller.CurrentPresentation.IsPresentationVisibleOrRequestPending);
	}

	[TestMethod]
	public async Task CancelInFlightRequest_WhileRequestInFlight_CancelsTheTokenAndKeepsThePresentation()
	{
		var firstCompletion = new TaskCompletionSource<TextSignatureHelp?>(TaskCreationOptions.RunContinuationsAsynchronously);
		var secondCompletion = new TaskCompletionSource<TextSignatureHelp?>(TaskCreationOptions.RunContinuationsAsynchronously);
		int requestCount = 0;

		var host = new SignatureHelpTestHost
		{
			RequestSignatureHelpAsync = (_, _, _) => ++requestCount == 1
				? Task.FromResult<TextSignatureHelp?>(CreateDefaultSignatureHelp())
				: requestCount == 2 ? firstCompletion.Task : secondCompletion.Task
		};

		var controller = host.CreateController();

		await controller.RequestAsync(5);
		Assert.IsTrue(controller.CurrentPresentation.IsVisible);

		// A second request is in flight; canceling it must cancel the provider token without clearing the
		// visible presentation or a scheduled refresh.
		Task secondRequest = controller.RequestAsync(9);

		controller.ScheduleRefresh();
		Assert.IsTrue(controller.CurrentPresentation.IsRefreshPending);

		controller.CancelInFlightRequest();

		Assert.IsTrue(host.RequestTokens[1].IsCancellationRequested);
		Assert.IsTrue(controller.CurrentPresentation.IsVisible);
		Assert.IsTrue(controller.CurrentPresentation.IsRefreshPending);

		// The canceled request's late result is rejected.
		firstCompletion.TrySetResult(CreateDefaultSignatureHelp());
		DispatcherTestUtils.PumpUntil(() => secondRequest.IsCompleted);
		secondRequest.GetAwaiter().GetResult();

		Assert.AreEqual(1, host.ShownSignatures.Count);

		// A request started after the cancellation observes a fresh, uncanceled token.
		Task thirdRequest = controller.RequestAsync(12);

		Assert.IsFalse(host.RequestTokens[2].IsCancellationRequested);

		secondCompletion.TrySetResult(CreateDefaultSignatureHelp());
		DispatcherTestUtils.PumpUntil(() => thirdRequest.IsCompleted);
		thirdRequest.GetAwaiter().GetResult();

		controller.Dispose();
	}

	[TestMethod]
	public async Task RequestAsync_ProviderReturnsSignature_ShowsSignatureHelp()
	{
		var signature = CreateDefaultSignatureHelp();
		var host = new SignatureHelpTestHost
		{
			RequestSignatureHelpAsync = (_, _, _) => Task.FromResult<TextSignatureHelp?>(signature)
		};

		using var controller = host.CreateController();
		await controller.RequestAsync(5);

		Assert.AreEqual(1, host.ShownSignatures.Count);
		Assert.AreSame(signature, host.ShownSignatures[0]);
		Assert.IsTrue(controller.CurrentPresentation.IsVisible);
		Assert.AreSame(signature, controller.CurrentPresentation.SignatureHelp);
	}

	[TestMethod]
	public void RequestAsync_SupersedingRequest_CancelsEarlierProviderTokenAndAppliesOnlyTheNewestResult()
	{
		var firstCompletion = new TaskCompletionSource<TextSignatureHelp?>(TaskCreationOptions.RunContinuationsAsynchronously);
		var secondCompletion = new TaskCompletionSource<TextSignatureHelp?>(TaskCreationOptions.RunContinuationsAsynchronously);
		var secondSignature = new TextSignatureHelp([new TextSignatureInformation("f(a, b)")]);
		int requestCount = 0;

		var host = new SignatureHelpTestHost
		{
			RequestSignatureHelpAsync = (_, _, _) => ++requestCount == 1 ? firstCompletion.Task : secondCompletion.Task
		};

		using var controller = host.CreateController();

		Task firstRequest = controller.RequestAsync(5);
		Task secondRequest = controller.RequestAsync(9);

		// A superseding request cancels the earlier provider token and owns the in-flight state; no queued
		// refresh delays it.
		Assert.IsTrue(host.RequestTokens[0].IsCancellationRequested);
		Assert.IsFalse(host.RequestTokens[1].IsCancellationRequested);
		Assert.IsTrue(controller.CurrentPresentation.IsRequestInFlight);
		Assert.IsFalse(controller.CurrentPresentation.IsRefreshPending);

		// The earlier in-flight result is ignored even though its provider ignored the cancellation.
		firstCompletion.TrySetResult(CreateDefaultSignatureHelp());
		DispatcherTestUtils.PumpUntil(() => firstRequest.IsCompleted);
		firstRequest.GetAwaiter().GetResult();

		Assert.IsFalse(controller.CurrentPresentation.IsVisible);

		secondCompletion.TrySetResult(secondSignature);
		DispatcherTestUtils.PumpUntil(() => secondRequest.IsCompleted);
		secondRequest.GetAwaiter().GetResult();

		Assert.AreEqual(1, host.ShownSignatures.Count);
		Assert.AreSame(secondSignature, host.ShownSignatures[0]);
		Assert.IsFalse(controller.CurrentPresentation.IsRequestInFlight);
	}

	[TestMethod]
	public void RequestAsync_InvalidateThenNewRequest_StaleCompletionKeepsNewerRequestInFlight()
	{
		var firstCompletion = new TaskCompletionSource<TextSignatureHelp?>(TaskCreationOptions.RunContinuationsAsynchronously);
		var secondCompletion = new TaskCompletionSource<TextSignatureHelp?>(TaskCreationOptions.RunContinuationsAsynchronously);
		var thirdCompletion = new TaskCompletionSource<TextSignatureHelp?>(TaskCreationOptions.RunContinuationsAsynchronously);
		int requestCount = 0;

		var host = new SignatureHelpTestHost
		{
			RequestSignatureHelpAsync = (_, _, _) => ++requestCount switch
			{
				1 => firstCompletion.Task,
				2 => secondCompletion.Task,
				_ => thirdCompletion.Task
			}
		};

		var controller = host.CreateController();

		Task firstRequest = controller.RequestAsync(5);

		// Invalidate the in-flight request, then start a newer one before the old one completes.
		controller.InvalidateRequests();
		Task secondRequest = controller.RequestAsync(9);

		Assert.IsTrue(controller.CurrentPresentation.IsRequestInFlight);

		// A stale completion must not clear the newer request's in-flight flag.
		firstCompletion.TrySetResult(null);
		DispatcherTestUtils.PumpUntil(() => firstRequest.IsCompleted);
		firstRequest.GetAwaiter().GetResult();

		Assert.IsTrue(
			controller.CurrentPresentation.IsRequestInFlight,
			"The invalidated request must not clear the newer request's in-flight state.");

		// A third request supersedes the second one instead of waiting for it.
		Task thirdRequest = controller.RequestAsync(12);

		Assert.IsTrue(controller.CurrentPresentation.IsRequestInFlight);
		Assert.IsFalse(controller.CurrentPresentation.IsRefreshPending);
		Assert.AreEqual(3, requestCount);

		secondCompletion.TrySetResult(null);
		thirdCompletion.TrySetResult(null);
		DispatcherTestUtils.PumpUntil(() => secondRequest.IsCompleted && thirdRequest.IsCompleted);
		secondRequest.GetAwaiter().GetResult();
		thirdRequest.GetAwaiter().GetResult();

		Assert.IsFalse(controller.CurrentPresentation.IsPresentationVisibleOrRequestPending);

		controller.Dispose();
	}
}
