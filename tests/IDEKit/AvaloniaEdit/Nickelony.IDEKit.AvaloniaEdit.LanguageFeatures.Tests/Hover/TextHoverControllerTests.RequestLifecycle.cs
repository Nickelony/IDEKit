using Avalonia;
using Nickelony.IDEKit.IntelliSense.Diagnostics;
using Nickelony.IDEKit.IntelliSense.Hover;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;

/// <summary>
/// Covers the hover controller's request lifecycle: how cancel, invalidate, dispose, and a newer
/// evaluation supersede an in-flight hover request, and how the request-offset and liveness hooks decide
/// whether a completed result still belongs to the current request.
/// </summary>
public sealed partial class TextHoverControllerTests
{
	[TestMethod]
	public void CancelInFlightRequest_WhileRequestInFlight_CancelsTheTokenAndSuppressesTheResult()
	{
		var completion = new TaskCompletionSource<TextHoverInfo?>(TaskCreationOptions.RunContinuationsAsynchronously);
		using var host = new HoverTestHost
		{
			RequestHoverAsync = (_, _) => completion.Task
		};

		using var controller = host.CreateController();

		Task hoverTask = controller.HandleMouseHoverAsync(HoverTestHost.CreateMouseEventArgs());
		controller.CancelInFlightRequest();

		// The provider observes the cancellation through the token it received, and the completed result is
		// rejected even though the provider ignored the cancellation.
		Assert.IsTrue(host.RequestTokens[0].IsCancellationRequested);

		completion.TrySetResult(new TextHoverInfo("hover") { SymbolName = "symbol" });
		DispatcherTestUtils.PumpUntil(() => hoverTask.IsCompleted);
		hoverTask.GetAwaiter().GetResult();

		CollectionAssert.AreEqual(Array.Empty<(TextHoverInfo?, TextDiagnostic?)>(), host.DisplayCalls);
	}

	[TestMethod]
	public void InvalidateRequests_WhileRequestInFlight_RejectsTheResultWithoutCancelingTheToken()
	{
		var completion = new TaskCompletionSource<TextHoverInfo?>(TaskCreationOptions.RunContinuationsAsynchronously);
		using var host = new HoverTestHost
		{
			RequestHoverAsync = (_, _) => completion.Task
		};

		using var controller = host.CreateController();

		Task hoverTask = controller.HandleMouseHoverAsync(HoverTestHost.CreateMouseEventArgs());
		controller.InvalidateRequests();

		// Invalidation rejects outstanding results without asking the provider to stop.
		Assert.IsFalse(host.RequestTokens[0].IsCancellationRequested);

		completion.TrySetResult(new TextHoverInfo("hover") { SymbolName = "symbol" });
		DispatcherTestUtils.PumpUntil(() => hoverTask.IsCompleted);
		hoverTask.GetAwaiter().GetResult();

		CollectionAssert.AreEqual(Array.Empty<(TextHoverInfo?, TextDiagnostic?)>(), host.DisplayCalls);
	}

	[TestMethod]
	public void Dispose_WhileRequestInFlight_CancelsTheTokenAndSuppressesTheResult()
	{
		var completion = new TaskCompletionSource<TextHoverInfo?>(TaskCreationOptions.RunContinuationsAsynchronously);
		using var host = new HoverTestHost
		{
			RequestHoverAsync = (_, _) => completion.Task
		};

		using var controller = host.CreateController();

		Task hoverTask = controller.HandleMouseHoverAsync(HoverTestHost.CreateMouseEventArgs());
		controller.Dispose();

		// Disposal cancels pending work and a request that completes afterwards must not publish.
		Assert.IsTrue(host.RequestTokens[0].IsCancellationRequested);

		completion.TrySetResult(new TextHoverInfo("hover") { SymbolName = "symbol" });
		DispatcherTestUtils.PumpUntil(() => hoverTask.IsCompleted);
		hoverTask.GetAwaiter().GetResult();

		CollectionAssert.AreEqual(Array.Empty<(TextHoverInfo?, TextDiagnostic?)>(), host.DisplayCalls);
	}

	[TestMethod]
	public void CancelInFlightRequest_And_InvalidateRequests_AfterDisposal_AreNoOps()
	{
		using var host = new HoverTestHost();
		using var controller = host.CreateController();

		controller.Dispose();
		controller.CancelInFlightRequest();
		controller.InvalidateRequests();

		// The post-disposal calls are no-ops: no request is started and no decision is displayed.
		CollectionAssert.AreEqual(Array.Empty<int>(), host.RequestOffsets);
		CollectionAssert.AreEqual(Array.Empty<(TextHoverInfo?, TextDiagnostic?)>(), host.DisplayCalls);
	}

