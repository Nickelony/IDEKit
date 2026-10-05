using Nickelony.IDEKit.LanguageFeatures.Controllers.SignatureHelp;
using Nickelony.IDEKit.LanguageFeatures.SignatureHelp;
using Nickelony.Testing;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Tests;

public sealed partial class SignatureHelpControllerTests
{
	[TestMethod]
	public async Task RequestAsync_ThrowingProvider_LeavesSignatureHelpInactive()
	{
		var logger = new RecordingLogger();

		using var host = new SignatureHelpTestHost
		{
			RequestSignatureHelpAsync = (_, _, _) => throw new InvalidOperationException("Signature help failed."),
		};

		using SignatureHelpController controller = host.CreateController(logger: logger);
		await controller.RequestAsync(5, cancellationToken: TestContext.CancellationToken);

		// A provider failure is contained and leaves signature help hidden with no request pending, and it
		// carries the documented request event id.
		Assert.IsFalse(controller.CurrentPresentation.IsVisible);
		Assert.IsFalse(controller.CurrentPresentation.IsPresentationVisibleOrRequestScheduled);
		Assert.AreSequenceEqual([], host.ShownSignatures);
		Assert.HasCount(1, logger.Entries);
		Assert.AreEqual(1020, logger.Entries[0].EventId.Id);
	}

	[TestMethod]
	public void RequestAsync_AsyncProviderWithoutSynchronizationContext_ShowsOnTheDispatcherThread()
	{
		var providerGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var providerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		int showThreadId = 0;

		using var host = new SignatureHelpTestHost(installSynchronizationContext: false)
		{
			RequestSignatureHelpAsync = async (_, _, _) =>
			{
				providerStarted.TrySetResult();
				await providerGate.Task.ConfigureAwait(false);
				return CreateDefaultSignatureHelp();
			},

			ShowCallback = _ => showThreadId = Environment.CurrentManagedThreadId,
		};

		using SignatureHelpController controller = host.CreateController();

		// The dispatcher carries no synchronization context here, so the provider continuation resumes on a
		// thread-pool thread; the host show callback and the state bookkeeping must still run on the
		// dispatcher thread instead of touching dispatcher-bound state from there.
		Task requestTask = controller.RequestAsync(5, cancellationToken: TestContext.CancellationToken);

		// Wait on the provider's start signal instead of a fixed pump window, so the gate is released only
		// once the request is genuinely in flight.
		host.Dispatcher.PumpUntil(() => providerStarted.Task.IsCompleted);
		providerGate.SetResult();
		host.Dispatcher.PumpUntil(() => requestTask.IsCompleted);

		Assert.HasCount(1, host.ShownSignatures);
		Assert.AreEqual(host.Dispatcher.OwnerThreadId, showThreadId);
	}

	[TestMethod]
	public void RequestAsync_NullResultFromAPoolThread_DismissesOnTheDispatcherThread()
	{
		var completion = new TaskCompletionSource<SignatureHelpInformation?>(TaskCreationOptions.RunContinuationsAsynchronously);

		int dismissThreadId = 0;

		using var host = new SignatureHelpTestHost(installSynchronizationContext: false)
		{
			RequestSignatureHelpAsync = (_, _, _) => completion.Task,
			DismissCallback = () => dismissThreadId = Environment.CurrentManagedThreadId,
		};

		using SignatureHelpController controller = host.CreateController();

		Task requestTask = controller.RequestAsync(5, cancellationToken: TestContext.CancellationToken);

		completion.TrySetResult(null);
		host.Dispatcher.PumpUntil(() => requestTask.IsCompleted);

		// A null result dismisses the presentation through the same dispatched step as a shown result.
		Assert.AreEqual(1, host.DismissCount);
		Assert.AreEqual(host.Dispatcher.OwnerThreadId, dismissThreadId);
	}

	[TestMethod]
	public void RequestAsync_DisposalLandsBeforeTheQueuedPublication_ShowsNothing()
	{
		using var host = new SignatureHelpTestHost(installSynchronizationContext: false);

		StaleResultScenario scenario = StartStaleResultScenario(host);

		// The disposal lands between the coordinator's checks and the queued step, which must not show a popup after it.
		scenario.Controller.Dispose();
		host.Dispatcher.PumpUntil(() => scenario.Request.IsCompleted);

		Assert.AreSequenceEqual([], host.ShownSignatures);
		Assert.AreEqual(1, host.DismissCount);
		Assert.IsFalse(scenario.Controller.CurrentPresentation.IsVisible);
	}

