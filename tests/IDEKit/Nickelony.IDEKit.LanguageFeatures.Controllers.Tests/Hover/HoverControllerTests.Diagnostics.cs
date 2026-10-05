using Nickelony.IDEKit.Core.Diagnostics;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Hover;
using Nickelony.IDEKit.LanguageFeatures.Diagnostics;
using Nickelony.IDEKit.LanguageFeatures.Hover;
using Nickelony.Testing;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Tests;

/// <summary>
/// Covers the hover controller's diagnostic-fallback and failure-containment behavior: which display decision a provider failure,
/// an unavailable tooltip, or a canceled evaluation resolves to, and which failures are reported through the documented event ids.
/// </summary>
public sealed partial class HoverControllerTests
{
	[TestMethod]
	public async Task RequestAsync_ThrowingProvider_ReportsTheDiagnosticFallbackWithoutThrowing()
	{
		var logger = new RecordingLogger();

		using var host = new HoverTestHost
		{
			BuildDecision = _ => CreateState(canShowDiagnosticFallback: true, diagnosticInfo: CreateDiagnostic()),
			RequestHoverAsync = (_, _) => throw new InvalidOperationException("Hover failed."),
		};

		using HoverController controller = host.CreateController(logger);
		await controller.RequestAsync();

		// A provider failure must not propagate; available diagnostic content is shown instead, and the
		// failure carries the documented hover request event id.
		Assert.HasCount(1, host.DisplayCalls);
		Assert.IsNull(host.DisplayCalls[0].HoverInfo);
		Assert.AreEqual("diagnostic", host.DisplayCalls[0].Diagnostic?.Message);
		Assert.HasCount(1, logger.Entries);
		Assert.AreEqual(1010, logger.Entries[0].EventId.Id);
	}

	[TestMethod]
	public async Task RequestAsync_WithoutAProvider_ShowsTheDiagnosticOfTheDecisionAndAsksForNoHover()
	{
		using var host = new HoverTestHost { BuildDecision = _ => CreateState(canShowDiagnosticFallback: true, diagnosticInfo: CreateDiagnostic()) };

		HoverControllerHooks hooks = host.CreateHooks();
		using var controller = new HoverController(pointerSource: host.PointerSource, provider: null, hooks, dispatcher: host.Dispatcher);

		await controller.RequestAsync();

		// The decision asks for a hover, but no provider exists to ask, so the evaluation concludes with the diagnostic alone.
		Assert.HasCount(1, host.DisplayCalls);
		Assert.IsNull(host.DisplayCalls[0].HoverInfo);
		Assert.AreEqual("diagnostic", host.DisplayCalls[0].Diagnostic?.Message);
		Assert.AreSequenceEqual([], host.RequestOffsets);
	}

	[TestMethod]
	public async Task RequestAsync_WithoutAProviderAndADecisionWithoutADiagnostic_HidesThePopup()
	{
		using var host = new HoverTestHost();
		HoverControllerHooks hooks = host.CreateHooks();
		using var controller = new HoverController(pointerSource: host.PointerSource, provider: null, hooks, dispatcher: host.Dispatcher);

		await controller.RequestAsync();

		Assert.HasCount(1, host.DisplayCalls);
		Assert.IsNull(host.DisplayCalls[0].HoverInfo);
		Assert.IsNull(host.DisplayCalls[0].Diagnostic);
	}

	[TestMethod]
	public void Constructor_WithoutAProvider_NeedsNoRequestHook_ButAProviderNeedsOne()
	{
		using var host = new HoverTestHost();
		var withoutRequest = new HoverControllerHooks { ShowHover = static (_, _, _) => { } };

		using var controller = new HoverController(
			pointerSource: host.PointerSource,
			provider: null,
			hooks: withoutRequest,
			dispatcher: host.Dispatcher
		);

		Assert.ThrowsExactly<ArgumentNullException>(() =>
			new HoverController(pointerSource: host.PointerSource, provider: host, hooks: withoutRequest, dispatcher: host.Dispatcher)
		);
	}

	[TestMethod]
	public async Task RequestAsync_ThrowingOffsetResolution_LogsTheFailureWithoutDisplayDecision()
	{
		var logger = new RecordingLogger();

		using var host = new HoverTestHost();
		host.PointerSource.Offset = static () => throw new InvalidOperationException("Offset resolution failed.");

		using HoverController controller = host.CreateController(logger);

		// A failure before a hovered offset exists has no display target, so only the log reports it and
		// the host tooltip is not touched.
		await controller.RequestAsync();

		Assert.AreSequenceEqual(Array.Empty<(HoverInformation?, Diagnostic?)>(), host.DisplayCalls);
		Assert.AreSequenceEqual([], host.RequestOffsets);
		Assert.HasCount(1, logger.Entries);
		Assert.AreEqual(1010, logger.Entries[0].EventId.Id);
	}