	[TestMethod]
	public async Task HandleMouseHoverAsync_SupersededByNoOffset_CancelsTheInFlightTokenAndSuppressesTheResult()
	{
		var completion = new TaskCompletionSource<TextHoverInfo?>(TaskCreationOptions.RunContinuationsAsynchronously);
		using var host = new HoverTestHost
		{
			RequestHoverAsync = (_, _) => completion.Task
		};

		using var controller = host.CreateController();

		Task firstTask = controller.HandleMouseHoverAsync(HoverTestHost.CreateMouseEventArgs());

		// The latest evaluation finds no hover target, which supersedes the in-flight request.
		host.GetOffsetFromPoint = static _ => null;
		await controller.HandleMouseHoverAsync(HoverTestHost.CreateMouseEventArgs());

		Assert.IsTrue(host.RequestTokens[0].IsCancellationRequested);

		// The older result must not be published after the latest evaluation superseded it; the only display
		// decision is the hide request of the newest evaluation.
		completion.TrySetResult(new TextHoverInfo("hover") { SymbolName = "symbol" });
		DispatcherTestUtils.PumpUntil(() => firstTask.IsCompleted);
		firstTask.GetAwaiter().GetResult();

		Assert.AreEqual(1, host.DisplayCalls.Count);
		Assert.IsNull(host.DisplayCalls[0].HoverInfo);
		Assert.IsNull(host.DisplayCalls[0].DiagnosticInfo);
	}

	[TestMethod]
	public async Task HandleMouseHoverAsync_SupersededBySuppressedHover_CancelsTheInFlightTokenAndReportsHide()
	{
		var completion = new TaskCompletionSource<TextHoverInfo?>(TaskCreationOptions.RunContinuationsAsynchronously);
		using var host = new HoverTestHost
		{
			RequestHoverAsync = (_, _) => completion.Task
		};

		using var controller = host.CreateController();

		Task firstTask = controller.HandleMouseHoverAsync(HoverTestHost.CreateMouseEventArgs());

		// The latest evaluation decides that no request should be made, which supersedes the in-flight one.
		host.BuildEvaluationState = _ => CreateState(shouldRequestHover: false, requestOffset: -1, canShowHoverContent: false);
		await controller.HandleMouseHoverAsync(HoverTestHost.CreateMouseEventArgs());

		Assert.IsTrue(host.RequestTokens[0].IsCancellationRequested);

		completion.TrySetResult(new TextHoverInfo("hover") { SymbolName = "symbol" });
		DispatcherTestUtils.PumpUntil(() => firstTask.IsCompleted);
		firstTask.GetAwaiter().GetResult();

		// The older result must not be published; the newest evaluation reports the hide decision.
		Assert.AreEqual(1, host.DisplayCalls.Count);
		Assert.IsNull(host.DisplayCalls[0].HoverInfo);
		Assert.IsNull(host.DisplayCalls[0].DiagnosticInfo);
	}

	[TestMethod]
	public async Task HandleMouseHoverAsync_SupersededProviderFailure_DoesNotOverwriteTheNewerDisplayDecision()
	{
		var logger = new CapturingLogger();
		var firstCompletion = new TaskCompletionSource<TextHoverInfo?>(TaskCreationOptions.RunContinuationsAsynchronously);
		int requestCount = 0;
		using var host = new HoverTestHost
		{
			BuildEvaluationState = _ => CreateState(canShowDiagnosticFallback: true, diagnosticInfo: CreateDiagnostic()),
			RequestHoverAsync = (_, _) => ++requestCount == 1
				? firstCompletion.Task
				: Task.FromResult<TextHoverInfo?>(new TextHoverInfo("second") { SymbolName = "second" })
		};

		using var controller = host.CreateController(logger);

		Task firstTask = controller.HandleMouseHoverAsync(HoverTestHost.CreateMouseEventArgs());

		// The second hover admits a newer request, which supersedes the first one and publishes its own
		// display decision.
		await controller.HandleMouseHoverAsync(HoverTestHost.CreateMouseEventArgs());

		firstCompletion.SetException(new InvalidOperationException("Stale hover failure."));
		DispatcherTestUtils.PumpUntil(() => firstTask.IsCompleted);
		firstTask.GetAwaiter().GetResult();

		// The superseded failure must not replace the newer hover content with the diagnostic fallback; it
		// is only logged.
		Assert.AreEqual(1, host.DisplayCalls.Count);
		Assert.AreEqual("second", host.DisplayCalls[0].HoverInfo?.Content);
		Assert.AreEqual(1, logger.Entries.Count);
		Assert.AreEqual(1010, logger.Entries[0].EventId.Id);
	}