	[TestMethod]
	public void RequestAsync_DismissLandsBeforeTheQueuedPublication_ShowsNothing()
	{
		using var host = new SignatureHelpTestHost(installSynchronizationContext: false);

		StaleResultScenario scenario = StartStaleResultScenario(host);

		// A dismissal in the same window must not be undone by the result that had already passed the coordinator's checks.
		scenario.Controller.Dismiss();
		host.Dispatcher.PumpUntil(() => scenario.Request.IsCompleted);

		Assert.AreSequenceEqual([], host.ShownSignatures);
		Assert.AreEqual(1, host.DismissCount);
		Assert.IsFalse(scenario.Controller.CurrentPresentation.IsVisible);

		scenario.Controller.Dispose();
	}

	[TestMethod]
	public void RequestAsync_InvalidationLandsBeforeTheQueuedPublication_ShowsNothing()
	{
		using var host = new SignatureHelpTestHost(installSynchronizationContext: false);

		StaleResultScenario scenario = StartStaleResultScenario(host);

		scenario.Controller.InvalidateRequests();
		host.Dispatcher.PumpUntil(() => scenario.Request.IsCompleted);

		Assert.AreSequenceEqual([], host.ShownSignatures);
		Assert.AreEqual(0, host.DismissCount);
		Assert.IsFalse(scenario.Controller.CurrentPresentation.IsVisible);

		scenario.Controller.Dispose();
	}

	[TestMethod]
	public void RequestAsync_CancellationLandsBeforeTheQueuedPublication_ShowsNothing()
	{
		using var host = new SignatureHelpTestHost(installSynchronizationContext: false);

		StaleResultScenario scenario = StartStaleResultScenario(host);

		scenario.Controller.CancelInFlightRequest();
		host.Dispatcher.PumpUntil(() => scenario.Request.IsCompleted);

		Assert.AreSequenceEqual([], host.ShownSignatures);
		Assert.AreEqual(0, host.DismissCount);
		Assert.IsFalse(scenario.Controller.CurrentPresentation.IsVisible);

		scenario.Controller.Dispose();
	}

	[TestMethod]
	public void RequestAsync_NewerRequestStartsBeforeTheQueuedPublication_ShowsOnlyTheNewerResult()
	{
		var secondCompletion = new TaskCompletionSource<SignatureHelpInformation?>(TaskCreationOptions.RunContinuationsAsynchronously);
		var secondSignature = new SignatureHelpInformation([new SignatureInformation("g(a, b)")]);

		using var host = new SignatureHelpTestHost(installSynchronizationContext: false);

		StaleResultScenario scenario = StartStaleResultScenario(host, onSecondRequest: () => secondCompletion.Task);

		// The newer request supersedes the result that waits in the queue; only the newer result may show.
		Task secondRequest = scenario.Controller.RequestAsync(9, cancellationToken: TestContext.CancellationToken);

		secondCompletion.TrySetResult(secondSignature);
		host.Dispatcher.PumpUntil(() => scenario.Request.IsCompleted && secondRequest.IsCompleted);

		Assert.HasCount(1, host.ShownSignatures);
		Assert.AreSame(secondSignature, host.ShownSignatures[0]);

		scenario.Controller.Dispose();
	}

	[TestMethod]
	public void RequestAsync_DispatcherShutsDownBeforeThePublication_CompletesWithoutFaulting()
	{
		using var host = new SignatureHelpTestHost(installSynchronizationContext: false);

		StaleResultScenario scenario = StartStaleResultScenario(host);

		// The dispatcher rejects the queued publication and the bookkeeping; the controller treats both as the end of the request.
		host.Dispatcher.Dispose();

		Assert.IsTrue(SpinWait.SpinUntil(() => scenario.Request.IsCompleted, TimeSpan.FromSeconds(5.0)));
		Assert.IsFalse(scenario.Request.GetAwaiter().GetResult());
		Assert.AreSequenceEqual([], host.ShownSignatures);
	}

