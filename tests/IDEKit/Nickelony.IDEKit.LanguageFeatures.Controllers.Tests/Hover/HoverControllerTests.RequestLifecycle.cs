using Nickelony.IDEKit.LanguageFeatures.Controllers.Hover;
using Nickelony.IDEKit.LanguageFeatures.Diagnostics;
using Nickelony.IDEKit.LanguageFeatures.Hover;
using Nickelony.Testing;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Tests;

/// <summary>
/// Covers the hover controller's request lifecycle: how cancel, invalidate, dispose, and a newer evaluation supersede an in-flight hover request,
/// and how the request-offset and liveness hooks decide whether a completed result still belongs to the current request.
/// </summary>
public sealed partial class HoverControllerTests
{
	[TestMethod]
	public void RequestAsync_PointerStaysAtTheOffset_DisplaysTheCompletedResult()
	{
		var completion = new TaskCompletionSource<HoverInformation?>(TaskCreationOptions.RunContinuationsAsynchronously);
		using var host = new HoverTestHost { RequestHoverAsync = (_, _) => completion.Task };

		using HoverController controller = host.CreateController();

		Task hoverTask = controller.RequestAsync();

		completion.TrySetResult(new HoverInformation("hover") { SymbolName = "symbol" });
		host.Dispatcher.PumpUntil(() => hoverTask.IsCompleted);
		hoverTask.GetAwaiter().GetResult();

		// The controller asks the source at the request and again when the result is about to be shown.
		Assert.HasCount(1, host.DisplayCalls);
		Assert.AreEqual("hover", host.DisplayCalls[0].HoverInfo?.Content);
		Assert.AreEqual(2, host.PointerSource.ReadCount);
	}

	[TestMethod]
	public void RequestAsync_PointerLeavesWhileRequestIsInFlight_RejectsTheCompletedResult()
	{
		var completion = new TaskCompletionSource<HoverInformation?>(TaskCreationOptions.RunContinuationsAsynchronously);
		using var host = new HoverTestHost { RequestHoverAsync = (_, _) => completion.Task };

		using HoverController controller = host.CreateController();

		Task hoverTask = controller.RequestAsync();

		// The source reports no offset once the pointer left the editor while the request was in flight.
		host.PointerSource.Offset = static () => null;

		completion.TrySetResult(new HoverInformation("hover") { SymbolName = "symbol" });
		host.Dispatcher.PumpUntil(() => hoverTask.IsCompleted);
		hoverTask.GetAwaiter().GetResult();

		// A pointer that left the editor while the request was in flight rejects the completed result.
		Assert.AreSequenceEqual(Array.Empty<(HoverInformation?, Diagnostic?)>(), host.DisplayCalls);
	}

	[TestMethod]
	public void CancelInFlightRequest_WhileRequestInFlight_CancelsTheTokenAndSuppressesTheResult()
	{
		var completion = new TaskCompletionSource<HoverInformation?>(TaskCreationOptions.RunContinuationsAsynchronously);
		using var host = new HoverTestHost { RequestHoverAsync = (_, _) => completion.Task };

		using HoverController controller = host.CreateController();

		Task hoverTask = controller.RequestAsync();
		controller.CancelInFlightRequest();

		// The provider observes the cancellation through the token it received, and the completed result is
		// rejected even though the provider ignored the cancellation.
		Assert.IsTrue(host.RequestTokens[0].IsCancellationRequested);

		completion.TrySetResult(new HoverInformation("hover") { SymbolName = "symbol" });
		host.Dispatcher.PumpUntil(() => hoverTask.IsCompleted);
		hoverTask.GetAwaiter().GetResult();

		Assert.AreSequenceEqual(Array.Empty<(HoverInformation?, Diagnostic?)>(), host.DisplayCalls);
	}