	[TestMethod]
	public void HandleMouseHoverAsync_RequestOffsetHookReturningNull_DiscardsTheCompletedResult()
	{
		var completion = new TaskCompletionSource<TextHoverInfo?>(TaskCreationOptions.RunContinuationsAsynchronously);
		bool vetoRequestOffset = false;

		using var host = new HoverTestHost
		{
			RequestHoverAsync = (_, _) => completion.Task,
			ResolveRequestOffset = _ => vetoRequestOffset ? null : 5,

			// The pointer hook keeps the liveness re-check away from an ambient mouse position, which
			// Avalonia does not expose; the provider continuation may resume on a pool thread.
			GetCurrentPointerPosition = static () => new Point(0.0, 0.0)
		};

		using var controller = host.CreateController();

		Task hoverTask = controller.HandleMouseHoverAsync(HoverTestHost.CreateMouseEventArgs());

		// The host vetoes the position while the request is in flight; the null answer must not fall
		// back to the raw hovered offset, which equals the request offset here.
		vetoRequestOffset = true;
		completion.TrySetResult(new TextHoverInfo("hover") { SymbolName = "symbol" });
		DispatcherTestUtils.PumpUntil(() => hoverTask.IsCompleted);
		hoverTask.GetAwaiter().GetResult();

		// A rejected result produces no display decision at all, so the previous tooltip state is untouched.
		CollectionAssert.AreEqual(
			Array.Empty<(TextHoverInfo?, TextDiagnostic?)>(),
			host.DisplayCalls,
			string.Join("; ", host.DisplayCalls.Select(call => $"{(call.HoverInfo?.Content ?? "<null>")}|{(call.DiagnosticInfo?.Message ?? "<null>")}")));
	}

	[TestMethod]
	public async Task HandleMouseHoverAsync_RequestOffsetHookRemappingTheOffset_DiscardsTheCompletedResult()
	{
		using var host = new HoverTestHost
		{
			ResolveRequestOffset = static _ => 9
		};

		using var controller = host.CreateController();
		await controller.HandleMouseHoverAsync(HoverTestHost.CreateMouseEventArgs());

		// The hook remaps the hovered offset to a different request offset, so the completed result no
		// longer matches the current request.
		CollectionAssert.AreEqual(Array.Empty<(TextHoverInfo?, TextDiagnostic?)>(), host.DisplayCalls);
	}

	[TestMethod]
	public async Task HandleMouseHoverAsync_RequestOffsetHookRemappingTwoDistinctOffsets_RebuildsTheDisplayState()
	{
		int offsetCalls = 0;

		using var host = new HoverTestHost
		{
			// The initial evaluation hovers offset 5; the liveness re-check reports offset 9, which the
			// request-offset hook maps to the same request offset as offset 5.
			GetOffsetFromPoint = _ => offsetCalls++ == 0 ? 5 : 9,
			ResolveRequestOffset = static _ => 7,

			// Avalonia has no ambient pointer position, so the pointer-position hook is what drives the
			// liveness re-check; without it the controller reuses the captured hovered offset instead of
			// re-resolving the pointer.
			GetCurrentPointerPosition = static () => new Point(9.0, 9.0),
			BuildEvaluationState = offset => offset == 5
				? CreateState(requestOffset: 7, canShowDiagnosticFallback: true, diagnosticInfo: CreateDiagnostic("first"))
				: CreateState(requestOffset: 7, canShowDiagnosticFallback: true, diagnosticInfo: CreateDiagnostic("second"))
		};

		using var controller = host.CreateController();
		await controller.HandleMouseHoverAsync(HoverTestHost.CreateMouseEventArgs());

		// The pointer moved to a different hovered offset that maps to the same request, so the display
		// state is rebuilt from the latest evaluation instead of reusing the earlier state.
		Assert.AreEqual(1, host.DisplayCalls.Count);
		Assert.AreEqual("hover", host.DisplayCalls[0].HoverInfo?.Content);
		Assert.AreEqual("second", host.DisplayCalls[0].DiagnosticInfo?.Message);
	}

	[TestMethod]
	public async Task HandleMouseHoverAsync_RemappedRequestOffsetWithoutHook_LogsWarningOnceWithEventId()
	{
		var logger = new CapturingLogger();
		using var host = new HoverTestHost
		{
			BuildEvaluationState = _ => CreateState(requestOffset: 7),
			RequestHoverAsync = static (_, _) => Task.FromResult<TextHoverInfo?>(null)
		};

		using var controller = host.CreateController(logger);

		await controller.HandleMouseHoverAsync(HoverTestHost.CreateMouseEventArgs());
		await controller.HandleMouseHoverAsync(HoverTestHost.CreateMouseEventArgs());

		// A remapped request offset without the matching hook can never match the pointer position, so
		// the host is warned once with the documented hover event id instead of silently discarding every
		// completed result.
		Assert.AreEqual(1, logger.Entries.Count);
		Assert.AreEqual(Microsoft.Extensions.Logging.LogLevel.Warning, logger.Entries[0].Level);
		Assert.AreEqual(1012, logger.Entries[0].EventId.Id);
		StringAssert.Contains(logger.Messages[0], "ResolveRequestOffset");
	}