	[TestMethod]
	public async Task RequestAsync_StaleRequestAfterInvalidation_DoesNotShowSignatureHelp()
	{
		SignatureHelpController? controller = null;

		using var host = new SignatureHelpTestHost
		{
			// Invalidate the request before returning its result so the controller must ignore it.
			RequestSignatureHelpAsync = (_, _, _) =>
			{
				controller!.InvalidateRequests();
				return Task.FromResult<SignatureHelpInformation?>(CreateDefaultSignatureHelp());
			},
		};

		controller = host.CreateController();
		await controller.RequestAsync(5, cancellationToken: TestContext.CancellationToken);

		Assert.AreSequenceEqual([], host.ShownSignatures);
		Assert.IsFalse(controller.CurrentPresentation.IsVisible);
	}

	[TestMethod]
	public void RequestAsync_AfterDisposal_ReturnsCompletedTaskAndDoesNotShow()
	{
		using var host = new SignatureHelpTestHost
		{
			RequestSignatureHelpAsync = (_, _, _) => Task.FromResult<SignatureHelpInformation?>(CreateDefaultSignatureHelp()),
		};

		SignatureHelpController controller = host.CreateController();
		controller.Dispose();

		Task requestTask = controller.RequestAsync(5, cancellationToken: TestContext.CancellationToken);

		Assert.IsTrue(requestTask.IsCompletedSuccessfully);
		Assert.AreSequenceEqual([], host.ShownSignatures);
		Assert.IsFalse(controller.CurrentPresentation.IsVisible);
	}

	[TestMethod]
	[DataRow(true, DisplayName = "WithTheSynchronizationContext")]
	[DataRow(false, DisplayName = "WithoutTheSynchronizationContext")]
	public void RequestAsync_InFlightRequestCompletingAfterDisposal_DoesNotShowSignatureHelp(bool installSynchronizationContext)
	{
		var completion = new TaskCompletionSource<SignatureHelpInformation?>(TaskCreationOptions.RunContinuationsAsynchronously);
		using var host = new SignatureHelpTestHost(installSynchronizationContext) { RequestSignatureHelpAsync = (_, _, _) => completion.Task };

		SignatureHelpController controller = host.CreateController();

		Task requestTask = controller.RequestAsync(5, cancellationToken: TestContext.CancellationToken);
		controller.Dispose();
		completion.TrySetResult(CreateDefaultSignatureHelp());

		host.Dispatcher.PumpUntil(() => requestTask.IsCompleted);
		requestTask.GetAwaiter().GetResult();

		// A request that completes after disposal must not show signature help.
		Assert.AreSequenceEqual([], host.ShownSignatures);
		Assert.IsFalse(controller.CurrentPresentation.IsVisible);
	}

	[TestMethod]
	public void PublicOperations_AfterDisposal_LeaveSignatureHelpInactive()
	{
		using var host = new SignatureHelpTestHost
		{
			RequestSignatureHelpAsync = (_, _, _) => Task.FromResult<SignatureHelpInformation?>(CreateDefaultSignatureHelp()),
		};

		SignatureHelpController controller = host.CreateController();
		controller.Dispose();

		controller.Dismiss();
		controller.ScheduleRequest();
		controller.CancelScheduledRequest();
		controller.InvalidateRequests();
		controller.CancelInFlightRequest();

		Assert.IsFalse(controller.SelectNextSignature());
		Assert.IsFalse(controller.SelectPreviousSignature());
		Assert.AreSequenceEqual([], host.ShownSignatures);
		Assert.IsFalse(controller.CurrentPresentation.IsVisible);
		Assert.IsFalse(controller.CurrentPresentation.IsPresentationVisibleOrRequestScheduled);
	}

	[TestMethod]
	public async Task RequestAsync_ProviderReturnsNull_WhenNotVisible_DismissesSignatureHelp()
	{
		using var host = new SignatureHelpTestHost { RequestSignatureHelpAsync = (_, _, _) => Task.FromResult<SignatureHelpInformation?>(null) };

		using SignatureHelpController controller = host.CreateController();
		await controller.RequestAsync(5, cancellationToken: TestContext.CancellationToken);

		// A null result with nothing visible dismisses signature help.
		Assert.AreEqual(1, host.DismissCount);
		Assert.AreSequenceEqual([], host.ShownSignatures);
		Assert.IsFalse(controller.CurrentPresentation.IsVisible);
		Assert.IsNull(controller.CurrentPresentation.SignatureHelp);
	}