	[TestMethod]
	public void InvalidateRequests_WhileRequestInFlight_RejectsTheResultWithoutCancelingTheToken()
	{
		var completion = new TaskCompletionSource<HoverInformation?>(TaskCreationOptions.RunContinuationsAsynchronously);
		using var host = new HoverTestHost { RequestHoverAsync = (_, _) => completion.Task };

		using HoverController controller = host.CreateController();

		Task hoverTask = controller.RequestAsync();
		controller.InvalidateRequests();

		// Invalidation rejects outstanding results without asking the provider to stop.
		Assert.IsFalse(host.RequestTokens[0].IsCancellationRequested);

		completion.TrySetResult(new HoverInformation("hover") { SymbolName = "symbol" });
		host.Dispatcher.PumpUntil(() => hoverTask.IsCompleted);
		hoverTask.GetAwaiter().GetResult();

		Assert.AreSequenceEqual(Array.Empty<(HoverInformation?, Diagnostic?)>(), host.DisplayCalls);
	}

	[TestMethod]
	public void Dispose_WhileRequestInFlight_CancelsTheTokenAndSuppressesTheResult()
	{
		var completion = new TaskCompletionSource<HoverInformation?>(TaskCreationOptions.RunContinuationsAsynchronously);
		using var host = new HoverTestHost { RequestHoverAsync = (_, _) => completion.Task };

		using HoverController controller = host.CreateController();

		Task hoverTask = controller.RequestAsync();
		controller.Dispose();

		// Disposal cancels pending work and a request that completes afterwards must not publish.
		Assert.IsTrue(host.RequestTokens[0].IsCancellationRequested);

		completion.TrySetResult(new HoverInformation("hover") { SymbolName = "symbol" });
		host.Dispatcher.PumpUntil(() => hoverTask.IsCompleted);
		hoverTask.GetAwaiter().GetResult();

		Assert.AreSequenceEqual(Array.Empty<(HoverInformation?, Diagnostic?)>(), host.DisplayCalls);
	}

	[TestMethod]
	public void Dispose_WhileRequestInFlight_ShowsAndLogsNothingWhenTheProviderFailsAfterwards()
	{
		var logger = new RecordingLogger();
		var completion = new TaskCompletionSource<HoverInformation?>(TaskCreationOptions.RunContinuationsAsynchronously);

		using var host = new HoverTestHost
		{
			BuildDecision = _ => CreateState(canShowDiagnosticFallback: true, diagnosticInfo: CreateDiagnostic()),
			RequestHoverAsync = (_, _) => completion.Task,
		};

		using HoverController controller = host.CreateController(logger);

		Task hoverTask = controller.RequestAsync();
		controller.Dispose();

		completion.TrySetException(new InvalidOperationException("Hover failed after the controller was disposed."));
		host.Dispatcher.PumpUntil(() => hoverTask.IsCompleted);
		hoverTask.GetAwaiter().GetResult();

		// The request belonged to a disposed controller, so its failure neither reaches the host nor the log.
		Assert.AreSequenceEqual(Array.Empty<(HoverInformation?, Diagnostic?)>(), host.DisplayCalls);
		Assert.AreSequenceEqual([], logger.Messages);
	}

	[TestMethod]
	public void CancelInFlightRequest_And_InvalidateRequests_AfterDisposal_AreNoOps()
	{
		using var host = new HoverTestHost();
		using HoverController controller = host.CreateController();

		controller.Dispose();
		controller.CancelInFlightRequest();
		controller.InvalidateRequests();

		// The post-disposal calls are no-ops: no request is started and no decision is displayed.
		Assert.AreSequenceEqual([], host.RequestOffsets);
		Assert.AreSequenceEqual(Array.Empty<(HoverInformation?, Diagnostic?)>(), host.DisplayCalls);
	}