	[TestMethod]
	public void HandleMouseHoverAsync_WithPointerPositionHook_UsesTheHookPositionForTheLivenessCheck()
	{
		int offsetCalls = 0;
		Point livenessPosition = default;
		var completion = new TaskCompletionSource<TextHoverInfo?>(TaskCreationOptions.RunContinuationsAsynchronously);

		using var host = new HoverTestHost
		{
			// The initial evaluation accepts the event position; the liveness re-check reports the
			// pointer as left the target, so the completed result must be discarded.
			GetOffsetFromPoint = point =>
			{
				if (offsetCalls++ == 0)
					return 5;

				livenessPosition = point;
				return null;
			},
			RequestHoverAsync = (_, _) => completion.Task,
			GetCurrentPointerPosition = static () => new Point(7.0, 7.0)
		};

		using var controller = host.CreateController();

		Task hoverTask = controller.HandleMouseHoverAsync(HoverTestHost.CreateMouseEventArgs());
		completion.TrySetResult(new TextHoverInfo("hover") { SymbolName = "symbol" });
		DispatcherTestUtils.PumpUntil(() => hoverTask.IsCompleted);
		hoverTask.GetAwaiter().GetResult();

		// The pointer hook, not the event, supplied the re-check position.
		Assert.AreEqual(new Point(7.0, 7.0), livenessPosition);
		CollectionAssert.AreEqual(Array.Empty<(TextHoverInfo?, TextDiagnostic?)>(), host.DisplayCalls);
	}

	[TestMethod]
	public void HandleMouseHoverAsync_AsyncProviderWithoutSynchronizationContext_DisplaysOnTheOwnerThread()
	{
		var providerGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var providerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		int displayThreadId = 0;

		using var host = new HoverTestHost
		{
			RequestHoverAsync = async (_, _) =>
			{
				providerStarted.TrySetResult();
				await providerGate.Task.ConfigureAwait(false);
				return new TextHoverInfo("hover") { SymbolName = "symbol" };
			}
		};

		// The test body already runs on the Avalonia UI thread, which owns the host window, so the captured
		// thread id is the owner thread the display callback must run on.
		int ownerThreadId = Environment.CurrentManagedThreadId;
		host.OnDisplay = (_, _) => displayThreadId = Environment.CurrentManagedThreadId;

		using var controller = host.CreateController();

		// A provider continuation that releases the captured context resumes on a thread-pool thread; the
		// pointer resolution and the display callback must still run on the owner's thread instead of
		// touching tooltip state from there.
		Task hoverTask = controller.HandleMouseHoverAsync(HoverTestHost.CreateMouseEventArgs());

		// Wait on the provider's start signal instead of a fixed pump window, so the gate is released only
		// once the request is genuinely in flight.
		DispatcherTestUtils.PumpUntil(() => providerStarted.Task.IsCompleted);
		providerGate.SetResult();
		DispatcherTestUtils.PumpUntil(() => hoverTask.IsCompleted);

		Assert.AreEqual(1, host.DisplayCalls.Count);
		Assert.AreEqual(ownerThreadId, displayThreadId);
	}

	[TestMethod]
	public void HandleMouseHoverAsync_ContextVersionChangedWhileRequestInFlight_DoesNotShowTooltip()
	{
		int contextVersion = 1;
		var completion = new TaskCompletionSource<TextHoverInfo?>(TaskCreationOptions.RunContinuationsAsynchronously);

		using var host = new HoverTestHost
		{
			RequestHoverAsync = (_, _) => completion.Task,
			ContextVersionProvider = () => contextVersion
		};

		using var controller = host.CreateController();

		Task hoverTask = controller.HandleMouseHoverAsync(HoverTestHost.CreateMouseEventArgs());

		// The host context version changes while the request is in flight, so the completed result
		// belongs to a context that no longer exists and must not be published.
		contextVersion = 2;
		completion.TrySetResult(new TextHoverInfo("hover") { SymbolName = "symbol" });
		DispatcherTestUtils.PumpUntil(() => hoverTask.IsCompleted);
		hoverTask.GetAwaiter().GetResult();

		CollectionAssert.AreEqual(Array.Empty<(TextHoverInfo?, TextDiagnostic?)>(), host.DisplayCalls);
	}
}