	[TestMethod]
	public async Task RequestAsync_ProviderReturnsNull_WhenVisible_DismissesStaleSignatureHelp()
	{
		int servedResponses = 0;

		using var host = new SignatureHelpTestHost
		{
			RequestSignatureHelpAsync = (_, _, _) =>
				Task.FromResult<SignatureHelpInformation?>(servedResponses++ == 0 ? CreateDefaultSignatureHelp() : null),
		};

		using SignatureHelpController controller = host.CreateController();

		await controller.RequestAsync(5, cancellationToken: TestContext.CancellationToken);
		await controller.RequestAsync(9, cancellationToken: TestContext.CancellationToken);

		// A null result means there is nothing to present; the visible popup is dismissed instead of
		// leaving a stale signature on screen.
		Assert.HasCount(1, host.ShownSignatures);
		Assert.AreEqual(1, host.DismissCount);
		Assert.IsFalse(controller.CurrentPresentation.IsVisible);
		Assert.IsNull(controller.CurrentPresentation.SignatureHelp);
	}

	[TestMethod]
	[DataRow(true, DisplayName = "WithTheSynchronizationContext")]
	[DataRow(false, DisplayName = "WithoutTheSynchronizationContext")]
	public void Dismiss_WhileRequestInFlight_CancelsTheProviderToken(bool installSynchronizationContext)
	{
		var completion = new TaskCompletionSource<SignatureHelpInformation?>(TaskCreationOptions.RunContinuationsAsynchronously);
		using var host = new SignatureHelpTestHost(installSynchronizationContext) { RequestSignatureHelpAsync = (_, _, _) => completion.Task };

		using SignatureHelpController controller = host.CreateController();

		Task request = controller.RequestAsync(5, cancellationToken: TestContext.CancellationToken);
		controller.Dismiss();

		// Dismissing cancels the in-flight provider request's token.
		Assert.IsTrue(host.RequestTokens[0].IsCancellationRequested);

		completion.TrySetResult(null);
		host.Dispatcher.PumpUntil(() => request.IsCompleted);
		request.GetAwaiter().GetResult();

		Assert.IsFalse(controller.CurrentPresentation.IsPresentationVisibleOrRequestScheduled);
	}

	[TestMethod]
	[DataRow(true, DisplayName = "WithTheSynchronizationContext")]
	[DataRow(false, DisplayName = "WithoutTheSynchronizationContext")]
	public void Dispose_WhileRequestInFlight_CancelsTheProviderToken(bool installSynchronizationContext)
	{
		var completion = new TaskCompletionSource<SignatureHelpInformation?>(TaskCreationOptions.RunContinuationsAsynchronously);
		using var host = new SignatureHelpTestHost(installSynchronizationContext) { RequestSignatureHelpAsync = (_, _, _) => completion.Task };

		SignatureHelpController controller = host.CreateController();

		Task request = controller.RequestAsync(5, cancellationToken: TestContext.CancellationToken);
		controller.Dispose();

		// Disposal cancels the in-flight provider request's token.
		Assert.IsTrue(host.RequestTokens[0].IsCancellationRequested);

		completion.TrySetResult(null);
		host.Dispatcher.PumpUntil(() => request.IsCompleted);
		request.GetAwaiter().GetResult();

		Assert.IsFalse(controller.CurrentPresentation.IsPresentationVisibleOrRequestScheduled);
	}