	[TestMethod]
	public async Task RequestAsync_SupersededByNoOffset_CancelsTheInFlightTokenAndSuppressesTheResult()
	{
		var completion = new TaskCompletionSource<HoverInformation?>(TaskCreationOptions.RunContinuationsAsynchronously);
		using var host = new HoverTestHost { RequestHoverAsync = (_, _) => completion.Task };

		using HoverController controller = host.CreateController();

		Task firstTask = controller.RequestAsync();

		// The latest evaluation finds no hover target, which supersedes the in-flight request.
		host.PointerSource.Offset = static () => null;
		await controller.RequestAsync();

		Assert.IsTrue(host.RequestTokens[0].IsCancellationRequested);

		// The older result must not be published after the latest evaluation superseded it; the only display
		// decision is the hide request of the newest evaluation.
		completion.TrySetResult(new HoverInformation("hover") { SymbolName = "symbol" });
		host.Dispatcher.PumpUntil(() => firstTask.IsCompleted);
		firstTask.GetAwaiter().GetResult();

		Assert.HasCount(1, host.DisplayCalls);
		Assert.IsNull(host.DisplayCalls[0].HoverInfo);
		Assert.IsNull(host.DisplayCalls[0].Diagnostic);
	}

	[TestMethod]
	public async Task RequestAsync_SupersededBySuppressedHover_CancelsTheInFlightTokenAndReportsHide()
	{
		var completion = new TaskCompletionSource<HoverInformation?>(TaskCreationOptions.RunContinuationsAsynchronously);
		using var host = new HoverTestHost { RequestHoverAsync = (_, _) => completion.Task };

		using HoverController controller = host.CreateController();

		Task firstTask = controller.RequestAsync();

		// The latest evaluation decides that no request should be made, which supersedes the in-flight one.
		host.BuildDecision = _ => CreateState(shouldRequestHover: false, requestOffset: -1, canShowHoverContent: false);
		await controller.RequestAsync();

		Assert.IsTrue(host.RequestTokens[0].IsCancellationRequested);

		completion.TrySetResult(new HoverInformation("hover") { SymbolName = "symbol" });
		host.Dispatcher.PumpUntil(() => firstTask.IsCompleted);
		firstTask.GetAwaiter().GetResult();

		// The older result must not be published; the newest evaluation reports the hide decision.
		Assert.HasCount(1, host.DisplayCalls);
		Assert.IsNull(host.DisplayCalls[0].HoverInfo);
		Assert.IsNull(host.DisplayCalls[0].Diagnostic);
	}

	[TestMethod]
	public async Task RequestAsync_SupersededProviderFailure_DoesNotOverwriteTheNewerDisplayDecision()
	{
		var logger = new RecordingLogger();
		var firstCompletion = new TaskCompletionSource<HoverInformation?>(TaskCreationOptions.RunContinuationsAsynchronously);

		int requestCount = 0;

		using var host = new HoverTestHost
		{
			BuildDecision = _ => CreateState(canShowDiagnosticFallback: true, diagnosticInfo: CreateDiagnostic()),

			RequestHoverAsync = (_, _) =>
				++requestCount == 1
					? firstCompletion.Task
					: Task.FromResult<HoverInformation?>(new HoverInformation("second") { SymbolName = "second" }),
		};

		using HoverController controller = host.CreateController(logger);

		Task firstTask = controller.RequestAsync();

		// The second hover admits a newer request, which supersedes the first one and publishes its own
		// display decision.
		await controller.RequestAsync();

		firstCompletion.SetException(new InvalidOperationException("Stale hover failure."));
		host.Dispatcher.PumpUntil(() => firstTask.IsCompleted);
		firstTask.GetAwaiter().GetResult();

		// The superseded failure must not replace the newer hover content with the diagnostic fallback; it
		// is only logged.
		Assert.HasCount(1, host.DisplayCalls);
		Assert.AreEqual("second", host.DisplayCalls[0].HoverInfo?.Content);
		Assert.HasCount(1, logger.Entries);
		Assert.AreEqual(1010, logger.Entries[0].EventId.Id);
	}