	[TestMethod]
	public async Task RequestAsync_ThrowingDisplayCallback_IsContained()
	{
		var logger = new RecordingLogger();

		using var host = new HoverTestHost
		{
			BuildDecision = _ => CreateState(canShowDiagnosticFallback: true, diagnosticInfo: CreateDiagnostic()),
			RequestHoverAsync = (_, _) => throw new InvalidOperationException("Hover failed."),
			OnDisplay = (_, _) => throw new InvalidOperationException("Diagnostic tooltip failed."),
		};

		using HoverController controller = host.CreateController(logger);

		// The provider failure falls back to the diagnostic tooltip, whose own failure must be contained
		// instead of escaping the hover evaluation, and reported with the host-callback event id.
		await controller.RequestAsync();

		Assert.HasCount(1, host.DisplayCalls);
		Assert.IsNull(host.DisplayCalls[0].HoverInfo);
		Assert.IsNotNull(host.DisplayCalls[0].Diagnostic);

		// The provider failure is reported first, then the callback failure of the diagnostic fallback.
		Assert.HasCount(2, logger.Entries);
		Assert.AreEqual(1010, logger.Entries[0].EventId.Id);
		Assert.AreEqual(1011, logger.Entries[1].EventId.Id);
	}

	[TestMethod]
	public async Task RequestAsync_ContextVersionProviderThrowing_IsContainedAndShowsNothing()
	{
		int contextVersionCalls = 0;

		using var host = new HoverTestHost
		{
			BuildDecision = _ => CreateState(canShowDiagnosticFallback: true, diagnosticInfo: CreateDiagnostic()),

			ContextVersionProvider = () =>
			{
				contextVersionCalls++;

				// The first call captures the version before the request; the host's liveness check then
				// fails with a non-cancellation exception.
				if (contextVersionCalls > 1)
					throw new InvalidOperationException("The host session ended.");

				return 1;
			},
		};

		var logger = new RecordingLogger();
		using HoverController controller = host.CreateController(logger);

		// A throwing liveness check is contained like a provider failure and logged. The diagnostic fallback needs the same check to pass, so
		// with the check failing the host's tooltip stays as it is, and the second failure is reported as a host-callback failure.
		await controller.RequestAsync();

		Assert.AreSequenceEqual(Array.Empty<(HoverInformation?, Diagnostic?)>(), host.DisplayCalls);
		Assert.HasCount(2, logger.Entries);
		Assert.AreEqual(1010, logger.Entries[0].EventId.Id);
		Assert.AreEqual(1011, logger.Entries[1].EventId.Id);
	}

	[TestMethod]
	public async Task RequestAsync_PointerSourceThrowingOnThePublicationRead_IsContainedAndShowsNothing()
	{
		int reads = 0;

		using var host = new HoverTestHost { BuildDecision = _ => CreateState(canShowDiagnosticFallback: true, diagnosticInfo: CreateDiagnostic()) };

		host.PointerSource.Offset = () => ++reads == 1 ? 5 : throw new InvalidOperationException("The pointer source failed.");

		var logger = new RecordingLogger();
		using HoverController controller = host.CreateController(logger);

		// The second read happens when the completed result is about to be shown; its failure is contained like a provider failure. The fallback
		// reads the pointer again, so a source that keeps failing leaves the host's tooltip as it is.
		await controller.RequestAsync();

		Assert.AreSequenceEqual(Array.Empty<(HoverInformation?, Diagnostic?)>(), host.DisplayCalls);
		Assert.HasCount(2, logger.Entries);
		Assert.AreEqual(1010, logger.Entries[0].EventId.Id);
		Assert.AreEqual(1011, logger.Entries[1].EventId.Id);
	}