	[TestMethod]
	[DataRow(true, DisplayName = "WithTheSynchronizationContext")]
	[DataRow(false, DisplayName = "WithoutTheSynchronizationContext")]
	public async Task CancelInFlightRequest_WhileRequestInFlight_CancelsTheTokenAndKeepsThePresentation(bool installSynchronizationContext)
	{
		var firstCompletion = new TaskCompletionSource<SignatureHelpInformation?>(TaskCreationOptions.RunContinuationsAsynchronously);
		var secondCompletion = new TaskCompletionSource<SignatureHelpInformation?>(TaskCreationOptions.RunContinuationsAsynchronously);

		int requestCount = 0;

		using var host = new SignatureHelpTestHost(installSynchronizationContext)
		{
			RequestSignatureHelpAsync = (_, _, _) =>
				++requestCount == 1 ? Task.FromResult<SignatureHelpInformation?>(CreateDefaultSignatureHelp())
				: requestCount == 2 ? firstCompletion.Task
				: secondCompletion.Task,
		};

		SignatureHelpController controller = host.CreateController();

		await controller.RequestAsync(5, cancellationToken: TestContext.CancellationToken);
		Assert.IsTrue(controller.CurrentPresentation.IsVisible);

		// A second request is in flight; canceling it must cancel the provider token without clearing the
		// visible presentation or a scheduled request.
		Task secondRequest = controller.RequestAsync(9, cancellationToken: TestContext.CancellationToken);

		controller.ScheduleRequest();
		Assert.IsTrue(controller.CurrentPresentation.IsRequestScheduled);

		controller.CancelInFlightRequest();

		Assert.IsTrue(host.RequestTokens[1].IsCancellationRequested);
		Assert.IsTrue(controller.CurrentPresentation.IsVisible);
		Assert.IsTrue(controller.CurrentPresentation.IsRequestScheduled);

		// The canceled request's late result is rejected.
		firstCompletion.TrySetResult(CreateDefaultSignatureHelp());
		host.Dispatcher.PumpUntil(() => secondRequest.IsCompleted);
		secondRequest.GetAwaiter().GetResult();

		Assert.HasCount(1, host.ShownSignatures);

		// A request started after the cancellation observes a fresh, uncanceled token.
		Task thirdRequest = controller.RequestAsync(12, cancellationToken: TestContext.CancellationToken);

		Assert.IsFalse(host.RequestTokens[2].IsCancellationRequested);

		secondCompletion.TrySetResult(CreateDefaultSignatureHelp());
		host.Dispatcher.PumpUntil(() => thirdRequest.IsCompleted);
		thirdRequest.GetAwaiter().GetResult();

		controller.Dispose();
	}

	[TestMethod]
	public async Task RequestAsync_ProviderReturnsSignature_ShowsSignatureHelp()
	{
		SignatureHelpInformation signature = CreateDefaultSignatureHelp();
		using var host = new SignatureHelpTestHost { RequestSignatureHelpAsync = (_, _, _) => Task.FromResult<SignatureHelpInformation?>(signature) };

		using SignatureHelpController controller = host.CreateController();
		await controller.RequestAsync(5, cancellationToken: TestContext.CancellationToken);

		Assert.HasCount(1, host.ShownSignatures);
		Assert.AreSame(signature, host.ShownSignatures[0]);
		Assert.IsTrue(controller.CurrentPresentation.IsVisible);
		Assert.AreSame(signature, controller.CurrentPresentation.SignatureHelp);
	}

	[TestMethod]
	[DataRow(true, DisplayName = "WithTheSynchronizationContext")]
	[DataRow(false, DisplayName = "WithoutTheSynchronizationContext")]
	public void RequestAsync_SupersedingRequest_CancelsEarlierProviderTokenAndAppliesOnlyTheNewestResult(bool installSynchronizationContext)
	{
		var firstCompletion = new TaskCompletionSource<SignatureHelpInformation?>(TaskCreationOptions.RunContinuationsAsynchronously);
		var secondCompletion = new TaskCompletionSource<SignatureHelpInformation?>(TaskCreationOptions.RunContinuationsAsynchronously);
		var secondSignature = new SignatureHelpInformation([new SignatureInformation("f(a, b)")]);

		int requestCount = 0;

		using var host = new SignatureHelpTestHost(installSynchronizationContext)
		{
			RequestSignatureHelpAsync = (_, _, _) => ++requestCount == 1 ? firstCompletion.Task : secondCompletion.Task,
		};

		using SignatureHelpController controller = host.CreateController();

		Task firstRequest = controller.RequestAsync(5, cancellationToken: TestContext.CancellationToken);
		Task secondRequest = controller.RequestAsync(9, cancellationToken: TestContext.CancellationToken);

		// A superseding request cancels the earlier provider token and owns the in-flight state; no queued
		// scheduled request delays it.
		Assert.IsTrue(host.RequestTokens[0].IsCancellationRequested);
		Assert.IsFalse(host.RequestTokens[1].IsCancellationRequested);
		Assert.IsTrue(controller.CurrentPresentation.IsRequestInFlight);
		Assert.IsFalse(controller.CurrentPresentation.IsRequestScheduled);

		// The earlier in-flight result is ignored even though its provider ignored the cancellation.
		firstCompletion.TrySetResult(CreateDefaultSignatureHelp());
		host.Dispatcher.PumpUntil(() => firstRequest.IsCompleted);
		firstRequest.GetAwaiter().GetResult();

		Assert.IsFalse(controller.CurrentPresentation.IsVisible);

		secondCompletion.TrySetResult(secondSignature);
		host.Dispatcher.PumpUntil(() => secondRequest.IsCompleted);
		secondRequest.GetAwaiter().GetResult();

		Assert.HasCount(1, host.ShownSignatures);
		Assert.AreSame(secondSignature, host.ShownSignatures[0]);
		Assert.IsFalse(controller.CurrentPresentation.IsRequestInFlight);
	}