	[TestMethod]
	public void RequestAsync_RequestOffsetHookReturningNull_DiscardsTheCompletedResult()
	{
		var completion = new TaskCompletionSource<HoverInformation?>(TaskCreationOptions.RunContinuationsAsynchronously);
		bool vetoRequestOffset = false;

		using var host = new HoverTestHost
		{
			RequestHoverAsync = (_, _) => completion.Task,
			ResolveRequestOffset = _ => vetoRequestOffset ? null : 5,
		};

		using HoverController controller = host.CreateController();

		Task hoverTask = controller.RequestAsync();

		// The host vetoes the position while the request is in flight; the null answer must not fall
		// back to the raw hovered offset, which equals the request offset here.
		vetoRequestOffset = true;
		completion.TrySetResult(new HoverInformation("hover") { SymbolName = "symbol" });
		host.Dispatcher.PumpUntil(() => hoverTask.IsCompleted);
		hoverTask.GetAwaiter().GetResult();

		// A rejected result produces no display decision at all, so the previous tooltip state is untouched.
		Assert.AreSequenceEqual(
			Array.Empty<(HoverInformation?, Diagnostic?)>(),
			host.DisplayCalls,
			string.Join("; ", host.DisplayCalls.Select(call => $"{call.HoverInfo?.Content ?? "<null>"}|{call.Diagnostic?.Message ?? "<null>"}"))
		);
	}

	[TestMethod]
	public async Task RequestAsync_RequestOffsetHookRemappingTheOffset_DiscardsTheCompletedResult()
	{
		using var host = new HoverTestHost { ResolveRequestOffset = static _ => 9 };

		using HoverController controller = host.CreateController();
		await controller.RequestAsync();

		// The hook remaps the hovered offset to a different request offset, so the completed result does not
		// match the current request.
		Assert.AreSequenceEqual(Array.Empty<(HoverInformation?, Diagnostic?)>(), host.DisplayCalls);
	}

	[TestMethod]
	public async Task RequestAsync_RequestOffsetHookRemappingTwoDistinctOffsets_RebuildsTheDisplayState()
	{
		int offsetCalls = 0;

		using var host = new HoverTestHost
		{
			ResolveRequestOffset = static _ => 7,

			BuildDecision = offset =>
				offset == 5
					? CreateState(requestOffset: 7, canShowDiagnosticFallback: true, diagnosticInfo: CreateDiagnostic("first"))
					: CreateState(requestOffset: 7, canShowDiagnosticFallback: true, diagnosticInfo: CreateDiagnostic("second")),
		};

		// The initial evaluation hovers offset 5; the liveness re-check reports offset 9, which the
		// request-offset hook maps to the same request offset as offset 5.
		host.PointerSource.Offset = () => offsetCalls++ == 0 ? 5 : 9;

		using HoverController controller = host.CreateController();
		await controller.RequestAsync();

		// The pointer moved to a different hovered offset that maps to the same request, so the display
		// state is rebuilt from the latest evaluation instead of reusing the earlier state.
		Assert.HasCount(1, host.DisplayCalls);
		Assert.AreEqual("hover", host.DisplayCalls[0].HoverInfo?.Content);
		Assert.AreEqual("second", host.DisplayCalls[0].Diagnostic?.Message);
	}

	[TestMethod]
	public async Task RequestAsync_RemappedRequestOffsetWithoutHook_LogsWarningOnceWithEventId()
	{
		var logger = new RecordingLogger();

		using var host = new HoverTestHost
		{
			BuildDecision = _ => CreateState(requestOffset: 7),
			RequestHoverAsync = static (_, _) => Task.FromResult<HoverInformation?>(null),
		};

		using HoverController controller = host.CreateController(logger);

		await controller.RequestAsync();
		await controller.RequestAsync();

		// A remapped request offset without the matching hook can never match the pointer position, so
		// the host is warned once with the documented hover event id instead of silently discarding every
		// completed result.
		Assert.HasCount(1, logger.Entries);
		Assert.AreEqual(Microsoft.Extensions.Logging.LogLevel.Warning, logger.Entries[0].Level);
		Assert.AreEqual(1012, logger.Entries[0].EventId.Id);
		Assert.Contains("ResolveRequestOffset", logger.Messages[0]);
	}