	[TestMethod]
	public void RequestAsync_ProviderFailsAfterThePointerLeft_ShowsNothingAndLogsTheFailure()
	{
		var logger = new RecordingLogger();
		var completion = new TaskCompletionSource<HoverInformation?>(TaskCreationOptions.RunContinuationsAsynchronously);

		using var host = new HoverTestHost
		{
			BuildDecision = _ => CreateState(canShowDiagnosticFallback: true, diagnosticInfo: CreateDiagnostic()),
			RequestHoverAsync = (_, _) => completion.Task,
		};

		using HoverController controller = host.CreateController(logger);

		Task request = controller.RequestAsync();

		// The pointer leaves the editor while the request is in flight, so a failure that arrives now has no target to fall back on.
		host.PointerSource.Offset = static () => null;
		completion.TrySetException(new InvalidOperationException("Hover failed."));
		host.Dispatcher.PumpUntil(() => request.IsCompleted);
		request.GetAwaiter().GetResult();

		Assert.AreSequenceEqual(Array.Empty<(HoverInformation?, Diagnostic?)>(), host.DisplayCalls);
		Assert.HasCount(1, logger.Entries);
		Assert.AreEqual(1010, logger.Entries[0].EventId.Id);
	}

	[TestMethod]
	public void RequestAsync_ProviderFailsAfterTheHostContextChanged_ShowsNothingAndLogsTheFailure()
	{
		var logger = new RecordingLogger();
		var completion = new TaskCompletionSource<HoverInformation?>(TaskCreationOptions.RunContinuationsAsynchronously);

		int contextVersion = 1;

		using var host = new HoverTestHost
		{
			BuildDecision = _ => CreateState(canShowDiagnosticFallback: true, diagnosticInfo: CreateDiagnostic()),
			ContextVersionProvider = () => contextVersion,
			RequestHoverAsync = (_, _) => completion.Task,
		};

		using HoverController controller = host.CreateController(logger);

		Task request = controller.RequestAsync();

		// The host replaced its document while the request was in flight, so the diagnostic of the evaluated state describes a document that is gone.
		contextVersion = 2;
		completion.TrySetException(new InvalidOperationException("Hover failed."));
		host.Dispatcher.PumpUntil(() => request.IsCompleted);
		request.GetAwaiter().GetResult();

		Assert.AreSequenceEqual(Array.Empty<(HoverInformation?, Diagnostic?)>(), host.DisplayCalls);
		Assert.HasCount(1, logger.Entries);
		Assert.AreEqual(1010, logger.Entries[0].EventId.Id);
	}

	[TestMethod]
	public void RequestAsync_ProviderFailsAfterThePointerMovedToAnotherRequestOffset_ShowsNothing()
	{
		var completion = new TaskCompletionSource<HoverInformation?>(TaskCreationOptions.RunContinuationsAsynchronously);

		using var host = new HoverTestHost
		{
			BuildDecision = _ => CreateState(canShowDiagnosticFallback: true, diagnosticInfo: CreateDiagnostic()),
			RequestHoverAsync = (_, _) => completion.Task,
		};

		using HoverController controller = host.CreateController();

		Task request = controller.RequestAsync();

		// Without a request-offset hook the pointer addresses its own offset, which differs from the one the evaluation requested.
		host.PointerSource.Offset = static () => 7;
		completion.TrySetException(new InvalidOperationException("Hover failed."));
		host.Dispatcher.PumpUntil(() => request.IsCompleted);
		request.GetAwaiter().GetResult();

		Assert.AreSequenceEqual(Array.Empty<(HoverInformation?, Diagnostic?)>(), host.DisplayCalls);
	}

	[TestMethod]
	public void RequestAsync_ProviderFailsAfterThePointerMovedWithinTheSameRequestOffset_ShowsTheFallbackOfTheCurrentOffset()
	{
		var completion = new TaskCompletionSource<HoverInformation?>(TaskCreationOptions.RunContinuationsAsynchronously);

		using var host = new HoverTestHost
		{
			BuildDecision = offset =>
				CreateState(canShowDiagnosticFallback: true, diagnosticInfo: CreateDiagnostic(message: $"diagnostic at {offset}")),

			ResolveRequestOffset = static _ => 5,
			RequestHoverAsync = (_, _) => completion.Task,
		};

		using HoverController controller = host.CreateController();

		Task request = controller.RequestAsync();

		// Both offsets resolve to the requested one, so the failure still has a target, and its fallback describes the offset under the pointer.
		host.PointerSource.Offset = static () => 6;
		completion.TrySetException(new InvalidOperationException("Hover failed."));
		host.Dispatcher.PumpUntil(() => request.IsCompleted);
		request.GetAwaiter().GetResult();

		Assert.HasCount(1, host.DisplayCalls);
		Assert.IsNull(host.DisplayCalls[0].HoverInfo);
		Assert.AreEqual("diagnostic at 6", host.DisplayCalls[0].Diagnostic?.Message);
	}