	[TestMethod]
	[DataRow(true, DisplayName = "WithTheSynchronizationContext")]
	[DataRow(false, DisplayName = "WithoutTheSynchronizationContext")]
	public void RequestAsync_InvalidateThenNewRequest_StaleCompletionKeepsNewerRequestInFlight(bool installSynchronizationContext)
	{
		var firstCompletion = new TaskCompletionSource<SignatureHelpInformation?>(TaskCreationOptions.RunContinuationsAsynchronously);
		var secondCompletion = new TaskCompletionSource<SignatureHelpInformation?>(TaskCreationOptions.RunContinuationsAsynchronously);
		var thirdCompletion = new TaskCompletionSource<SignatureHelpInformation?>(TaskCreationOptions.RunContinuationsAsynchronously);

		int requestCount = 0;

		using var host = new SignatureHelpTestHost(installSynchronizationContext)
		{
			RequestSignatureHelpAsync = (_, _, _) =>
				++requestCount switch
				{
					1 => firstCompletion.Task,
					2 => secondCompletion.Task,
					_ => thirdCompletion.Task,
				},
		};

		SignatureHelpController controller = host.CreateController();

		Task firstRequest = controller.RequestAsync(5, cancellationToken: TestContext.CancellationToken);

		// Invalidate the in-flight request, then start a newer one before the old one completes.
		controller.InvalidateRequests();
		Task secondRequest = controller.RequestAsync(9, cancellationToken: TestContext.CancellationToken);

		Assert.IsTrue(controller.CurrentPresentation.IsRequestInFlight);

		// A stale completion must not clear the newer request's in-flight flag.
		firstCompletion.TrySetResult(null);
		host.Dispatcher.PumpUntil(() => firstRequest.IsCompleted);
		firstRequest.GetAwaiter().GetResult();

		Assert.IsTrue(
			controller.CurrentPresentation.IsRequestInFlight,
			"The invalidated request must not clear the newer request's in-flight state."
		);

		// A third request supersedes the second one instead of waiting for it.
		Task thirdRequest = controller.RequestAsync(12, cancellationToken: TestContext.CancellationToken);

		Assert.IsTrue(controller.CurrentPresentation.IsRequestInFlight);
		Assert.IsFalse(controller.CurrentPresentation.IsRequestScheduled);
		Assert.AreEqual(3, requestCount);

		secondCompletion.TrySetResult(null);
		thirdCompletion.TrySetResult(null);

		host.Dispatcher.PumpUntil(() => secondRequest.IsCompleted && thirdRequest.IsCompleted);

		secondRequest.GetAwaiter().GetResult();
		thirdRequest.GetAwaiter().GetResult();

		Assert.IsFalse(controller.CurrentPresentation.IsPresentationVisibleOrRequestScheduled);

		controller.Dispose();
	}

	[TestMethod]
	public void InvalidateRequests_OffThread_ThrowsInvalidOperationException()
	{
		using var host = new SignatureHelpTestHost();
		using SignatureHelpController controller = host.CreateController();

		// InvalidateRequests publishes presentation state, so it verifies the dispatcher affinity like the
		// other interactive members; a dedicated thread is not the dispatcher thread.
		Exception? failure = CaptureOffThreadFailure(controller.InvalidateRequests);

		Assert.IsInstanceOfType<InvalidOperationException>(failure);
	}