	[TestMethod]
	public void RequestAsync_AsyncProviderWithoutSynchronizationContext_DisplaysOnTheDispatcherThread()
	{
		var providerGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var providerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		using var host = new HoverTestHost(installSynchronizationContext: false)
		{
			RequestHoverAsync = async (_, _) =>
			{
				providerStarted.TrySetResult();
				await providerGate.Task.ConfigureAwait(false);
				return new HoverInformation("hover") { SymbolName = "symbol" };
			},
		};

		using HoverController controller = host.CreateController();

		// The dispatcher carries no synchronization context here, so the provider continuation resumes on a
		// thread-pool thread; the pointer resolution and the display callback must still run on the
		// dispatcher's thread instead of touching pointer and tooltip state from there.
		Task hoverTask = controller.RequestAsync();

		// Wait on the provider's start signal instead of a fixed pump window, so the gate is released only
		// once the request is genuinely in flight.
		host.Dispatcher.PumpUntil(() => providerStarted.Task.IsCompleted);
		providerGate.SetResult();
		host.Dispatcher.PumpUntil(() => hoverTask.IsCompleted);

		Assert.HasCount(1, host.DisplayCalls);
		Assert.AreEqual(host.Dispatcher.OwnerThreadId, host.DisplayThreadIds[0]);
	}

	[TestMethod]
	public void RequestAsync_ContextVersionChangedWhileRequestInFlight_DoesNotShowTheHover()
	{
		int contextVersion = 1;
		var completion = new TaskCompletionSource<HoverInformation?>(TaskCreationOptions.RunContinuationsAsynchronously);

		using var host = new HoverTestHost { RequestHoverAsync = (_, _) => completion.Task, ContextVersionProvider = () => contextVersion };

		using HoverController controller = host.CreateController();

		Task hoverTask = controller.RequestAsync();

		// The host context version changes while the request is in flight, so the completed result
		// belongs to a superseded context and must not be published.
		contextVersion = 2;
		completion.TrySetResult(new HoverInformation("hover") { SymbolName = "symbol" });
		host.Dispatcher.PumpUntil(() => hoverTask.IsCompleted);
		hoverTask.GetAwaiter().GetResult();

		Assert.AreSequenceEqual(Array.Empty<(HoverInformation?, Diagnostic?)>(), host.DisplayCalls);
	}

	[TestMethod]
	public void RequestAsync_DisposalLandsBeforeTheQueuedPublication_ShowsNothing()
	{
		using var host = new HoverTestHost(installSynchronizationContext: false);

		StaleResultScenario scenario = StartStaleResultScenario(host);

		// The disposal lands between the coordinator's checks and the queued step, which must not show a tooltip after it.
		scenario.Controller.Dispose();
		host.Dispatcher.PumpUntil(() => scenario.Request.IsCompleted);

		Assert.AreSequenceEqual(Array.Empty<(HoverInformation?, Diagnostic?)>(), host.DisplayCalls);
	}

	[TestMethod]
	public void RequestAsync_CancellationLandsBeforeTheQueuedPublication_ShowsNothing()
	{
		using var host = new HoverTestHost(installSynchronizationContext: false);

		StaleResultScenario scenario = StartStaleResultScenario(host);

		// A cancellation in the same window must not be undone by the result that had already passed the coordinator's checks.
		scenario.Controller.CancelInFlightRequest();
		host.Dispatcher.PumpUntil(() => scenario.Request.IsCompleted);

		Assert.AreSequenceEqual(Array.Empty<(HoverInformation?, Diagnostic?)>(), host.DisplayCalls);

		scenario.Controller.Dispose();
	}