	[TestMethod]
	public async Task RequestAsync_StateBuilderFailsForANewerEvaluation_SupersedesTheOutstandingRequest()
	{
		var completion = new TaskCompletionSource<HoverInformation?>(TaskCreationOptions.RunContinuationsAsynchronously);

		using var host = new HoverTestHost { RequestHoverAsync = (_, _) => completion.Task };
		using HoverController controller = host.CreateController();

		Task first = controller.RequestAsync();

		// The newest evaluation fails before it can admit a request; it still ends the older one, like an evaluation that concludes without a request.
		host.BuildDecision = static _ => throw new InvalidOperationException("The state builder failed.");
		await controller.RequestAsync();

		Assert.IsTrue(host.RequestTokens[0].IsCancellationRequested);

		completion.TrySetResult(new HoverInformation("hover") { SymbolName = "symbol" });
		host.Dispatcher.PumpUntil(() => first.IsCompleted);
		await first;

		// Only the hide of the failed evaluation was shown; the older result did not undo it.
		Assert.HasCount(1, host.DisplayCalls);
		Assert.IsNull(host.DisplayCalls[0].HoverInfo);
		Assert.IsNull(host.DisplayCalls[0].Diagnostic);
	}

	[TestMethod]
	public async Task RequestAsync_ThrowingRequestBuilder_FallsBackToTheDiagnosticAndLogsTheFailure()
	{
		var logger = new RecordingLogger();

		using var host = new HoverTestHost
		{
			BuildDecision = _ => CreateState(canShowDiagnosticFallback: true, diagnosticInfo: CreateDiagnostic()),
			BuildRequest = static _ => throw new InvalidOperationException("The request builder failed."),
		};

		using HoverController controller = host.CreateController(logger);

		// A failure before any request was admitted has no request to own the decision; the evaluation state already resolved the
		// diagnostic, so the fallback is the display decision and the failure is logged.
		await controller.RequestAsync();

		Assert.AreSequenceEqual([], host.RequestOffsets);
		Assert.HasCount(1, host.DisplayCalls);
		Assert.IsNull(host.DisplayCalls[0].HoverInfo);
		Assert.AreEqual("diagnostic", host.DisplayCalls[0].Diagnostic?.Message);
		Assert.HasCount(1, logger.Entries);
		Assert.AreEqual(1010, logger.Entries[0].EventId.Id);
	}

	[TestMethod]
	public async Task RequestAsync_ThrowingDecisionBuilder_ReportsHideAndLogsTheFailure()
	{
		var logger = new RecordingLogger();

		using var host = new HoverTestHost { BuildDecision = static _ => throw new InvalidOperationException("The state builder failed.") };

		using HoverController controller = host.CreateController(logger);

		// A hovered offset was resolved, so the failure ends in a hide decision; no state exists to supply a fallback.
		await controller.RequestAsync();

		Assert.AreSequenceEqual([], host.RequestOffsets);
		Assert.HasCount(1, host.DisplayCalls);
		Assert.IsNull(host.DisplayCalls[0].HoverInfo);
		Assert.IsNull(host.DisplayCalls[0].Diagnostic);
		Assert.HasCount(1, logger.Entries);
		Assert.AreEqual(1010, logger.Entries[0].EventId.Id);
	}

	[TestMethod]
	public async Task RequestAsync_SuccessfulRequestWithFallbackDisabled_StillCombinesBoth()
	{
		using var host = new HoverTestHost { BuildDecision = _ => CreateState(canShowDiagnosticFallback: false, diagnosticInfo: CreateDiagnostic()) };

		using HoverController controller = host.CreateController();
		await controller.RequestAsync();

		// The fallback flag gates only the not-made and failed paths; a successful request combines the
		// hover content with the diagnostic even when the fallback is disabled.
		Assert.HasCount(1, host.DisplayCalls);
		Assert.AreEqual("hover", host.DisplayCalls[0].HoverInfo?.Content);
		Assert.AreEqual("diagnostic", host.DisplayCalls[0].Diagnostic?.Message);
	}

	[TestMethod]
	public async Task RequestAsync_SuccessfulRequestWithDiagnostic_DisplaysBothInOneCall()
	{
		using var host = new HoverTestHost { BuildDecision = _ => CreateState(canShowDiagnosticFallback: true, diagnosticInfo: CreateDiagnostic()) };

		using HoverController controller = host.CreateController();
		await controller.RequestAsync();

		// A successful hover next to diagnostic content is reported as one combined display decision.
		Assert.HasCount(1, host.DisplayCalls);
		Assert.AreEqual("hover", host.DisplayCalls[0].HoverInfo?.Content);
		Assert.AreEqual("diagnostic", host.DisplayCalls[0].Diagnostic?.Message);
	}