	[TestMethod]
	public void ScheduleRequest_OffThread_ThrowsInvalidOperationException()
	{
		using var host = new SignatureHelpTestHost();
		using SignatureHelpController controller = host.CreateController();

		// ScheduleRequest reads the caret of the editor, which is thread-affine, so it verifies the dispatcher affinity.
		Exception? failure = CaptureOffThreadFailure(() => controller.ScheduleRequest());

		Assert.IsInstanceOfType<InvalidOperationException>(failure);
	}

	[TestMethod]
	public void InteractiveMembers_OffThread_ThrowInvalidOperationException()
	{
		using var host = new SignatureHelpTestHost();
		using SignatureHelpController controller = host.CreateController();

		(string Name, Action Call)[] calls =
		[
			("Dismiss", controller.Dismiss),
			("RequestAsync", () => _ = controller.RequestAsync(5, cancellationToken: TestContext.CancellationToken)),
			("SelectNextSignature", () => _ = controller.SelectNextSignature()),
			("SelectPreviousSignature", () => _ = controller.SelectPreviousSignature()),
		];

		foreach ((string name, Action call) in calls)
			Assert.IsInstanceOfType<InvalidOperationException>(CaptureOffThreadFailure(call), name);
	}

	[TestMethod]
	public void PublicOperations_FromAnotherThreadAfterDisposal_AreQuietNoOps()
	{
		using var host = new SignatureHelpTestHost
		{
			RequestSignatureHelpAsync = (_, _, _) => Task.FromResult<SignatureHelpInformation?>(CreateDefaultSignatureHelp()),
		};

		SignatureHelpController controller = host.CreateController();
		controller.Dispose();

		// After disposal every member returns before the dispatcher-affinity check, so the calls are safe
		// from any thread and none of them throws.
		Exception? failure = CaptureOffThreadFailure(() =>
		{
			controller.Dismiss();
			controller.ScheduleRequest();
			controller.CancelScheduledRequest();
			controller.InvalidateRequests();
			controller.CancelInFlightRequest();
			_ = controller.RequestAsync(5, cancellationToken: TestContext.CancellationToken);
			_ = controller.SelectNextSignature();
			_ = controller.SelectPreviousSignature();
		});

		Assert.IsNull(failure);
	}

	// Starts a request whose provider answers on a thread-pool thread and waits until the controller's publication of that answer is queued on the
	// dispatcher, so the test can interrupt the request in the window between the coordinator's checks and the dispatched step.
	private StaleResultScenario StartStaleResultScenario(SignatureHelpTestHost host, Func<Task<SignatureHelpInformation?>>? onSecondRequest = null)
	{
		var completion = new TaskCompletionSource<SignatureHelpInformation?>(TaskCreationOptions.RunContinuationsAsynchronously);

		int requestCount = 0;

		host.RequestSignatureHelpAsync = (_, _, _) => ++requestCount == 1 ? completion.Task : onSecondRequest?.Invoke() ?? completion.Task;

		SignatureHelpController controller = host.CreateController();
		Task<bool> request = controller.RequestAsync(5, cancellationToken: TestContext.CancellationToken);

		// Without a synchronization context the result resumes on a thread-pool thread, whose publication waits in the dispatcher's queue.
		completion.TrySetResult(CreateDefaultSignatureHelp());

		Assert.IsTrue(SpinWait.SpinUntil(() => host.Dispatcher.QueuedCount == 1, TimeSpan.FromSeconds(5.0)));

		return new StaleResultScenario(controller, request);
	}

	// A dedicated thread runs genuinely off the controller's dispatcher thread, and it carries no synchronization context.
	private static Exception? CaptureOffThreadFailure(Action action)
	{
		Exception? failure = null;

		var thread = new Thread(() =>
		{
			try
			{
				action();
			}
			catch (Exception exception)
			{
				failure = exception;
			}
		});

		thread.Start();
		thread.Join();

		return failure;
	}

	private readonly record struct StaleResultScenario(SignatureHelpController Controller, Task<bool> Request);
}