	[TestMethod]
	public void RequestAsync_InvalidationLandsBeforeTheQueuedPublication_ShowsNothing()
	{
		using var host = new HoverTestHost(installSynchronizationContext: false);

		StaleResultScenario scenario = StartStaleResultScenario(host);

		scenario.Controller.InvalidateRequests();
		host.Dispatcher.PumpUntil(() => scenario.Request.IsCompleted);

		Assert.AreSequenceEqual(Array.Empty<(HoverInformation?, Diagnostic?)>(), host.DisplayCalls);

		scenario.Controller.Dispose();
	}

	[TestMethod]
	public void RequestAsync_SuppressedHoverDecisionLandsBeforeTheQueuedPublication_ShowsOnlyTheHide()
	{
		using var host = new HoverTestHost(installSynchronizationContext: false);

		StaleResultScenario scenario = StartStaleResultScenario(host);

		// The pointer is still over the same offset when the queued step reads it, so only the supersession can reject the result: the
		// newest evaluation decided that no request is made and reported its hide decision, which the older result must not undo.
		host.BuildDecision = _ => CreateState(shouldRequestHover: false, requestOffset: -1, canShowHoverContent: false);
		_ = scenario.Controller.RequestAsync();
		host.Dispatcher.PumpUntil(() => scenario.Request.IsCompleted);

		Assert.HasCount(1, host.DisplayCalls);
		Assert.IsNull(host.DisplayCalls[0].HoverInfo);
		Assert.IsNull(host.DisplayCalls[0].Diagnostic);

		scenario.Controller.Dispose();
	}

	[TestMethod]
	public void RequestAsync_NewerRequestStartsBeforeTheQueuedPublication_ShowsOnlyTheNewerResult()
	{
		var secondCompletion = new TaskCompletionSource<HoverInformation?>(TaskCreationOptions.RunContinuationsAsynchronously);
		var second = new HoverInformation("second") { SymbolName = "second" };

		using var host = new HoverTestHost(installSynchronizationContext: false);

		StaleResultScenario scenario = StartStaleResultScenario(host, onSecondRequest: () => secondCompletion.Task);

		// The newer request supersedes the result that waits in the queue; only the newer result may show.
		Task secondRequest = scenario.Controller.RequestAsync();

		secondCompletion.TrySetResult(second);
		host.Dispatcher.PumpUntil(() => scenario.Request.IsCompleted && secondRequest.IsCompleted);

		Assert.HasCount(1, host.DisplayCalls);
		Assert.AreSame(second, host.DisplayCalls[0].HoverInfo);

		scenario.Controller.Dispose();
	}

	[TestMethod]
	public void RequestAsync_CancellationLandsBeforeTheQueuedFailureFallback_ShowsNothing()
	{
		var logger = new RecordingLogger();

		using var host = new HoverTestHost(installSynchronizationContext: false)
		{
			BuildDecision = _ => CreateState(canShowDiagnosticFallback: true, diagnosticInfo: CreateDiagnostic()),
		};

		StaleResultScenario scenario = StartStaleResultScenario(host, failure: new InvalidOperationException("Hover failed."), logger: logger);

		// The failure passed the coordinator's checks and its fallback waits in the queue; a cancellation in the window rejects it.
		scenario.Controller.CancelInFlightRequest();
		host.Dispatcher.PumpUntil(() => scenario.Request.IsCompleted);

		Assert.AreSequenceEqual(Array.Empty<(HoverInformation?, Diagnostic?)>(), host.DisplayCalls);
		Assert.HasCount(1, logger.Entries);
		Assert.AreEqual(1010, logger.Entries[0].EventId.Id);

		scenario.Controller.Dispose();
	}