	[TestMethod]
	public async Task RequestAsync_HoverNotRequested_DisplaysTheDiagnosticFallbackAndSkipsTheProvider()
	{
		using var host = new HoverTestHost
		{
			BuildDecision = _ =>
				CreateState(
					shouldRequestHover: false,
					requestOffset: -1,
					canShowHoverContent: false,
					canShowDiagnosticFallback: true,
					diagnosticInfo: CreateDiagnostic(severity: DiagnosticSeverity.Warning)
				),
		};

		using HoverController controller = host.CreateController();
		await controller.RequestAsync();

		// When hover is not applicable, only the diagnostic tooltip is shown and no provider call is made.
		Assert.HasCount(1, host.DisplayCalls);
		Assert.IsNull(host.DisplayCalls[0].HoverInfo);
		Assert.AreEqual("diagnostic", host.DisplayCalls[0].Diagnostic?.Message);
		Assert.AreSequenceEqual([], host.RequestOffsets);
	}

	[TestMethod]
	public async Task RequestAsync_ContextVersionProviderThrowingOperationCanceled_EndsSilently()
	{
		int contextVersionCalls = 0;

		using var host = new HoverTestHost
		{
			BuildDecision = _ => CreateState(canShowDiagnosticFallback: true, diagnosticInfo: CreateDiagnostic()),

			ContextVersionProvider = () =>
			{
				contextVersionCalls++;

				// The first call captures the version before the request; the host's session ends by the
				// time the completed result is checked, so the check cancels the evaluation.
				if (contextVersionCalls > 1)
					throw new OperationCanceledException();

				return 1;
			},
		};

		using HoverController controller = host.CreateController();

		// The explicit cancellation must not escape and must not fall back to the diagnostic tooltip,
		// even though fallback content is available.
		await controller.RequestAsync();

		Assert.AreSequenceEqual(Array.Empty<(HoverInformation?, Diagnostic?)>(), host.DisplayCalls);
	}

	[TestMethod]
	public async Task RequestAsync_ProviderThrowingOperationCanceled_EndsWithoutDisplay()
	{
		var logger = new RecordingLogger();
		using var host = new HoverTestHost { RequestHoverAsync = static (_, _) => throw new OperationCanceledException() };

		using HoverController controller = host.CreateController(logger);
		await controller.RequestAsync();

		// A canceled provider reports cancellation rather than a failure: nothing is displayed and nothing
		// is logged.
		Assert.AreSequenceEqual(Array.Empty<(HoverInformation?, Diagnostic?)>(), host.DisplayCalls);
		Assert.AreSequenceEqual([], logger.Messages);
	}

	[TestMethod]
	public async Task RequestAsync_CompletedRequestWithHoverContentForbidden_ReportsHide()
	{
		using var host = new HoverTestHost { BuildDecision = _ => CreateState(canShowHoverContent: false, diagnosticInfo: CreateDiagnostic()) };

		using HoverController controller = host.CreateController();
		await controller.RequestAsync();

		// A completed request whose display state forbids hover content resolves to a hide decision, even
		// when a diagnostic was resolved alongside it.
		Assert.HasCount(1, host.DisplayCalls);
		Assert.IsNull(host.DisplayCalls[0].HoverInfo);
		Assert.IsNull(host.DisplayCalls[0].Diagnostic);
		Assert.AreSequenceEqual([5], host.RequestOffsets);
	}

	[TestMethod]
	public async Task RequestAsync_ThrowingProviderWithoutDiagnosticFallback_ReportsHide()
	{
		var logger = new RecordingLogger();
		using var host = new HoverTestHost { RequestHoverAsync = (_, _) => throw new InvalidOperationException("Hover failed.") };

		using HoverController controller = host.CreateController(logger);
		await controller.RequestAsync();

		// Without fallback permission the failure resolves to a hide decision, and the failure is still
		// reported with the documented request event id.
		Assert.HasCount(1, host.DisplayCalls);
		Assert.IsNull(host.DisplayCalls[0].HoverInfo);
		Assert.IsNull(host.DisplayCalls[0].Diagnostic);
		Assert.HasCount(1, logger.Entries);
		Assert.AreEqual(1010, logger.Entries[0].EventId.Id);
	}
}