	[TestMethod]
	public void RequestAsync_DisposalLandsBeforeTheQueuedFailureFallback_ShowsNothing()
	{
		using var host = new HoverTestHost(installSynchronizationContext: false)
		{
			BuildDecision = _ => CreateState(canShowDiagnosticFallback: true, diagnosticInfo: CreateDiagnostic()),
		};

		StaleResultScenario scenario = StartStaleResultScenario(host, failure: new InvalidOperationException("Hover failed."));

		// The controller does not call the tooltip callback after disposal, even for a fallback that had already passed the coordinator's checks.
		scenario.Controller.Dispose();
		host.Dispatcher.PumpUntil(() => scenario.Request.IsCompleted);

		Assert.AreSequenceEqual(Array.Empty<(HoverInformation?, Diagnostic?)>(), host.DisplayCalls);
	}

	[TestMethod]
	public void RequestAsync_FailureFallbackQueuedWhileCurrent_ShowsTheFallbackOnTheDispatcherThread()
	{
		using var host = new HoverTestHost(installSynchronizationContext: false)
		{
			BuildDecision = _ => CreateState(canShowDiagnosticFallback: true, diagnosticInfo: CreateDiagnostic()),
		};

		StaleResultScenario scenario = StartStaleResultScenario(host, failure: new InvalidOperationException("Hover failed."));

		// Nothing interrupts the request, so the queued fallback is the display decision and it runs where the controller lives.
		host.Dispatcher.PumpUntil(() => scenario.Request.IsCompleted);

		Assert.HasCount(1, host.DisplayCalls);
		Assert.IsNull(host.DisplayCalls[0].HoverInfo);
		Assert.AreEqual("diagnostic", host.DisplayCalls[0].Diagnostic?.Message);
		Assert.AreEqual(host.Dispatcher.OwnerThreadId, host.DisplayThreadIds[0]);

		scenario.Controller.Dispose();
	}

	[TestMethod]
	public void RequestAsync_DispatcherShutsDownBeforeThePublication_CompletesWithoutFaulting()
	{
		using var host = new HoverTestHost(installSynchronizationContext: false);

		StaleResultScenario scenario = StartStaleResultScenario(host);

		// The dispatcher rejects the queued publication; the controller treats that as the end of the request.
		host.Dispatcher.Dispose();

		Assert.IsTrue(SpinWait.SpinUntil(() => scenario.Request.IsCompleted, TimeSpan.FromSeconds(5.0)));
		scenario.Request.GetAwaiter().GetResult();
		Assert.AreSequenceEqual(Array.Empty<(HoverInformation?, Diagnostic?)>(), host.DisplayCalls);

		scenario.Controller.Dispose();
	}

	// Starts a request whose provider answers on a thread-pool thread and waits until the controller's publication of that answer is queued on the
	// dispatcher, so the test can interrupt the request in the window between the coordinator's checks and the dispatched step. A failure makes the
	// provider fault instead of answering, which queues the fallback.
	private static StaleResultScenario StartStaleResultScenario(
		HoverTestHost host,
		Func<Task<HoverInformation?>>? onSecondRequest = null,
		Exception? failure = null,
		RecordingLogger? logger = null
	)
	{
		var completion = new TaskCompletionSource<HoverInformation?>(TaskCreationOptions.RunContinuationsAsynchronously);

		int requestCount = 0;

		host.RequestHoverAsync = (_, _) => ++requestCount == 1 ? completion.Task : onSecondRequest?.Invoke() ?? completion.Task;

		HoverController controller = host.CreateController(logger);
		Task request = controller.RequestAsync();

		// Without a synchronization context the result resumes on a thread-pool thread, whose publication waits in the dispatcher's queue.
		if (failure is null)
			completion.TrySetResult(new HoverInformation("hover") { SymbolName = "symbol" });
		else
			completion.TrySetException(failure);

		Assert.IsTrue(SpinWait.SpinUntil(() => host.Dispatcher.QueuedCount == 1, TimeSpan.FromSeconds(5.0)));

		return new StaleResultScenario(controller, request);
	}

	private readonly record struct StaleResultScenario(HoverController Controller, Task Request);
}
